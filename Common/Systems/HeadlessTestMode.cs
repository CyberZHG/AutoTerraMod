using System;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class HeadlessTestMode : ModSystem
{
	public static bool Enabled => Main.dedServ && Environment.GetEnvironmentVariable("AUTOTERRA_HEADLESS") == "1";
	public static string Scenario => Enabled ? Environment.GetEnvironmentVariable("AUTOTERRA_SCENARIO") : null;

	static readonly int[] StrongScenarioKit =
	{
		ItemID.FieryGreatsword, ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves
	};

	static readonly int[] BossScenarioKit =
	{
		ItemID.PlatinumBroadsword, ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves
	};

	sealed record BossScenario(int[] AlreadyDefeated, int SummonItem, bool StartAtNight, bool RevealWorld, World.Landmark? StartIn = null, int StartNearTile = -1, bool StrongKit = false);

	static readonly System.Collections.Generic.Dictionary<string, BossScenario> BossScenarios = new()
	{
		["boss"] = new(Array.Empty<int>(), ItemID.SlimeCrown, false, false),
		["eoc"] = new(new int[] { NPCID.KingSlime }, ItemID.SuspiciousLookingEye, true, false),
		["evil"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true),
		["evilarena"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true, World.Landmark.Corruption),
		["queenbee"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu }, ItemID.None, false, true, World.Landmark.Jungle, TileID.Larva, true),
		["deerclops"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead }, ItemID.DeerThing, false, true, World.Landmark.Snow),
		["skeletron"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee }, ItemID.None, true, true, World.Landmark.Dungeon)
	};

	public static bool BossFirst => Scenario != null && BossScenarios.ContainsKey(Scenario);

	public static void PrepareScenario()
	{
		if (Scenario == null || !BossScenarios.TryGetValue(Scenario, out var scenario))
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			faction.DefeatedBosses.Clear();
			foreach (int boss in scenario.AlreadyDefeated)
				faction.DefeatedBosses.Add(boss);
		}
		if (scenario.StartAtNight)
		{
			Main.dayTime = false;
			Main.time = 0;
		}
		if (scenario.RevealWorld)
		{
			foreach (var bot in BotWorldSystem.Bots)
			{
				Give(bot, ItemID.IronHammer, 1);
				Give(bot, ItemID.NightmarePickaxe, 1);
			}
			LogEvilSpan();
			RevealShrines();
		}
		if (scenario.StartIn is World.Landmark start)
			PlaceTeamsIn(start);
		if (scenario.StartNearTile >= 0)
			PlaceTeamsNear(scenario.StartNearTile);
		foreach (var bot in BotWorldSystem.Bots)
		{
			Bots.Junk.TrashExcess(bot);
			foreach (int type in scenario.StrongKit ? StrongScenarioKit : BossScenarioKit)
				Give(bot, type, 1);
			Give(bot, scenario.StrongKit ? ItemID.HealingPotion : ItemID.LesserHealingPotion, 10);
			Give(bot, ItemID.Rope, 60);
			int targetLife = scenario.StrongKit ? 300 : 200;
			if (bot.Avatar.statLifeMax < targetLife)
				Give(bot, ItemID.LifeCrystal, (targetLife - bot.Avatar.statLifeMax) / 20);
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			var summoner = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id).MaxBy(bot => bot.Personality.Combat);
			if (summoner != null && scenario.SummonItem != ItemID.None)
				Give(summoner, scenario.SummonItem, 1);
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"headless scenario '{Scenario}' prepared");
	}

	static void PlaceTeamsIn(World.Landmark landmark)
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (World.Grounds.SurfaceSpotNear(BotWorldSystem.KnowledgeOf(faction), landmark, faction.BaseTile) is not Microsoft.Xna.Framework.Vector2 spot)
				continue;
			int offset = 0;
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
			{
				int x = (int)(spot.X / 16f) + offset++ * 2;
				bot.SavedFeetPosition = new Microsoft.Xna.Framework.Vector2(x * 16f + 8f, World.TileQuery.SurfaceY(x) * 16f);
			}
		}
	}

	static void PlaceTeamsNear(int tileType)
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var target = BotWorldSystem.KnowledgeOf(faction).NearestKnown(faction.BaseTile, new System.Collections.Generic.HashSet<int> { tileType }, new System.Collections.Generic.HashSet<Microsoft.Xna.Framework.Point>(), 5000);
			if (target is not Microsoft.Xna.Framework.Point shrine || StandingSpotNear(shrine) is not Microsoft.Xna.Framework.Point feet)
				continue;
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				bot.SavedFeetPosition = new Microsoft.Xna.Framework.Vector2(feet.X * 16f + 16f, (feet.Y + 1) * 16f);
		}
	}

	static Microsoft.Xna.Framework.Point? StandingSpotNear(Microsoft.Xna.Framework.Point center)
	{
		for (int radius = 2; radius < 20; radius++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					int x = center.X + dx, y = center.Y + dy;
					bool clear = Enumerable.Range(0, 3).All(up => !World.TileQuery.IsSolid(x, y - up) && !World.TileQuery.IsSolid(x + 1, y - up) && Main.tile[x, y - up].LiquidAmount == 0);
					if (clear && World.TileQuery.CanStandOn(x, y + 1))
						return new Microsoft.Xna.Framework.Point(x, y);
				}
			}
		}
		return null;
	}

	static void RevealShrines()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var knowledge = BotWorldSystem.KnowledgeOf(faction);
			knowledge.RememberLandmark(World.Landmark.Dungeon, new Microsoft.Xna.Framework.Point(Main.dungeonX, Main.dungeonY));
			foreach (var (x, y) in SurfaceColumns(TileID.CorruptGrass, TileID.CrimsonGrass, TileID.Ebonstone, TileID.Crimstone))
				knowledge.RememberLandmark(World.Landmarks.WorldEvil, new Microsoft.Xna.Framework.Point(x, y));
			foreach (var (x, y) in SurfaceColumns(TileID.JungleGrass))
				knowledge.RememberLandmark(World.Landmark.Jungle, new Microsoft.Xna.Framework.Point(x, y));
			foreach (var (x, y) in SurfaceColumns(TileID.SnowBlock, TileID.IceBlock))
				knowledge.RememberLandmark(World.Landmark.Snow, new Microsoft.Xna.Framework.Point(x, y));
			for (int x = 0; x < Main.maxTilesX; x++)
			{
				for (int y = 0; y < Main.maxTilesY; y++)
				{
					if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType is TileID.ShadowOrbs or TileID.Larva)
						knowledge.Remember(new Microsoft.Xna.Framework.Point(x, y), Main.tile[x, y].TileType);
				}
			}
		}
	}

	static System.Collections.Generic.IEnumerable<(int x, int y)> SurfaceColumns(params int[] tileTypes)
	{
		for (int x = 0; x < Main.maxTilesX; x++)
		{
			int y = World.TileQuery.SurfaceY(x);
			var tile = Main.tile[x, y];
			if (tile.HasTile && tileTypes.Contains(tile.TileType))
				yield return (x, y);
		}
	}

	static void LogEvilSpan()
	{
		int minX = int.MaxValue, maxX = -1, orbs = 0;
		for (int x = 0; x < Main.maxTilesX; x++)
		{
			for (int y = 0; y < Main.maxTilesY; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile)
					continue;
				if (tile.TileType is TileID.Ebonstone or TileID.Crimstone or TileID.CorruptGrass or TileID.CrimsonGrass)
				{
					minX = Math.Min(minX, x);
					maxX = Math.Max(maxX, x);
				}
				if (tile.TileType == TileID.ShadowOrbs)
					orbs++;
			}
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: world {Main.maxTilesX}x{Main.maxTilesY} crimson={WorldGen.crimson} evil x {minX}..{maxX}, orb tiles {orbs}, dungeon x {Main.dungeonX}, spawn x {Main.spawnTileX}");
	}

	static void Give(Bots.Bot bot, int type, int stack)
	{
		int missing = stack - Bots.Inventory.Count(bot.Avatar, type) - bot.Avatar.armor.Count(item => item.type == type);
		if (missing <= 0)
			return;
		var item = new Item();
		item.SetDefaults(type);
		item.stack = missing;
		if (!Bots.Inventory.HasRoomFor(bot.Avatar, item))
			bot.Avatar.inventory.Take(Bots.Inventory.MainSlotCount).Where(held => held.createTile >= 0).MaxBy(held => held.stack)?.TurnToAir();
		if (!Bots.Inventory.Add(bot.Avatar, item).IsAir)
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"scenario: no room for {item.Name} in {bot.Name}'s inventory");
	}

	public override void Load()
	{
		if (Enabled)
			On_Netplay.UpdateConnectedClients += KeepWorldRunningWithoutClients;
	}

	static void KeepWorldRunningWithoutClients(On_Netplay.orig_UpdateConnectedClients orig)
	{
		if (Netplay.Clients.Any(client => client.IsConnected() || client.IsActive))
			orig();
		Netplay.HasClients = true;
	}

	public override void PostUpdateWorld()
	{
		if (Enabled && Main.netMode == NetmodeID.Server && Main.GameUpdateCount % 3600 == 0)
			Mod.Logger.Info($"headless test running: {Main.GameUpdateCount / 60}s of game time");
	}
}
