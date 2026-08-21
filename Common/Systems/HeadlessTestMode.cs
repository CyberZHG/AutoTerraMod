using Terraria.DataStructures;
using System;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class HeadlessTestMode : ModSystem
{
	static readonly bool headlessRequested = Environment.GetEnvironmentVariable("AUTOTERRA_HEADLESS") == "1";
	static readonly string requestedScenario = Environment.GetEnvironmentVariable("AUTOTERRA_SCENARIO");

	public static bool Enabled => Main.dedServ && headlessRequested;
	public static string Scenario => Enabled ? requestedScenario : null;

	public static bool WeakBosses => Enabled && Environment.GetEnvironmentVariable("AUTOTERRA_WEAK_BOSS") == "1";
	public static Config.FactionRelation? RelationOverride =>
		Enabled && Enum.TryParse<Config.FactionRelation>(Environment.GetEnvironmentVariable("AUTOTERRA_RELATION"), out var relation) ? relation : null;

	public static int? BotsPerFactionOverride => Enabled && int.TryParse(Environment.GetEnvironmentVariable("AUTOTERRA_BOTS"), out int bots) ? bots : null;

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

	static readonly int[][] HardmodeAccessories =
	{
		new int[] { ItemID.FrozenTurtleShell, ItemID.CrossNecklace, ItemID.CobaltShield, ItemID.BandofRegeneration },
		new int[] { ItemID.FrozenShield, ItemID.CharmofMyths, ItemID.RangerEmblem, ItemID.WormScarf, ItemID.AngelWings },
		new int[] { ItemID.AnkhShield, ItemID.CharmofMyths, ItemID.DestroyerEmblem, ItemID.WormScarf, ItemID.FrozenShield, ItemID.AngelWings }
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
		["templedoor"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera), ItemID.LihzahrdPowerCell, false, true, Tier: 2),
		["fishron"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem), ItemID.TruffleWorm, false, true, World.Landmark.Ocean, Tier: 3),
		["empress"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron), ItemID.EmpressButterfly, true, true, World.Landmark.Hallow, Tier: 3),
		["cultist"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss), ItemID.None, false, true, World.Landmark.Dungeon, Tier: 3),
		["moonlord"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss, NPCID.CultistBoss), ItemID.CelestialSigil, false, true, Tier: 3),
		["pillars"] = new(Through(NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.DukeFishron, NPCID.HallowBoss, NPCID.CultistBoss), ItemID.None, false, true, Tier: 3)
	};

	internal static void MarkWorldDefeated(int boss)
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
		UpgradeScenarios.Prepare();
		WarScenario.Prepare();
		TownScenario.Prepare();
		FishingScenario.Prepare();
		EscapeScenario.Prepare();
		PylonHopScenario.Prepare();
		AmmoScenario.Prepare();
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
		if (Scenario == "templedoor")
			PlaceTeamsOutsideTempleDoor();
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
			{
				Give(summoner, scenario.SummonItem, 1);
				ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: {summoner.Name} holds {Bots.Inventory.Count(summoner.Avatar, scenario.SummonItem)} x {Lang.GetItemNameValue(scenario.SummonItem)}");
			}
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"headless scenario '{Scenario}' prepared");
	}

	static void GiveHardmodeKit(Bots.Bot bot, int tier)
	{
		foreach (int type in HardmodeKits[tier].Concat(HardmodeAccessories[tier]))
			Give(bot, type, 1);
		Give(bot, HardmodeAmmo[tier], 3000);
		Give(bot, HardmodePotions[tier], 20);
		foreach (int buff in new int[] { ItemID.IronskinPotion, ItemID.RegenerationPotion, ItemID.EndurancePotion, ItemID.LifeforcePotion, ItemID.ArcheryPotion, ItemID.InfernoPotion })
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
		var shaft = Bosses.BossRituals.ShaftFor(faction);
		for (int y = shaft.TopY; y < Main.UnderworldLayer; y++)
		{
			for (int column = shaft.X; column <= shaft.Right; column++)
			{
				if (Main.tile[column, y].HasTile && !World.TileQuery.IsPlatform(column, y))
					WorldGen.KillTile(column, y, noItem: true);
				if (column == shaft.X && shaft.NeedsPlatform(y) && !Main.tile[column, y].HasTile)
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

	const int LihzahrdDoorFrameTop = 594;
	const int LihzahrdDoorFrameBottom = 646;
	const int UnlockedDoorFrameOffset = 54;

	static void RelockTempleDoors()
	{
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && tile.TileType == TileID.ClosedDoor && tile.TileFrameY >= LihzahrdDoorFrameTop && tile.TileFrameY <= LihzahrdDoorFrameBottom + UnlockedDoorFrameOffset)
				{
					tile.TileFrameX = (short)(tile.TileFrameX % UnlockedDoorFrameOffset);
					if (tile.TileFrameY > LihzahrdDoorFrameBottom)
						tile.TileFrameY -= UnlockedDoorFrameOffset;
				}
			}
		}
	}

	static void ProbeMazeRoute(Microsoft.Xna.Framework.Point door)
	{
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
			{
				if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.LihzahrdAltar)
					continue;
				foreach (bool rope in new[] { false, true })
				{
					var watch = System.Diagnostics.Stopwatch.StartNew();
					var abilities = new Navigation.NavigationAbilities(0, false, rope, false, rope, Navigation.PathFinder.MaxSafeFall, true);
					var route = Navigation.PathFinder.FindLongRoute(door, new Microsoft.Xna.Framework.Point(x, y), 3, abilities, 60000);
					var end = route.Nodes.Count > 0 ? route.Nodes[^1].Feet : door;
					ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: maze route (rope={rope}) door {door} -> altar {x},{y}: nodes={route.Nodes.Count} reaches={route.ReachesGoal} end={end} in {watch.ElapsedMilliseconds}ms");
				}
				return;
			}
		}
	}

	static void PlaceTeamsOutsideTempleDoor()
	{
		RelockTempleDoors();
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
			{
				if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.ClosedDoor || !WorldGen.IsLockedDoor(x, y))
					continue;
				foreach (int side in Enumerable.Range(3, 40).SelectMany(distance => new[] { -distance, distance }))
				{
					if (World.Grounds.StandingSpotNear(new Microsoft.Xna.Framework.Point(x + side, y)) is not Microsoft.Xna.Framework.Point feet || Main.tile[feet.X, feet.Y].WallType == WallID.LihzahrdBrickUnsafe)
						continue;
					foreach (var bot in BotWorldSystem.Bots)
					{
						bot.SavedFeetPosition = new Microsoft.Xna.Framework.Vector2(feet.X * 16f + 8f, (feet.Y + 1) * 16f);
						Give(bot, ItemID.TempleKey, 1);
					}
					ModContent.GetInstance<AutoTerraMod>().Logger.Info($"scenario: teams placed outside the temple door at {x},{y}");
					ProbeMazeRoute(feet);
					return;
				}
			}
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Warn("scenario: no locked temple door found");
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

	internal static void Give(Bots.Bot bot, int type, int stack)
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
		foreach (var client in Netplay.Clients)
		{
			if (client.IsConnected() || client.IsActive)
			{
				orig();
				break;
			}
		}
		Netplay.HasClients = true;
	}

	static readonly string requestedGift = Environment.GetEnvironmentVariable("AUTOTERRA_GIFT");
	static bool giftDelivered;

	static bool startTimeApplied;

	static void ApplyStartTime()
	{
		if (startTimeApplied)
			return;
		startTimeApplied = true;
		if (double.TryParse(Environment.GetEnvironmentVariable("AUTOTERRA_DAY_TIME"), out double time))
		{
			Main.dayTime = true;
			Main.time = time;
		}
	}

	static void DeliverGift()
	{
		bool armMelee = Environment.GetEnvironmentVariable("AUTOTERRA_ARM_MELEE") == "1";
		if (giftDelivered || requestedGift == null && !armMelee || BotWorldSystem.Bots.Count == 0)
			return;
		giftDelivered = true;
		if (requestedGift != null)
		{
			foreach (string gift in requestedGift.Split(','))
				DeliverGift(gift.Split(':'));
		}
		if (armMelee)
			ArmMeleeBots();
		Terraria.IO.WorldFile.SaveWorld();
	}

	static void ArmMeleeBots()
	{
		var best = BotWorldSystem.Bots.SelectMany(bot => Bots.Inventory.Items(bot.Avatar)).Where(Bots.BotHands.IsRangedWeapon).MaxBy(item => item.damage);
		if (best == null)
		{
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn("arm melee: no bot owns a ranged weapon");
			return;
		}
		foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.Personality.Style == Bots.CombatStyle.Melee))
		{
			if (Bots.Inventory.Items(bot.Avatar).Any(item => Bots.BotHands.IsRangedWeapon(item) && item.damage >= best.damage))
				continue;
			var copy = best.Clone();
			copy.stack = 1;
			var leftover = Bots.Inventory.Add(bot.Avatar, copy);
			if (!leftover.IsAir)
				Item.NewItem(new EntitySource_WorldEvent(), bot.Center, leftover);
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"arm melee: gave {bot.Name} ({bot.Faction.Name}, {bot.Personality.Style}) {best.Name} dmg {best.damage}{(leftover.IsAir ? "" : " - inventory full")}");
		}
	}

	static void DeliverGift(string[] parts)
	{
		var bot = BotWorldSystem.Bots.FirstOrDefault(candidate => candidate.Name == parts[0]);
		if (bot == null || !ItemID.Search.TryGetId(parts[1], out int type))
		{
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"gift: cannot resolve {string.Join(":", parts)}");
			return;
		}
		int amount = parts.Length > 2 && int.TryParse(parts[2], out int parsed) ? parsed : 1;
		Give(bot, type, Bots.Inventory.Count(bot.Avatar, type) + amount);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"gift: gave {bot.Name} {amount} x {Lang.GetItemNameValue(type)} (now {Bots.Inventory.Count(bot.Avatar, type)})");
	}

	static readonly int saveAfterSeconds = int.TryParse(Environment.GetEnvironmentVariable("AUTOTERRA_SAVE_AFTER"), out int seconds) ? seconds : -1;
	static uint runStartedAt;
	static bool savedOnce;

	static void SaveWhenDue()
	{
		if (saveAfterSeconds < 0 || savedOnce)
			return;
		if (runStartedAt == 0)
			runStartedAt = Math.Max(1u, Main.GameUpdateCount);
		if (Main.GameUpdateCount - runStartedAt < saveAfterSeconds * 60u)
			return;
		savedOnce = true;
		Terraria.IO.WorldFile.SaveWorld();
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"headless test saved the world after {saveAfterSeconds}s");
	}

	static readonly string dumpSpec = Environment.GetEnvironmentVariable("AUTOTERRA_DUMP");
	static bool dumped;

	static void DumpWhenAsked()
	{
		if (dumped || string.IsNullOrEmpty(dumpSpec) || Main.GameUpdateCount < 120)
			return;
		dumped = true;
		foreach (var spec in dumpSpec.Split(';'))
		{
			if (spec.StartsWith("capture:"))
			{
				var at = spec["capture:".Length..].Split(',').Select(int.Parse).ToArray();
				ModContent.GetInstance<AutoTerraMod>().Logger.Info(World.TerrainDump.Capture(new Microsoft.Xna.Framework.Point(at[0], at[1]), new Microsoft.Xna.Framework.Vector2(at[2] * 16f, at[3] * 16f)));
				continue;
			}
			var parts = spec.Split(',').Select(int.Parse).ToArray();
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"dump around {parts[0]},{parts[1]}:{World.TerrainDump.Around(new Microsoft.Xna.Framework.Point(parts[0], parts[1]), parts[2], parts[3])}");
		}
	}

	static readonly string importSource = Environment.GetEnvironmentVariable("AUTOTERRA_IMPORT");
	static bool imported;

	static void ImportWhenAsked()
	{
		if (imported || string.IsNullOrEmpty(importSource) || Main.GameUpdateCount < 300)
			return;
		imported = true;
		BotImport.Run(importSource, ModContent.GetInstance<Config.AutoTerraConfig>());
	}

	static bool blueprintsLogged;

	static void LogBlueprints()
	{
		if (blueprintsLogged || Main.GameUpdateCount < 180 || Environment.GetEnvironmentVariable("AUTOTERRA_BLUEPRINTS") != "1")
			return;
		blueprintsLogged = true;
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (faction.Settlement is not { } camp)
				continue;
			foreach (var structure in camp.Structures)
				ModContent.GetInstance<AutoTerraMod>().Logger.Info($"[{faction.Name}] blueprint #{structure.Index} {structure.Purpose} x={structure.Left}..{structure.Right} y={structure.Top}..{structure.Floor} finished={Building.HouseInspector.IsFinished(faction, structure)} next={Building.HouseInspector.NextOperation(faction, structure)?.ToString() ?? "none"}");
		}
	}

	static readonly string traceBot = Environment.GetEnvironmentVariable("AUTOTERRA_TRACE_BOT");

	static void TraceWhenAsked()
	{
		if (string.IsNullOrEmpty(traceBot) || Main.GameUpdateCount % 20 != 0)
			return;
		foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.Name == traceBot && bot.IsAlive))
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"trace {bot.Name}: task={bot.Brain.Current?.NameKey} plan={(bot.Brain.Plan is Planning.BuildBridgeStep b && b.Bridge is World.Highway h ? $"highway {h.StartX}->{h.EndX} y={h.Y} dir={h.Direction} frontier={h.Frontier(h.Direction)} work={h.WorkFrontier()}" : bot.Brain.Plan?.ToString())} {bot.Motor.Describe()}");
	}

	public override void PostUpdateWorld()
	{
		if (Enabled)
		{
			ApplyStartTime();
			DeliverGift();
			SaveWhenDue();
			DumpWhenAsked();
			TraceWhenAsked();
			LogBlueprints();
			ImportWhenAsked();
		}
		if (Enabled && Main.netMode == NetmodeID.Server && Main.GameUpdateCount % 3600 == 0)
		{
			int dollDropperKills = Bosses.BossCredit.DollDroppers.Sum(type => Main.BestiaryTracker.Kills.GetKillCount(ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[type]));
			Mod.Logger.Info($"headless test running: {Main.GameUpdateCount / 60}s of game time; doll dropper kills in the world: {dollDropperKills}");
		}
	}
}
