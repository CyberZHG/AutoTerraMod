using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Systems;

public sealed class BotWorldSystem : ModSystem
{
	public const int FirstAvatarSlot = 200;
	const int BaseSpacingTiles = 240;
	const int WorldEdgeMargin = 150;

	public static readonly List<Faction> Factions = new();
	public static readonly List<Bot> Bots = new();
	static readonly List<Faction> dormantFactions = new();
	static readonly List<Bot> dormantBots = new();
	public static FactionRelation Relation { get; private set; }
	public static WorldFactionSettings Settings { get; private set; }
	static FactionKnowledge sharedKnowledge = new();
	static bool initialized;
	static int nextBotId;

	public static bool IsRunning => initialized && Main.netMode != NetmodeID.MultiplayerClient;

	public static Bot FindBot(int id)
	{
		foreach (var bot in Bots)
		{
			if (bot.Id == id)
				return bot;
		}
		return null;
	}

	public static FactionKnowledge KnowledgeOf(Faction faction) =>
		Relation == FactionRelation.Cooperate ? sharedKnowledge : faction.Knowledge;

	public override void OnWorldLoad()
	{
		Clear();
		using var _ = Perf.Measure(PerfSection.Planner);
		ResourceSources.Warm();
		Planning.Shops.Warm();
		ProgressionPlanner.Warm();
	}

	public override void OnWorldUnload()
	{
		ReleaseAvatarSlots();
		Clear();
	}

	static void Clear()
	{
		Factions.Clear();
		Bots.Clear();
		dormantFactions.Clear();
		dormantBots.Clear();
		sharedKnowledge = new FactionKnowledge();
		World.SharedAltar.Origin = null;
		World.TilePresence.Reset();
		Relation = FactionRelation.Race;
		Settings = null;
		initialized = false;
		nextBotId = 0;
		Stations.Reset();
		HouseInspector.Reset();
		BaseStorage.Reset();
		ItemReservations.Clear();
		ResourceSources.Reset();
		World.TreeSupply.Reset();
		Planning.Shops.Reset();
		ProgressionPlanner.Reset();
		Bosses.BossCredit.Reset();
		World.WaterFinder.Reset();
		Navigation.DangerSpots.Reset();
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Settings != null)
			tag["settings"] = Settings.Save();
		if (Factions.Count == 0)
			return;
		tag["relation"] = (int)Relation;
		tag["nextBotId"] = nextBotId;
		tag["factions"] = Factions.Concat(dormantFactions).OrderBy(faction => faction.Id).Select(faction => faction.Save()).ToList();
		tag["bots"] = Bots.Concat(dormantBots).OrderBy(bot => bot.Id).Select(bot => bot.Save()).ToList();
		tag["sharedKnowledge"] = sharedKnowledge.Save();
		if (World.SharedAltar.Origin is Microsoft.Xna.Framework.Point altar)
			tag["sharedAltar"] = new Microsoft.Xna.Framework.Vector2(altar.X, altar.Y);
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Relation = (FactionRelation)tag.GetInt("relation");
		nextBotId = tag.GetInt("nextBotId");
		Factions.AddRange(tag.GetList<TagCompound>("factions").Select(Faction.Load));
		Bots.AddRange(tag.GetList<TagCompound>("bots").Select(Bot.Load));
		if (tag.ContainsKey("sharedKnowledge"))
			sharedKnowledge.Load(tag.GetCompound("sharedKnowledge"));
		if (tag.ContainsKey("sharedAltar"))
			World.SharedAltar.Origin = tag.Get<Microsoft.Xna.Framework.Vector2>("sharedAltar").ToPoint();
		Settings = tag.ContainsKey("settings") ? WorldFactionSettings.Load(tag.GetCompound("settings"))
			: Factions.Count > 0 ? new WorldFactionSettings(Factions.Count, Factions.Max(MembersOf), Relation)
			: null;
	}

	static int MembersOf(Faction faction) => Bots.Count(bot => bot.FactionId == faction.Id);

	public static void ChangeSettings(WorldFactionSettings settings)
	{
		Settings = settings;
		Relation = HeadlessTestMode.RelationOverride ?? settings.Relation;
		if (!initialized)
			return;
		var wasActive = Bots.ToHashSet();
		foreach (var bot in Bots)
			bot.SavedFeetPosition = bot.Npc?.Bottom;
		ReleaseAvatarSlots();
		GrowToConfig();
		DesignMissingHouses();
		foreach (var bot in dormantBots.Where(wasActive.Contains))
			Hide(bot);
		AssignAvatarSlots();
		foreach (var bot in Bots)
		{
			if (!wasActive.Contains(bot))
				Spawn(bot, bot.SavedFeetPosition ?? bot.Faction.BaseFeet);
			bot.SavedFeetPosition = null;
		}
	}

	static void Hide(Bot bot)
	{
		if (bot.Npc is { } npc)
		{
			bot.Grapple.Release(npc, jump: false);
			npc.active = false;
		}
		bot.NpcIndex = -1;
		BotDiagnostics.LogWorld($"{bot.Name} hidden by the faction settings");
	}

	static void ApplyLimits(int botsPerFaction)
	{
		Factions.AddRange(dormantFactions);
		Bots.AddRange(dormantBots);
		dormantFactions.Clear();
		dormantBots.Clear();
		Factions.Sort((a, b) => a.Id.CompareTo(b.Id));
		Bots.Sort((a, b) => a.Id.CompareTo(b.Id));
		while (Factions.Count > Settings.FactionCount)
		{
			dormantFactions.Insert(0, Factions[^1]);
			Factions.RemoveAt(Factions.Count - 1);
		}
		var members = new Dictionary<int, int>();
		for (int i = 0; i < Bots.Count; i++)
		{
			var bot = Bots[i];
			int rank = members.GetValueOrDefault(bot.FactionId);
			members[bot.FactionId] = rank + 1;
			if (bot.FactionId < Factions.Count && rank < botsPerFaction)
				continue;
			dormantBots.Add(bot);
			Bots.RemoveAt(i--);
		}
	}

	public override void PreUpdateWorld()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient && !initialized)
			Initialize();
	}

	public override void PostUpdateNPCs()
	{
		if (!IsRunning)
			return;
		foreach (var bot in Bots)
		{
			if (!bot.IsAlive && --bot.RespawnTicks <= 0)
				Spawn(bot, RespawnFeet(bot));
		}
		Navigation.DangerSpots.Prune();
	}

	static void Initialize()
	{
		GrowToConfig();
		DesignMissingHouses();
		HeadlessTestMode.PrepareScenario();
		foreach (var faction in Factions)
			BotDiagnostics.LogWorld($"[{faction.Name}] base site {faction.BaseTile}:{TerrainDump.Around(faction.BaseTile, 45, 25)}");
		AssignAvatarSlots();
		foreach (var bot in Bots)
		{
			Spawn(bot, bot.SavedFeetPosition ?? bot.Faction.BaseFeet);
			bot.SavedFeetPosition = null;
		}
		initialized = true;
	}

	const int MinimumBaseSeparation = 150;

	static void GrowToConfig()
	{
		Settings ??= WorldFactionSettings.FromConfig(ModContent.GetInstance<AutoTerraConfig>());
		Relation = HeadlessTestMode.RelationOverride ?? Settings.Relation;
		int botsPerFaction = HeadlessTestMode.BotsPerFactionOverride ?? Settings.BotsPerFaction;
		ApplyLimits(botsPerFaction);
		bool firstSetup = Factions.Count == 0;
		for (int index = Factions.Count; index < Settings.FactionCount; index++)
		{
			var baseTile = firstSetup ? FindBaseTile(index, Settings.FactionCount) : FindBaseTileAwayFromOthers();
			Factions.Add(new Faction { Id = index, BaseTile = baseTile });
		}
		foreach (var faction in Factions)
		{
			int members = Bots.Count(bot => bot.FactionId == faction.Id);
			if (members >= botsPerFaction)
				continue;
			for (; members < botsPerFaction; members++)
				Bots.Add(Bot.Create(nextBotId++, faction, Main.rand, Bots.Concat(dormantBots).Select(bot => bot.Name).ToHashSet()));
			faction.ForgetSettlement();
		}
		Building.HouseInspector.Reset();
		Building.BaseStorage.Reset();
	}

	static Point FindBaseTileAwayFromOthers()
	{
		for (int step = 1; step < Main.maxTilesX / BaseSpacingTiles; step++)
		{
			foreach (int direction in new[] { 1, -1 })
			{
				int x = Main.spawnTileX + direction * step * BaseSpacingTiles;
				if (x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
					continue;
				if (Factions.All(faction => System.Math.Abs(faction.BaseTile.X - x) >= MinimumBaseSeparation) && FindBaseTileNear(x) is Point site)
					return site;
			}
		}
		return FindBaseTileNear(Main.spawnTileX) ?? SurfaceSite(Main.spawnTileX);
	}

	static void DesignMissingHouses()
	{
		foreach (var faction in Factions.Where(faction => faction.HouseDesign == null))
		{
			var builder = Bots.Where(bot => bot.FactionId == faction.Id).MaxBy(bot => bot.Personality.Building);
			if (builder != null)
				faction.HouseDesign = HouseDesign.Roll(builder.Personality, Main.rand);
		}
	}

	const int BaseSearchRadius = 60;
	const int FlatnessHalfWidth = 8;
	const int SolidGroundDepth = 25;

	static Point FindBaseTile(int index, int count)
	{
		int targetX = Main.spawnTileX + (int)((index - (count - 1) / 2f) * BaseSpacingTiles);
		int awayFromSpawn = targetX >= Main.spawnTileX ? 1 : -1;
		for (int shift = 0; shift <= MaxEvilDetour; shift += BaseSearchRadius)
		{
			if (FindBaseTileNear(targetX + awayFromSpawn * shift) is Point site)
				return site;
		}
		return SurfaceSite(targetX);
	}

	const int MaxEvilDetour = 600;
	const int EvilClearance = 40;
	const int EvilDepth = 30;

	static Point SurfaceSite(int x)
	{
		x = Utils.Clamp(x, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		return new Point(x, TileQuery.SurfaceY(x) - 1);
	}

	static bool NearEvil(int centerX)
	{
		for (int x = centerX - EvilClearance; x <= centerX + EvilClearance; x += 2)
		{
			if (!WorldGen.InWorld(x, 10, 10))
				continue;
			int surface = TileQuery.SurfaceY(x);
			for (int y = surface - 5; y <= surface + EvilDepth; y += 2)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && (TileID.Sets.Corrupt[tile.TileType] || TileID.Sets.Crimson[tile.TileType]))
					return true;
			}
		}
		return false;
	}

	static Point? FindBaseTileNear(int targetX)
	{
		int preferredX = Utils.Clamp(targetX, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		int bestX = preferredX;
		float bestScore = float.MaxValue;
		for (int x = preferredX - BaseSearchRadius; x <= preferredX + BaseSearchRadius; x += 2)
		{
			if (x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
				continue;
			float roughness = SiteRoughness(x);
			if (roughness == float.MaxValue || NearEvil(x))
				continue;
			float score = roughness + System.Math.Abs(x - preferredX) * 0.05f;
			if (score < bestScore)
			{
				bestScore = score;
				bestX = x;
			}
		}
		return bestScore == float.MaxValue ? null : new Point(bestX, TileQuery.SurfaceY(bestX) - 1);
	}

	static float SiteRoughness(int centerX)
	{
		int centerY = TileQuery.SurfaceY(centerX);
		if (Main.tile[centerX, centerY - 1].LiquidAmount > 0 || !HasSolidGroundBelow(centerX, centerY))
			return float.MaxValue;
		float roughness = 0f;
		for (int x = centerX - FlatnessHalfWidth; x <= centerX + FlatnessHalfWidth; x++)
			roughness += System.Math.Abs(TileQuery.SurfaceY(x) - centerY);
		return roughness;
	}

	static bool HasSolidGroundBelow(int x, int surfaceY)
	{
		int solid = 0;
		for (int y = surfaceY; y < surfaceY + SolidGroundDepth; y++)
		{
			if (TileQuery.IsSolid(x, y))
				solid++;
		}
		return solid >= SolidGroundDepth * 4 / 5;
	}

	static void AssignAvatarSlots()
	{
		for (int i = 0; i < Bots.Count; i++)
			AssignAvatarSlot(i);
	}

	static void AssignAvatarSlot(int index)
	{
		var avatar = Bots[index].Avatar;
		avatar.whoAmI = FirstAvatarSlot + index;
		avatar.active = false;
		Main.player[avatar.whoAmI] = avatar;
	}

	static void ReleaseAvatarSlots()
	{
		foreach (var bot in Bots)
		{
			int slot = bot.Avatar.whoAmI;
			if (slot >= FirstAvatarSlot && slot < Main.maxPlayers && Main.player[slot] == bot.Avatar)
				Main.player[slot] = new Player();
		}
	}

	public static Vector2 RespawnFeet(Bot bot)
	{
		if (Building.Outpost.ArenaBedFor(bot.Faction) is Point arena)
			return new Vector2(arena.X * 16f + 8f, (arena.Y + 1) * 16f);
		if (bot.OutpostBed is Point outpost && Building.Outpost.IsUsable(outpost))
			return new Vector2(outpost.X * 16f + 8f, (outpost.Y + 1) * 16f);
		var settlement = bot.Faction.Settlement;
		int memberIndex = Bots.Where(member => member.FactionId == bot.FactionId).ToList().IndexOf(bot);
		if (settlement == null || memberIndex < 0 || memberIndex >= settlement.Bedrooms.Count)
			return bot.Faction.BaseFeet;
		var bedroom = settlement.Bedrooms[memberIndex];
		return bedroom.FindTileInside(TileID.Beds) is Point bed
			? new Vector2(bed.X * 16f + 8f, bedroom.Floor * 16f)
			: bot.Faction.BaseFeet;
	}

	static void Spawn(Bot bot, Vector2 feet)
	{
		int index = NPC.NewNPC(new EntitySource_Misc("AutoTerraMod:BotSpawn"), (int)feet.X, (int)feet.Y, ModContent.NPCType<BotNPC>());
		if (index >= Main.maxNPCs)
			return;
		var npc = Main.npc[index];
		((BotNPC)npc.ModNPC).BotId = bot.Id;
		npc.GivenName = bot.Name;
		bot.NpcIndex = index;
		bot.Vitals.OnSpawned(npc);
	}
}
