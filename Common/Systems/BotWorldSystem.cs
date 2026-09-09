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
	const int MinimumBaseSpacing = 240;

	static int BaseSpacingTiles => System.Math.Max(MinimumBaseSpacing, Main.maxTilesX / (2 * System.Math.Max(1, Settings?.FactionCount ?? 1)));
	const int WorldEdgeMargin = 150;

	public static readonly List<Faction> Factions = new();
	public static readonly List<Bot> Bots = new();
	static readonly List<Faction> dormantFactions = new();
	static readonly List<Bot> dormantBots = new();
	public static FactionRelation Relation { get; private set; }
	public static WorldFactionSettings Settings { get; private set; }
	static FactionKnowledge sharedKnowledge = new();
	static bool initialized;
	static bool configShowsWorld;
	static int nextBotId;

	public static bool IsRunning => initialized && Main.netMode != NetmodeID.MultiplayerClient && Settings?.BotsEnabled != false;

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
		configShowsWorld = false;
		World.Landmarks.DrunkCrimson = null;
		nextBotId = 0;
		Stations.Reset();
		HouseInspector.Reset();
		BaseStorage.Reset();
		ItemReservations.Clear();
		ResourceSources.Reset();
		World.TempleDoor.Reset();
		World.SkyArena.Forget();
		World.ShimmerZone.Reset();
		World.MushroomPatch.Reset();
		World.TreeSupply.Reset();
		Planning.Shops.Reset();
		ProgressionPlanner.Reset();
		Bosses.BossCredit.Reset();
		World.Highway.Forget();
		Planning.SurveyRoadGoal.Forget();
		World.WaterFinder.Reset();
		Navigation.DangerSpots.Reset();
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Settings != null)
			tag["settings"] = Settings.Save();
		if (World.Landmarks.DrunkCrimson is bool drunkCrimson)
			tag["drunkCrimson"] = drunkCrimson;
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
		if (tag.ContainsKey("drunkCrimson"))
			World.Landmarks.DrunkCrimson = tag.GetBool("drunkCrimson");
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

	public static List<string> ImportFrom(TagCompound data)
	{
		var notes = new List<string>();
		var source = data.ContainsKey("settings") ? WorldFactionSettings.Load(data.GetCompound("settings")) : null;
		var sourceFactions = data.GetList<TagCompound>("factions").OrderBy(tag => tag.GetInt("id")).ToList();
		int factionLimit = source?.FactionCount ?? sourceFactions.Count;
		var keptFactions = sourceFactions.Where(tag => tag.GetInt("id") < factionLimit).ToList();
		int memberLimit = source?.BotsPerFaction ?? int.MaxValue;
		var allBots = data.GetList<TagCompound>("bots").OrderBy(tag => tag.GetInt("id")).ToList();
		var keptBots = allBots.Where(tag => keptFactions.Any(faction => faction.GetInt("id") == tag.GetInt("faction")))
			.GroupBy(tag => tag.GetInt("faction")).SelectMany(group => group.Take(memberLimit)).ToList();
		if (keptFactions.Count == 0 || keptBots.Count == 0)
		{
			notes.Add(BotImport.Text("NoBots", ""));
			return notes;
		}
		if (allBots.Count > keptBots.Count)
			notes.Add(BotImport.Text("HiddenSkipped", allBots.Count - keptBots.Count));
		int previous = Bots.Count + dormantBots.Count;
		if (previous > 0)
			notes.Add(BotImport.Text("Replaced", previous, Factions.Count + dormantFactions.Count));
		if (!Main.hardMode && keptFactions.Any(tag => tag.ContainsKey("bosses") && tag.GetList<int>("bosses").Contains(NPCID.WallofFlesh)))
			notes.Add(BotImport.Text("HardmodeGear"));

		foreach (var bot in Bots.Where(bot => bot.Npc != null))
			Hide(bot);
		ReleaseAvatarSlots();
		var relation = source?.Relation ?? Relation;
		int materials = Settings?.MaterialMultiplier ?? 1;
		Clear();
		int perFaction = keptFactions.Max(faction => keptBots.Count(bot => bot.GetInt("faction") == faction.GetInt("id")));
		Settings = new WorldFactionSettings(keptFactions.Count, perFaction, relation, true, materials);
		Relation = HeadlessTestMode.RelationOverride ?? relation;
		var factionIds = new Dictionary<int, int>();
		foreach (var tag in keptFactions)
		{
			int id = Factions.Count;
			factionIds[tag.GetInt("id")] = id;
			Factions.Add(new Faction
			{
				Id = id,
				BaseTile = FindBaseTile(id, keptFactions.Count),
				HouseDesign = tag.ContainsKey("house") ? HouseDesign.Load(tag.GetCompound("house")) : null
			});
		}
		int unloadedItems = 0;
		foreach (var tag in keptBots)
		{
			var avatar = AvatarStorage.Load(tag.GetCompound("avatar"));
			unloadedItems += avatar.inventory.Concat(avatar.armor).Concat(avatar.miscEquips).Count(item => item?.ModItem is Terraria.ModLoader.Default.UnloadedItem);
			Bots.Add(new Bot(nextBotId++, factionIds[tag.GetInt("faction")], tag.GetString("name"), Personality.Load(tag.GetCompound("personality")), avatar));
		}
		if (unloadedItems > 0)
			notes.Add(BotImport.Text("UnloadedItems", unloadedItems));
		if (previous > 0)
			notes.Add(BotImport.Text("OldBuildings"));
		configShowsWorld = true;
		Initialize();
		notes.Insert(0, BotImport.Text("Done", Bots.Count, Factions.Count));
		return notes;
	}

	static int MembersOf(Faction faction) => Bots.Count(bot => bot.FactionId == faction.Id);

	public static void ChangeSettings(WorldFactionSettings settings)
	{
		bool wasEnabled = Settings?.BotsEnabled != false;
		Settings = settings;
		Relation = HeadlessTestMode.RelationOverride ?? settings.Relation;
		if (!initialized)
			return;
		if (!settings.BotsEnabled)
		{
			foreach (var bot in Bots.Where(bot => bot.Npc != null))
			{
				bot.SavedFeetPosition = bot.Npc.Bottom;
				Hide(bot);
			}
			return;
		}
		if (!wasEnabled)
		{
			foreach (var bot in Bots)
			{
				Spawn(bot, bot.SavedFeetPosition ?? bot.Faction.BaseFeet);
				bot.SavedFeetPosition = null;
			}
		}
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
		if (Main.netMode == NetmodeID.MultiplayerClient || initialized)
			return;
		Settings ??= WorldFactionSettings.ForNewWorld(ModContent.GetInstance<AutoTerraConfig>());
		if (!configShowsWorld)
		{
			configShowsWorld = true;
			var config = ModContent.GetInstance<AutoTerraConfig>();
			config.BotsEnabled = Settings.BotsEnabled;
			config.FactionCount = Settings.FactionCount;
			config.BotsPerFaction = Settings.BotsPerFaction;
			config.Relation = Settings.Relation;
		}
		if (Settings.BotsEnabled)
			Initialize();
	}

	public override void PostUpdateNPCs()
	{
		if (!IsRunning)
			return;
		foreach (var bot in Bots)
		{
			if (!bot.IsAlive && --bot.RespawnTicks <= 0)
			{
				if (bot.OutpostBed is Point outpost)
					bot.Faction.TouchOutpost(outpost);
				Spawn(bot, RespawnFeetNear(bot, bot.DeathPosition));
			}
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

	static int MinimumBaseSeparation => BaseSpacingTiles * 5 / 8;

	static void GrowToConfig()
	{
		Settings ??= WorldFactionSettings.ForNewWorld(ModContent.GetInstance<AutoTerraConfig>());
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

	const float AllyBedAdvantage = 16f * 100f;

	static Vector2 RespawnFeetNear(Bot bot, Vector2? death)
	{
		var own = RespawnFeet(bot);
		if (death is not Vector2 at || Relation == Config.FactionRelation.War)
			return own;
		var best = own;
		float bestDistance = Vector2.Distance(own, at) - AllyBedAdvantage;
		foreach (var faction in Factions)
		{
			if (faction.Id == bot.FactionId || faction.Settlement is not { } settlement)
				continue;
			foreach (var bedroom in settlement.Bedrooms)
			{
				if (bedroom.FindTileInside(TileID.Beds) is not Point bed)
					continue;
				var feet = new Vector2(bed.X * 16f + 8f, bedroom.Floor * 16f);
				float distance = Vector2.Distance(feet, at);
				if (distance < bestDistance && !Bosses.BossRituals.InPillarZone(feet))
				{
					best = feet;
					bestDistance = distance;
				}
			}
		}
		if (best != own)
			BotDiagnostics.Log(bot, $"respawning at an allied bed {best.ToTileCoordinates()}");
		return best;
	}

	public static Vector2 RespawnFeet(Bot bot)
	{
		if ((Building.Outpost.TempleBed(bot) ?? bot.OutpostBed) is Point outpost && Building.Outpost.BedStands(outpost) && !Bosses.BossRituals.InPillarZone(outpost.ToWorldCoordinates()))
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
