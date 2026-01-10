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

	static readonly int[] BossScenarioKit =
	{
		ItemID.PlatinumBroadsword, ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves
	};

	sealed record BossScenario(int[] AlreadyDefeated, int SummonItem, bool StartAtNight, bool RevealWorld, World.Landmark? StartIn = null);

	static readonly System.Collections.Generic.Dictionary<string, BossScenario> BossScenarios = new()
	{
		["boss"] = new(Array.Empty<int>(), ItemID.SlimeCrown, false, false),
		["eoc"] = new(new int[] { NPCID.KingSlime }, ItemID.SuspiciousLookingEye, true, false),
		["evil"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true),
		["evilarena"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true, World.Landmark.Corruption),
		["skeletron"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee }, ItemID.None, true, true, World.Landmark.Dungeon)
	};

	public static void PrepareScenario()
	{
		if (Scenario == null || !BossScenarios.TryGetValue(Scenario, out var scenario))
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
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
		foreach (var bot in BotWorldSystem.Bots)
		{
			Bots.Junk.TrashExcess(bot);
			foreach (int type in BossScenarioKit)
				Give(bot, type, 1);
			Give(bot, ItemID.LesserHealingPotion, 10);
			if (bot.Avatar.statLifeMax < 200)
				Give(bot, ItemID.LifeCrystal, (200 - bot.Avatar.statLifeMax) / 20);
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

	static void RevealShrines()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var knowledge = BotWorldSystem.KnowledgeOf(faction);
			knowledge.RememberLandmark(World.Landmark.Dungeon, new Microsoft.Xna.Framework.Point(Main.dungeonX, Main.dungeonY));
			foreach (var (x, y) in EvilSurfaceEdges())
				knowledge.RememberLandmark(World.Landmarks.WorldEvil, new Microsoft.Xna.Framework.Point(x, y));
			for (int x = 0; x < Main.maxTilesX; x++)
			{
				for (int y = 0; y < Main.maxTilesY; y++)
				{
					if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.ShadowOrbs)
						knowledge.Remember(new Microsoft.Xna.Framework.Point(x, y), TileID.ShadowOrbs);
				}
			}
		}
	}

	static System.Collections.Generic.IEnumerable<(int x, int y)> EvilSurfaceEdges()
	{
		for (int x = 0; x < Main.maxTilesX; x++)
		{
			int y = World.TileQuery.SurfaceY(x);
			var tile = Main.tile[x, y];
			if (tile.HasTile && tile.TileType is TileID.CorruptGrass or TileID.CrimsonGrass or TileID.Ebonstone or TileID.Crimstone)
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
