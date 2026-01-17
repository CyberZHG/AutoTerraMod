using Terraria.DataStructures;
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

	static readonly int[][] HardmodeKits =
	{
		new int[] { ItemID.Megashark, ItemID.AdamantiteMask, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings },
		new int[] { ItemID.ChainGun, ItemID.ChlorophyteHelmet, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves },
		new int[] { ItemID.SDMG, ItemID.VortexHelmet, ItemID.VortexBreastplate, ItemID.VortexLeggings }
	};

	static readonly int[] HardmodeAmmo = { ItemID.CrystalBullet, ItemID.ChlorophyteBullet, ItemID.ChlorophyteBullet };
	static readonly int[] HardmodePotions = { ItemID.GreaterHealingPotion, ItemID.GreaterHealingPotion, ItemID.SuperHealingPotion };
	static readonly int[] HardmodeLife = { 500, 500, 500 };

	static readonly int[] PreHardmode = { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead, NPCID.Deerclops, NPCID.WallofFlesh };

	static int[] Through(params int[] more) => PreHardmode.Concat(more).ToArray();

	sealed record BossScenario(int[] AlreadyDefeated, int SummonItem, bool StartAtNight, bool RevealWorld, World.Landmark? StartIn = null, int StartNearTile = -1, bool StrongKit = false, int Tier = 0);

	static readonly System.Collections.Generic.Dictionary<string, BossScenario> BossScenarios = new()
	{
		["boss"] = new(Array.Empty<int>(), ItemID.SlimeCrown, false, false),
		["eoc"] = new(new int[] { NPCID.KingSlime }, ItemID.SuspiciousLookingEye, true, false),
		["evil"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true),
		["evilarena"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu }, ItemID.WormFood, false, true, World.Landmark.Corruption),
		["queenbee"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu }, ItemID.None, false, true, World.Landmark.Jungle, TileID.Larva, true),
		["deerclops"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead }, ItemID.DeerThing, false, true, World.Landmark.Snow),
		["wof"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead, NPCID.Deerclops }, ItemID.GuideVoodooDoll, false, true, World.Landmark.Underworld, -1, true),
		["wofbase"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead, NPCID.Deerclops }, ItemID.GuideVoodooDoll, false, true, null, -1, true),
		["skeletron"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee }, ItemID.None, true, true, World.Landmark.Dungeon),
		["queenslime"] = new(Through(), ItemID.QueenSlimeCrystal, false, true, World.Landmark.Hallow, Tier: 1),
		["destroyer"] = new(Through(NPCID.QueenSlimeBoss), ItemID.MechanicalWorm, true, true, Tier: 1),
		["twins"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer), ItemID.MechanicalEye, true, true, Tier: 1),
		["prime"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer), ItemID.MechanicalSkull, true, true, Tier: 1),
		["plantera"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime), ItemID.None, false, true, null, TileID.PlanteraBulb, Tier: 2),
		["golem"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera), ItemID.LihzahrdPowerCell, false, true, null, TileID.LihzahrdAltar, Tier: 2),
		["fishron"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem), ItemID.TruffleWorm, false, true, World.Landmark.Ocean, Tier: 2),
		["empress"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron), ItemID.EmpressButterfly, true, true, World.Landmark.Hallow, Tier: 3),
		["cultist"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss), ItemID.None, false, true, World.Landmark.Dungeon, Tier: 3),
		["moonlord"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss, NPCID.CultistBoss), ItemID.CelestialSigil, false, true, Tier: 3),
		["pillars"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss, NPCID.CultistBoss), ItemID.None, false, true, Tier: 3)
	};

	static void MarkWorldDefeated(int boss)
	{
		switch (boss)
		{
			case NPCID.KingSlime: NPC.downedSlimeKing = true; break;
			case NPCID.EyeofCthulhu: NPC.downedBoss1 = true; break;
			case NPCID.EaterofWorldsHead or NPCID.BrainofCthulhu: NPC.downedBoss2 = true; break;
			case NPCID.QueenBee: NPC.downedQueenBee = true; break;
			case NPCID.SkeletronHead: NPC.downedBoss3 = true; break;
			case NPCID.Deerclops: NPC.downedDeerclops = true; break;
			case NPCID.WallofFlesh:
				Main.hardMode = true;
				if (NearestExposed(new Microsoft.Xna.Framework.Point(Main.spawnTileX, Main.spawnTileY), TileID.HallowedGrass, TileID.Pearlstone, TileID.Pearlsand) == null)
					WorldGen.smCallBack(1);
				break;
			case NPCID.QueenSlimeBoss: NPC.downedQueenSlime = true; break;
			case NPCID.TheDestroyer: NPC.downedMechBoss1 = true; NPC.downedMechBossAny = true; break;
			case NPCID.Retinazer: NPC.downedMechBoss2 = true; NPC.downedMechBossAny = true; break;
			case NPCID.SkeletronPrime: NPC.downedMechBoss3 = true; NPC.downedMechBossAny = true; break;
			case NPCID.Plantera: NPC.downedPlantBoss = true; break;
			case NPCID.Golem: NPC.downedGolemBoss = true; break;
			case NPCID.DukeFishron: NPC.downedFishron = true; break;
			case NPCID.HallowBoss: NPC.downedEmpressOfLight = true; break;
			case NPCID.CultistBoss: NPC.downedAncientCultist = true; break;
		}
	}

	static void PrepareHardmodeWorld()
	{
		if (Scenario == "plantera")
			PlantBulb();
		if (Scenario == "cultist")
		{
			Terraria.GameContent.Events.CultistRitual.delay = 0;
			Terraria.GameContent.Events.CultistRitual.recheck = 0;
		}
		if (Scenario == "pillars")
			WorldGen.TriggerLunarApocalypse();
	}

	static void PlantBulb()
	{
		int dungeonSide = Main.dungeonX < Main.maxTilesX / 2 ? -1 : 1;
		for (int attempt = 0; attempt < 200000; attempt++)
		{
			int x = Main.rand.Next(200, Main.maxTilesX - 200);
			int y = Main.rand.Next((int)Main.rockLayer, Main.UnderworldLayer - 50);
			bool floor = Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.JungleGrass && Main.tile[x + 1, y].HasTile && Main.tile[x + 1, y].TileType == TileID.JungleGrass;
			bool room = !Main.tile[x, y - 1].HasTile && !Main.tile[x + 1, y - 1].HasTile && !Main.tile[x, y - 2].HasTile && !Main.tile[x + 1, y - 2].HasTile;
			if (!floor || !room)
				continue;
			WorldGen.PlaceJunglePlant(x, y - 1, TileID.PlanteraBulb, 0, 0);
			if (Main.tile[x, y - 1].TileType == TileID.PlanteraBulb && Main.tile[x, y - 1].HasTile)
			{
				ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: planted a Plantera bulb at {x},{y - 1}");
				return;
			}
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Warn("scenario: could not plant a Plantera bulb");
	}

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
		if (scenario.Tier > 0)
		{
			foreach (int boss in scenario.AlreadyDefeated)
				MarkWorldDefeated(boss);
			PrepareHardmodeWorld();
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
			if (scenario.SummonItem == ItemID.GuideVoodooDoll)
			{
				Give(bot, ItemID.MoltenFury, 1);
				Give(bot, ItemID.HellfireArrow, 999);
				Give(bot, ItemID.WoodPlatform, 300);
				Give(bot, ItemID.ObsidianSkinPotion, 10);
			}
			Give(bot, scenario.StrongKit ? ItemID.HealingPotion : ItemID.LesserHealingPotion, 10);
			Give(bot, ItemID.Rope, 60);
			int targetLife = scenario.SummonItem == ItemID.GuideVoodooDoll ? 400 : scenario.StrongKit ? 300 : 200;
			if (scenario.Tier > 0)
				GiveHardmodeKit(bot, scenario.Tier - 1);
			else if (bot.Avatar.statLifeMax < targetLife)
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

	static void GiveHardmodeKit(Bots.Bot bot, int tier)
	{
		foreach (int type in HardmodeKits[tier])
			Give(bot, type, 1);
		Give(bot, HardmodeAmmo[tier], 3000);
		Give(bot, HardmodePotions[tier], 20);
		foreach (int buff in new int[] { ItemID.IronskinPotion, ItemID.RegenerationPotion, ItemID.EndurancePotion, ItemID.LifeforcePotion, ItemID.ArcheryPotion })
			Give(bot, buff, 3);
		var avatar = bot.Avatar;
		if (avatar.ConsumedLifeCrystals < Player.LifeCrystalMax)
			Give(bot, ItemID.LifeCrystal, Player.LifeCrystalMax - avatar.ConsumedLifeCrystals);
		int fruit = (HardmodeLife[tier] - 400) / 5 - avatar.ConsumedLifeFruit;
		if (fruit > 0)
			Give(bot, ItemID.LifeFruit, fruit);
		if (Scenario == "fishron")
			Give(bot, ItemID.ReinforcedFishingPole, 1);
	}

	static void PlaceTeamsIn(World.Landmark landmark)
	{
		if (landmark == World.Landmark.Underworld)
		{
			PlaceTeamsInUnderworld();
			return;
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (World.Grounds.ArenaSpot(BotWorldSystem.KnowledgeOf(faction), landmark, faction.BaseTile) is not Microsoft.Xna.Framework.Vector2 spot)
				continue;
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				bot.SavedFeetPosition = spot;
		}
	}

	static void PlaceTeamsInUnderworld()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (UnderworldGround(faction.BaseTile.X) is not Microsoft.Xna.Framework.Point feet)
				continue;
			BotWorldSystem.KnowledgeOf(faction).RememberLandmark(World.Landmark.Underworld, feet);
			faction.Hellbridge = null;
			CarveHellevator(faction);
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				bot.SavedFeetPosition = new Microsoft.Xna.Framework.Vector2(feet.X * 16f + 16f, (feet.Y + 1) * 16f);
		}
		if (!NPC.AnyNPCs(NPCID.Guide))
			NPC.NewNPC(new EntitySource_WorldEvent(), Main.spawnTileX * 16, Main.spawnTileY * 16, NPCID.Guide);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: guide alive={NPC.AnyNPCs(NPCID.Guide)}");
	}

	static void CarveHellevator(Factions.Faction faction)
	{
		int x = faction.BaseTile.X + 24;
		var shaft = faction.Hellevator ??= new World.Hellevator(x, World.TileQuery.SurfaceY(x) - 1);
		for (int y = shaft.TopY; y < Main.UnderworldLayer; y++)
		{
			for (int column = shaft.X; column <= shaft.X + 1; column++)
			{
				if (Main.tile[column, y].HasTile && !World.TileQuery.IsPlatform(column, y))
					WorldGen.KillTile(column, y, noItem: true);
				if (shaft.NeedsPlatform(y) && !Main.tile[column, y].HasTile)
				{
					var tile = Main.tile[column, y];
					tile.HasTile = true;
					tile.TileType = TileID.Platforms;
					WorldGen.SquareTileFrame(column, y);
				}
			}
		}
	}

	static Microsoft.Xna.Framework.Point? UnderworldGround(int centerX)
	{
		for (int dx = 0; dx < 200; dx++)
		{
			foreach (int x in new[] { centerX + dx, centerX - dx })
			{
				for (int y = Main.UnderworldLayer + 5; y < Main.maxTilesY - 20; y++)
				{
					bool clear = Enumerable.Range(0, 3).All(up => !World.TileQuery.IsSolid(x, y - up) && !World.TileQuery.IsSolid(x + 1, y - up) && Main.tile[x, y - up].LiquidAmount == 0 && Main.tile[x + 1, y - up].LiquidAmount == 0);
					if (clear && World.TileQuery.IsSolid(x, y + 1) && World.TileQuery.IsSolid(x + 1, y + 1))
						return new Microsoft.Xna.Framework.Point(x, y);
				}
			}
		}
		return null;
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
			if (NearestExposed(faction.BaseTile, TileID.HallowedGrass, TileID.Pearlstone, TileID.Pearlsand) is Microsoft.Xna.Framework.Point hallow)
				knowledge.RememberLandmark(World.Landmark.Hallow, hallow);
			foreach (var (x, y) in SurfaceColumns(TileID.HallowedGrass, TileID.Pearlstone, TileID.Pearlsand))
				knowledge.RememberLandmark(World.Landmark.Hallow, new Microsoft.Xna.Framework.Point(x, y));
			foreach (var (x, y) in SurfaceColumns(TileID.Sand).Where(column => WorldGen.oceanDepths(column.x, column.y) && (column.x < Main.maxTilesX / 2) == (faction.BaseTile.X < Main.maxTilesX / 2)))
				knowledge.RememberLandmark(World.Landmark.Ocean, new Microsoft.Xna.Framework.Point(x, y));
			for (int x = 0; x < Main.maxTilesX; x++)
			{
				for (int y = 0; y < Main.maxTilesY; y++)
				{
					if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType is TileID.ShadowOrbs or TileID.Larva or TileID.PlanteraBulb or TileID.LihzahrdAltar)
						knowledge.Remember(new Microsoft.Xna.Framework.Point(x, y), Main.tile[x, y].TileType);
					if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.LihzahrdAltar)
						knowledge.RememberLandmark(World.Landmark.Temple, new Microsoft.Xna.Framework.Point(x, y));
				}
			}
		}
	}

	static Microsoft.Xna.Framework.Point? NearestExposed(Microsoft.Xna.Framework.Point from, params int[] tileTypes)
	{
		Microsoft.Xna.Framework.Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = 10; y < Main.UnderworldLayer; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile || !tileTypes.Contains(tile.TileType))
					continue;
				long distance = (long)(x - from.X) * (x - from.X) + (long)(y - from.Y) * (y - from.Y);
				if (distance < bestDistance && World.TileQuery.IsExposed(x, y) && World.Grounds.StandingSpotNear(new Microsoft.Xna.Framework.Point(x, y)) != null)
				{
					bestDistance = distance;
					best = new Microsoft.Xna.Framework.Point(x, y);
				}
			}
		}
		return best;
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
			bot.Avatar.inventory.Take(Bots.Inventory.MainSlotCount).Where(held => held.createTile >= 0 && !Bots.Junk.IsPlatform(held) && held.type != type).MaxBy(held => held.stack)?.TurnToAir();
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
