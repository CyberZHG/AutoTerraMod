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
	public static FactionRelation Relation { get; private set; }
	static FactionKnowledge sharedKnowledge = new();
	static bool initialized;
	static int nextBotId;

	public static bool IsRunning => initialized && Main.netMode != NetmodeID.MultiplayerClient;

	public static Bot FindBot(int id) => id < 0 ? null : Bots.Find(bot => bot.Id == id);

	public static FactionKnowledge KnowledgeOf(Faction faction) =>
		Relation == FactionRelation.Cooperate ? sharedKnowledge : faction.Knowledge;

	public override void OnWorldLoad() => Clear();

	public override void OnWorldUnload()
	{
		ReleaseAvatarSlots();
		Clear();
	}

	static void Clear()
	{
		Factions.Clear();
		Bots.Clear();
		sharedKnowledge = new FactionKnowledge();
		Relation = FactionRelation.Race;
		initialized = false;
		nextBotId = 0;
		Stations.Reset();
		HouseInspector.Reset();
		BaseStorage.Reset();
		ResourceSources.Reset();
		ProgressionPlanner.Reset();
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Factions.Count == 0)
			return;
		tag["relation"] = (int)Relation;
		tag["nextBotId"] = nextBotId;
		tag["factions"] = Factions.Select(faction => faction.Save()).ToList();
		tag["bots"] = Bots.Select(bot => bot.Save()).ToList();
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Relation = (FactionRelation)tag.GetInt("relation");
		nextBotId = tag.GetInt("nextBotId");
		Factions.AddRange(tag.GetList<TagCompound>("factions").Select(Faction.Load));
		Bots.AddRange(tag.GetList<TagCompound>("bots").Select(Bot.Load));
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
		foreach (var bot in Bots.Where(bot => !bot.IsAlive))
		{
			if (--bot.RespawnTicks <= 0)
				Spawn(bot, RespawnFeet(bot));
		}
	}

	static void Initialize()
	{
		if (Factions.Count == 0)
			CreateFactionsFromConfig();
		DesignMissingHouses();
		AssignAvatarSlots();
		foreach (var bot in Bots)
		{
			Spawn(bot, bot.SavedFeetPosition ?? bot.Faction.BaseFeet);
			bot.SavedFeetPosition = null;
		}
		initialized = true;
	}

	static void CreateFactionsFromConfig()
	{
		var config = ModContent.GetInstance<AutoTerraConfig>();
		Relation = config.Relation;
		for (int index = 0; index < config.FactionCount; index++)
		{
			var faction = new Faction { Id = index, BaseTile = FindBaseTile(index, config.FactionCount) };
			Factions.Add(faction);
			for (int member = 0; member < config.BotsPerFaction; member++)
				Bots.Add(Bot.Create(nextBotId++, faction, Main.rand, Bots.Select(bot => bot.Name).ToHashSet()));
		}
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
		float offset = (index - (count - 1) / 2f) * BaseSpacingTiles;
		int preferredX = Utils.Clamp(Main.spawnTileX + (int)offset, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		int bestX = preferredX;
		float bestScore = float.MaxValue;
		for (int x = preferredX - BaseSearchRadius; x <= preferredX + BaseSearchRadius; x += 2)
		{
			if (x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
				continue;
			float score = SiteRoughness(x) + System.Math.Abs(x - preferredX) * 0.05f;
			if (score < bestScore)
			{
				bestScore = score;
				bestX = x;
			}
		}
		return new Point(bestX, TileQuery.SurfaceY(bestX) - 1);
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
		{
			var avatar = Bots[i].Avatar;
			avatar.whoAmI = FirstAvatarSlot + i;
			avatar.active = false;
			Main.player[avatar.whoAmI] = avatar;
		}
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
