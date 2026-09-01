using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public static class BossRituals
{
	const int ShrineSearchRadius = 3000;
	const int ShrineSearchesBeforeGivingUp = 10;
	const uint ShrineRetryTicks = 20 * 60 * 60;

	public static readonly int[] Hammers =
	{
		ItemID.WoodenHammer, ItemID.CopperHammer, ItemID.TinHammer, ItemID.IronHammer, ItemID.LeadHammer,
		ItemID.SilverHammer, ItemID.TungstenHammer, ItemID.GoldHammer, ItemID.PlatinumHammer
	};

	static readonly HashSet<int> OrbTiles = new() { TileID.ShadowOrbs };
	static readonly HashSet<int> LarvaTiles = new() { TileID.Larva };
	static readonly HashSet<int> BulbTiles = new() { TileID.PlanteraBulb };
	static readonly HashSet<int> AltarTiles = new() { TileID.LihzahrdAltar };

	static HashSet<int> ShrineTilesOf(Landmark landmark) => landmark switch
	{
		Landmark.Jungle => LarvaTiles,
		Landmark.Temple => AltarTiles,
		_ => OrbTiles
	};

	public static bool KnowsShrine(Bot bot, Landmark landmark) => KnownShrine(bot, landmark) != null;

	public static bool KnowsTile(Bot bot, int tileType) => KnownTile(bot, new HashSet<int> { tileType }) != null;

	static Point? KnownShrine(Bot bot, Landmark landmark)
	{
		if (!Main.drunkWorld || landmark is not (Landmark.Corruption or Landmark.Crimson))
			return KnownTile(bot, ShrineTilesOf(landmark));
		var skipped = new HashSet<Point>();
		for (int tries = 0; tries < DrunkShrineTries; tries++)
		{
			if (BotWorldSystem.KnowledgeOf(bot.Faction).NearestKnown(bot.Faction.BaseTile, OrbTiles, skipped, ShrineSearchRadius) is not Point orb)
				return null;
			if (Main.tile[orb.X, orb.Y].TileFrameX >= CrimsonHeartFrame == (landmark == Landmark.Crimson))
				return orb;
			skipped.Add(orb);
		}
		return null;
	}

	const int DrunkShrineTries = 12;
	const int CrimsonHeartFrame = 36;

	static Point? KnownTile(Bot bot, HashSet<int> tiles) =>
		BotWorldSystem.KnowledgeOf(bot.Faction).NearestKnown(bot.Faction.BaseTile, tiles, new HashSet<Point>(), ShrineSearchRadius);

	public static IEnumerable<BossTrigger> InPreferredOrder(Bot bot, BossChallenge challenge) =>
		challenge.Triggers.OrderBy(trigger => trigger switch
		{
			SummonItemTrigger item when Inventory.Count(bot.Avatar, item.Item) > 0 => 0,
			PillarsTrigger when NearestPillar(bot.Center) != null => 0,
			SmashOrbsTrigger when !CanMineEvilStone(bot) => 2,
			_ => 1
		});

	public static bool CanMineEvilStone(Bot bot) =>
		(bot.Hands.BestPickaxe?.pick ?? 0) >= Navigation.PickRequirement.MinimumPick(Landmarks.EvilIsCrimson ? TileID.Crimstone : TileID.Ebonstone, 0, 0);

	public static PlanStep Plan(Bot bot, BossChallenge challenge, BossTrigger trigger) =>
		bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until && bot.Faction.RallyBossType != challenge.BossType
			? PlanStep.Blocked
			: PlanFor(bot, challenge, trigger);

	static PlanStep PlanFor(Bot bot, BossChallenge challenge, BossTrigger trigger) => trigger switch
	{
		SummonItemTrigger item => PlanSummonItem(bot, challenge, item),
		SmashOrbsTrigger orbs => PlanSmashOrbs(bot, challenge, orbs),
		SmashLarvaTrigger larva => PlanSmashLarva(bot, challenge, larva),
		CurseOldManTrigger curse => PlanCurse(bot, challenge, curse),
		VoodooDollTrigger voodoo => PlanVoodoo(bot, challenge, voodoo),
		ClothierDollTrigger clothier => PlanClothier(bot, challenge, clothier),
		AltarTrigger altar => PlanAltar(bot, challenge, altar),
		BulbTrigger bulb => PlanBulb(bot, challenge, bulb),
		BaitTrigger bait => PlanBait(bot, challenge, bait),
		LacewingTrigger lacewing => PlanLacewing(bot, challenge, lacewing),
		CultistRitualTrigger ritual => PlanCultists(bot, challenge, ritual),
		PillarsTrigger pillars => PlanPillars(bot, challenge, pillars),
		_ => PlanStep.Blocked
	};

	static PlanStep PlanAltar(Bot bot, BossChallenge challenge, AltarTrigger trigger)
	{
		if (trigger.Place == Landmark.Temple && Inventory.Count(bot.Avatar, ItemID.TempleKey) == 0 && Building.BaseStorage.Count(bot.Faction, ItemID.TempleKey) > 0)
			return new WithdrawStep(ItemID.TempleKey, 1);
		if (trigger.Place == Landmark.Temple && KnownTile(bot, AltarTiles) == null && !LearnAltarOfOpenTemple(bot))
		{
			bool locked = KnownLandmark(bot, Landmark.Temple) is Point temple && TempleLocked(temple);
			if (locked && Inventory.Count(bot.Avatar, ItemID.TempleKey) == 0)
				return ProgressionPlanner.Obtain(bot, ItemID.TempleKey, 1) is var key and not IdleStep ? key : PlanStep.Blocked;
			return new ScoutStep(trigger.Place, true);
		}
		if (Inventory.Count(bot.Avatar, trigger.Item) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Item, 1);
		if (KnownTile(bot, AltarTiles) is not Point altar)
			return new ScoutStep(trigger.Place, true);
		return new SummonBossStep(challenge, trigger, altar.ToWorldCoordinates(), altar);
	}

	static (uint at, bool locked) templeLock;
	const uint TempleLockRecheckTicks = 600;

	public static bool TempleLocked(Point temple)
	{
		if (templeLock.at == 0 || Main.GameUpdateCount - templeLock.at >= TempleLockRecheckTicks)
			templeLock = (Main.GameUpdateCount | 1, Bots.Tasks.ScoutTask.LockedTempleDoor(temple) != null);
		return templeLock.locked;
	}


	static bool LearnAltarOfOpenTemple(Bot bot)
	{
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		if (knowledge.LandmarkOf(Landmark.Temple) is not Point temple || TempleLocked(temple) || FindAltar(knowledge) is not Point altar)
			return false;
		knowledge.Remember(altar, TileID.LihzahrdAltar);
		return true;
	}

	static Point? templeAltar;
	static uint altarScannedAt;
	const uint AltarRescanTicks = 1800;
	const int AltarSpanMargin = 200;

	static Point? FindAltar(Factions.FactionKnowledge knowledge)
	{
		if (templeAltar is Point known && Main.tile[known.X, known.Y].HasTile && Main.tile[known.X, known.Y].TileType == TileID.LihzahrdAltar)
			return known;
		templeAltar = null;
		if (altarScannedAt != 0 && Main.GameUpdateCount - altarScannedAt < AltarRescanTicks)
			return null;
		altarScannedAt = Main.GameUpdateCount | 1;
		var (minX, maxX) = knowledge.SpanOf(Landmark.Jungle) is var (left, right) ? (left - AltarSpanMargin, right + AltarSpanMargin) : (0, Main.maxTilesX);
		for (int x = System.Math.Max(minX, 10); x <= System.Math.Min(maxX, Main.maxTilesX - 10); x++)
		{
			for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
			{
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.LihzahrdAltar)
					return templeAltar = new Point(x, y);
			}
		}
		return null;
	}

	static PlanStep PlanBulb(Bot bot, BossChallenge challenge, BulbTrigger trigger)
	{
		if (KnownTile(bot, BulbTiles) is Point bulb)
			return new SummonBossStep(challenge, trigger, bulb.ToWorldCoordinates(), bulb);
		return new ScoutStep(Landmark.Jungle, true, TileID.PlanteraBulb);
	}

	public static bool IsFishingPole(Item item) => item.fishingPole > 0;

	static readonly int[] BugNets = { ItemID.BugNet, ItemID.GoldenBugNet, ItemID.FireproofBugNet };

	static PlanStep PlanBait(Bot bot, BossChallenge challenge, BaitTrigger trigger)
	{
		if (!Inventory.Items(bot.Avatar).Any(IsFishingPole))
		{
			var woodPole = ProgressionPlanner.Obtain(bot, ItemID.WoodFishingPole, 1);
			return woodPole is not BlockedStep ? woodPole : ProgressionPlanner.ObtainAny(bot, new int[] { ItemID.ReinforcedFishingPole, ItemID.FisherofSouls, ItemID.Fleshcatcher });
		}
		if (Inventory.Count(bot.Avatar, trigger.Bait) == 0)
		{
			if (trigger.Bait == ItemID.TruffleWorm && !BugNets.Any(net => Inventory.Count(bot.Avatar, net) > 0))
				return ProgressionPlanner.ObtainAny(bot, BugNets);
			return ProgressionPlanner.Obtain(bot, trigger.Bait, 1);
		}
		return Shoreline(bot) is Vector2 shore ? new SummonBossStep(challenge, trigger, shore, null) : new ScoutStep(Landmark.Ocean, false);
	}

	const int ShoreInset = 3;
	const int WorldEdgeMargin = 40;

	public static Vector2? Shoreline(Bot bot)
	{
		if (BotWorldSystem.KnowledgeOf(bot.Faction).SpanOf(Landmark.Ocean) is not var (minX, maxX))
			return null;
		int seaward = minX < Main.maxTilesX / 2 ? -1 : 1;
		int land = seaward < 0 ? maxX : minX;
		for (int x = land; x > WorldEdgeMargin && x < Main.maxTilesX - WorldEdgeMargin; x += seaward)
		{
			if (Main.tile[x, TileQuery.SurfaceY(x)].LiquidAmount == 0)
				continue;
			int shore = x - seaward * ShoreInset;
			return new Vector2(shore * 16f, (TileQuery.SurfaceY(shore) - 2) * 16f);
		}
		return new Vector2(land * 16f, (TileQuery.SurfaceY(land) - 2) * 16f);
	}

	const double LacewingSpawnEnd = 16200.0;

	static bool LacewingsFlying => !Main.dayTime && Main.time < LacewingSpawnEnd;

	static PlanStep PlanLacewing(Bot bot, BossChallenge challenge, LacewingTrigger trigger)
	{
		if (!challenge.CanSummonNow)
			return PlanStep.Idle;
		if (Inventory.Count(bot.Avatar, ItemID.EmpressButterfly) == 0)
		{
			if (!BugNets.Any(net => Inventory.Count(bot.Avatar, net) > 0))
				return ProgressionPlanner.ObtainAny(bot, BugNets);
			return LacewingsFlying ? ProgressionPlanner.Obtain(bot, ItemID.EmpressButterfly, 1) : PlanStep.Idle;
		}
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		return (Grounds.QuietSurfaceSpot(knowledge, Landmark.Hallow, bot.Faction.BaseTile) ?? Grounds.ArenaSpot(knowledge, Landmark.Hallow, bot.Faction.BaseTile)) is Vector2 place
			? new SummonBossStep(challenge, trigger, place, null)
			: new ScoutStep(Landmark.Hallow, false);
	}

	static PlanStep PlanCultists(Bot bot, BossChallenge challenge, CultistRitualTrigger trigger)
	{
		if (KnownLandmark(bot, Landmark.Dungeon) is not Point dungeon)
			return new ScoutStep(Landmark.Dungeon, false);
		if (RitualFloorGap() is Point gap)
			return Inventory.Items(bot.Avatar).Any(Bots.Tasks.FillTileTask.IsFiller) ? new FillTileStep(gap) : ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, RitualFillerBlocks);
		var ritual = Main.dungeonX > 0 ? (Grounds.StandingSpotNear(new Point(Main.dungeonX, Main.dungeonY - 1)) ?? new Point(Main.dungeonX, Main.dungeonY - 1)) : dungeon;
		return new SummonBossStep(challenge, trigger, ritual.ToWorldCoordinates(), null);
	}

	const int RitualFillerBlocks = 10;
	const int RitualColumnStep = 2;
	const int RitualFloorSlack = 3;

	static Point? RitualFloorGap()
	{
		int x = Main.dungeonX, y = Main.dungeonY;
		if (x <= 0 || y < 8)
			return null;
		var center = new Vector2(x * 16 + 8, y * 16 - 64 - 8 - 27);
		if (Terraria.GameContent.Events.CultistRitual.CheckFloor(center, out _))
			return null;
		var middle = center.ToTileCoordinates();
		foreach (int offset in new[] { -5, -3, 3, 5 })
		{
			int column = middle.X + offset * RitualColumnStep;
			if (HasRitualFloor(column, middle.Y))
				continue;
			for (int slack = 0; slack <= RitualFloorSlack; slack++)
			{
				foreach (int row in new[] { y + slack, y - slack })
				{
					if (!WorldGen.SolidTile(column, row) && !Collision.SolidTiles(column - 1, column + 1, row - 3, row - 1))
						return new Point(column, row);
				}
			}
		}
		return null;
	}

	static bool HasRitualFloor(int column, int middleRow)
	{
		for (int row = middleRow - 5; row < middleRow + 12; row++)
		{
			if ((WorldGen.SolidTile(column, row) || TileID.Sets.Platforms[Framing.GetTileSafely(column, row).TileType])
				&& (!Collision.SolidTiles(column - 1, column + 1, row - 3, row - 1)
					|| !Collision.SolidTiles(column, column, row - 3, row - 1) && !Collision.SolidTiles(column + 1, column + 1, row - 3, row - 2) && !Collision.SolidTiles(column - 1, column - 1, row - 3, row - 2)))
				return true;
		}
		return false;
	}

	public static readonly int[] Pillars = { NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust };

	public static NPC NearestPillar(Vector2 from) =>
		Main.npc.Where(npc => npc.active && Pillars.Contains(npc.type)).OrderBy(ShieldLeft).ThenBy(npc => Vector2.Distance(npc.Center, from)).FirstOrDefault();

	static int ShieldLeft(NPC pillar) => pillar.type switch
	{
		NPCID.LunarTowerSolar => NPC.ShieldStrengthTowerSolar,
		NPCID.LunarTowerVortex => NPC.ShieldStrengthTowerVortex,
		NPCID.LunarTowerNebula => NPC.ShieldStrengthTowerNebula,
		_ => NPC.ShieldStrengthTowerStardust
	};

	public static PlanStep LeftoverPillars(Bot bot)
	{
		var moonLord = BossCatalog.ChallengeOf(NPCID.MoonLordCore);
		if (moonLord == null || !BossCredit.HasDefeated(bot.Faction, moonLord) || bot.Faction.Rally != null && bot.Faction.RallyBossType != moonLord.BossType
			|| Main.npc.Any(npc => npc.active && npc.boss))
			return null;
		return NearestPillar(bot.Center) is NPC pillar && !SiegeStalled(bot, pillar) ? new SummonBossStep(moonLord, new PillarsTrigger(), pillar.Bottom, null) : null;
	}

	const uint SiegePatienceTicks = 5 * 3600;
	const uint SiegeRestTicks = 20 * 3600;
	const float SiegeProgressFraction = 0.05f;

	const uint SiegeTripTicks = 10 * 3600;

	static readonly Dictionary<int, (int pillar, int life, uint since, uint started)> sieges = new();
	static readonly Dictionary<int, uint> siegeRestUntil = new();

	public static bool PillarsWorthRaising(Factions.Faction faction) =>
		!Main.npc.Any(npc => npc.active && Pillars.Contains(npc.type)) && faction.WantsRefight(NPCID.MoonLordCore);

	static bool SiegeStalled(Bot bot, NPC pillar)
	{
		var faction = bot.Faction;
		uint now = Main.GameUpdateCount;
		if (siegeRestUntil.TryGetValue(faction.Id, out uint rest) && now < rest)
			return true;
		bool known = sieges.TryGetValue(faction.Id, out var siege) && siege.pillar == pillar.whoAmI;
		if (!known || pillar.life < siege.life - pillar.lifeMax * SiegeProgressFraction)
		{
			sieges[faction.Id] = (pillar.whoAmI, pillar.life, now, known ? siege.started : now);
			return false;
		}
		bool besieging = BotWorldSystem.Bots.Any(member => member.FactionId == faction.Id && member.IsAlive && Vector2.Distance(member.Center, pillar.Center) < PillarZone);
		if (!besieging && now - siege.started < SiegeTripTicks)
		{
			sieges[faction.Id] = siege with { since = now };
			return false;
		}
		if (now - siege.since < SiegePatienceTicks)
			return false;
		sieges.Remove(faction.Id);
		siegeRestUntil[faction.Id] = now + SiegeRestTicks;
		if (faction.RallyBossType == NPCID.MoonLordCore)
			faction.Rally = null;
		BotDiagnostics.Log(bot, $"giving up on the {Lang.GetNPCNameValue(pillar.type)} at {pillar.Bottom.ToTileCoordinates()} for now: life {pillar.life}/{pillar.lifeMax} barely moved");
		return true;
	}

	const float PillarZone = 4000f;
	const float PillarZoneSlack = 16f * 25f;

	public static bool PillarsStanding() => Main.npc.Any(npc => npc.active && Pillars.Contains(npc.type));

	public static bool InPillarZone(Vector2 at) =>
		Main.npc.Any(npc => npc.active && Pillars.Contains(npc.type) && Vector2.Distance(npc.Center, at) < PillarZone + PillarZoneSlack);

	public static PlanStep PillarInTheWay(Bot bot, Vector2 destination)
	{
		var moonLord = BossCatalog.ChallengeOf(NPCID.MoonLordCore);
		if (moonLord == null || !BossCredit.HasDefeated(bot.Faction, moonLord))
			return null;
		float near = System.Math.Min(bot.Faction.BaseCenter.X, destination.X) - PillarZone;
		float far = System.Math.Max(bot.Faction.BaseCenter.X, destination.X) + PillarZone;
		var pillar = Main.npc.Where(npc => npc.active && Pillars.Contains(npc.type) && npc.Center.X > near && npc.Center.X < far)
			.OrderBy(ShieldLeft).ThenBy(npc => Vector2.Distance(npc.Center, bot.Center)).FirstOrDefault();
		return pillar == null || SiegeStalled(bot, pillar) ? null : new SummonBossStep(moonLord, new PillarsTrigger(), pillar.Bottom, null);
	}

	static PlanStep PlanPillars(Bot bot, BossChallenge challenge, PillarsTrigger trigger) =>
		NearestPillar(bot.Center) is NPC pillar ? new SummonBossStep(challenge, trigger, pillar.Bottom, null) : PlanStep.Blocked;

	static Point? KnownLandmark(Bot bot, Landmark landmark) => BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(landmark);

	static PlanStep PlanSummonItem(Bot bot, BossChallenge challenge, SummonItemTrigger trigger)
	{
		if (Inventory.Count(bot.Avatar, trigger.Item) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Item, 1);
		if (trigger.Place is not Landmark landmark)
		{
			var arena = Main.hardMode && SkyArena.For(bot.Faction) is { IsComplete: true } sky ? sky.Center : Grounds.CampArena(bot.Faction);
			return MaySetOut(bot, challenge, arena) ? new SummonBossStep(challenge, trigger, arena, null) : PlanStep.Idle;
		}
		if (Grounds.ArenaSpot(BotWorldSystem.KnowledgeOf(bot.Faction), landmark, bot.Faction.BaseTile) is not Vector2 place)
			return new ScoutStep(landmark, false);
		return MaySetOut(bot, challenge, place) ? new SummonBossStep(challenge, trigger, place, null) : PlanStep.Idle;
	}

	static bool MaySetOut(Bot bot, BossChallenge challenge, Vector2 place) =>
		bot.Faction.Rally != null && bot.Faction.RallyLeaderId == bot.Id && challenge.WindowStillOpen || challenge.CanSetOut(FarthestTeammateTiles(bot, place));

	static float FarthestTeammateTiles(Bot bot, Vector2 place)
	{
		float viaHome = Vector2.Distance(bot.Faction.BaseCenter, place) / 16f;
		return BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive)
			.Select(member => System.Math.Min(Vector2.Distance(member.Center, place) / 16f, Bots.Tasks.BotTask.CanRecall(member) ? viaHome : float.MaxValue))
			.DefaultIfEmpty(0f).Max();
	}

	static PlanStep PlanSmashOrbs(Bot bot, BossChallenge challenge, SmashOrbsTrigger trigger)
	{
		if (bot.Hands.BestHammer == null)
			return ProgressionPlanner.ObtainAny(bot, Hammers);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		var evil = BossCatalog.EvilOf(challenge);
		var orb = KnownShrine(bot, evil);
		if (orb is not Point shrine)
		{
			if (knowledge.ShrineSearchExhausted(evil, ShrineSearchesBeforeGivingUp, ShrineRetryTicks))
				return PlanStep.Blocked;
			return new ScoutStep(evil, knowledge.LandmarkOf(evil) != null);
		}
		bool final = WorldGen.shadowOrbCount >= 2;
		return final ? new SummonBossStep(challenge, trigger, shrine.ToWorldCoordinates(), shrine) : new SmashShrineStep(shrine);
	}

	static PlanStep PlanSmashLarva(Bot bot, BossChallenge challenge, SmashLarvaTrigger trigger)
	{
		if (KnownShrine(bot, Landmark.Jungle) is Point larva)
			return new SummonBossStep(challenge, trigger, larva.ToWorldCoordinates(), larva);
		if (!World.TilePresence.AnyExists(LarvaTiles))
			return PlanStep.Blocked;
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		if (knowledge.ShrineSearchExhausted(Landmark.Jungle, ShrineSearchesBeforeGivingUp, ShrineRetryTicks))
			return PlanStep.Blocked;
		return new ScoutStep(Landmark.Jungle, knowledge.LandmarkOf(Landmark.Jungle) != null);
	}

	static PlanStep PlanCurse(Bot bot, BossChallenge challenge, CurseOldManTrigger trigger)
	{
		if (NPC.downedBoss3)
			return PlanStep.Blocked;
		if (KnownLandmark(bot, Landmark.Dungeon) is not Point dungeon)
			return new ScoutStep(Landmark.Dungeon, false);
		int oldMan = NPC.FindFirstNPC(NPCID.OldMan);
		var place = oldMan >= 0 ? Main.npc[oldMan].Center : dungeon.ToWorldCoordinates();
		return MaySetOut(bot, challenge, place) ? new SummonBossStep(challenge, trigger, place, null) : PlanStep.Idle;
	}

	static PlanStep PlanClothier(Bot bot, BossChallenge challenge, ClothierDollTrigger trigger)
	{
		if (!NPC.downedBoss3)
			return PlanStep.Blocked;
		int clothier = NPC.FindFirstNPC(NPCID.Clothier);
		if (clothier < 0)
			return PlanStep.Blocked;
		if (!Inventory.Owns(bot.Avatar, ItemID.ClothierVoodooDoll))
			return ProgressionPlanner.Obtain(bot, ItemID.ClothierVoodooDoll, 1);
		return challenge.CanSummonNow ? new SummonBossStep(challenge, trigger, Main.npc[clothier].Center, null) : PlanStep.Idle;
	}

	static PlanStep PlanVoodoo(Bot bot, BossChallenge challenge, VoodooDollTrigger trigger)
	{
		if (!NPC.AnyNPCs(NPCID.Guide))
			return PlanStep.Blocked;
		var bridge = bot.Faction.Hellbridge;
		if (bridge == null || !bridge.IsComplete && bridge.BuiltLength < Hellbridge.FightableLength * 3 / 4)
			return PlanStep.Idle;
		bool inUnderworld = bot.CenterTile.Y >= Main.UnderworldLayer;
		if (Inventory.Count(bot.Avatar, ItemID.GuideVoodooDoll) == 0)
			return inUnderworld ? ProgressionPlanner.Obtain(bot, ItemID.GuideVoodooDoll, 1) : new TravelStep(bridge.Center);
		return new SummonBossStep(challenge, trigger, bridge.SummonPoint, null);
	}

	const int ShaftOffsetFromBase = 24;
	const int ShaftInApron = 2;

	public static Hellevator ShaftFor(Factions.Faction faction)
	{
		if (faction.Hellevator is { } shaft && faction.Settlement is { } camp && !UnderHall(shaft, camp) && (shaft.Right >= camp.Left && shaft.X <= camp.Right || shaft.TopY != camp.Floor - 1))
			faction.Hellevator = null;
		var planned = faction.Hellevator ??= PlanShaft(faction);
		planned.LandingRow = faction.Hellbridge?.Y;
		return planned;
	}

	static bool UnderHall(Hellevator shaft, Building.Settlement camp) =>
		shaft.TopY == camp.Hall.Floor && shaft.X > camp.Hall.Left && shaft.Right < camp.Hall.Right;

	static Hellevator PlanShaft(Factions.Faction faction)
	{
		if (faction.Settlement is { } camp)
		{
			int hallX = (camp.Hall.Left + camp.Hall.Right) / 2 - 1;
			return hallX > camp.Hall.Left && hallX + Hellevator.Width - 1 < camp.Hall.Right
				? new Hellevator(hallX, camp.Hall.Floor)
				: new Hellevator(camp.Right + ShaftInApron, camp.Floor - 1);
		}
		int x = faction.BaseTile.X + ShaftOffsetFromBase;
		return new Hellevator(x, TileQuery.SurfaceY(x) - 1);
	}

	public static int BridgeDirection(Bot bot) => bot.Id % 2 == 0 ? 1 : -1;

	public static bool PreparingForWall(Factions.Faction faction) =>
		NPC.AnyNPCs(NPCID.Guide) && BossCatalog.ChallengeOf(NPCID.WallofFlesh) is { } wall && !BossCredit.HasDefeated(faction, wall)
		&& (BossCredit.NextChallengeFor(faction)?.BossType == NPCID.WallofFlesh || ReadyForWall(faction));

	const uint WallReadinessTicks = 600;
	static readonly Dictionary<int, (uint at, bool ready)> wallReadiness = new();

	public static bool ReadyForWall(Factions.Faction faction)
	{
		if (Main.hardMode)
			return true;
		if (wallReadiness.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.at < WallReadinessTicks)
			return cached.ready;
		var summoner = BotWorldSystem.Bots.Where(member => member.FactionId == faction.Id && member.IsAlive).MaxBy(member => member.Personality.Combat);
		bool ready = summoner != null && BossCatalog.ChallengeOf(NPCID.WallofFlesh) is { } wall && BossReadiness.Evaluate(faction, wall, summoner).IsReady;
		wallReadiness[faction.Id] = (Main.GameUpdateCount, ready);
		return ready;
	}

	public static PlanStep PlanShaftWork(Bot bot)
	{
		var shaft = ShaftFor(bot.Faction);
		if (shaft.IsFinished || !IsChiefDigger(bot, shaft))
			return PlanStep.Idle;
		if (bot.Hands.HasRope && HighwayGoal.Hang(bot, shaft.RopeX, shaft.RopeTop, shaft.RopeBottom(), DiggerRopeTrip) is { } hang)
			return hang;
		return ShaftWork(bot, shaft);
	}

	const int BombRestock = 3;
	const int DiggerRopeTrip = 200;
	const int WaterBombRestock = 15;
	const int ShaftPlatformRestock = 20;

	public static bool CanDigRow(Bot bot, Hellevator shaft, int row) => CanDigRowWith(bot.Hands.BestPickaxe?.pick ?? 0, shaft, row);

	public static bool CanDigRowWith(int pick, Hellevator shaft, int row)
	{
		for (int column = shaft.X; column <= shaft.X + 1; column++)
		{
			if (!TileQuery.IsSolid(column, row))
				continue;
			if (pick <= 0 || Navigation.PickRequirement.MinimumPick(Main.tile[column, row].TileType, column, row) > pick || Building.HouseInspector.IsProtected(column, row))
				return false;
		}
		return true;
	}

	static readonly HashSet<int> platformRestock = new();

	static PlanStep ObtainBombs(Bot bot, int amount) =>
		ProgressionPlanner.Obtain(bot, ItemID.StickyBomb, amount) is var sticky and not (BlockedStep or IdleStep) ? sticky : ProgressionPlanner.Obtain(bot, ItemID.Bomb, amount);

	public static bool RowTouchesLava(Hellevator shaft, int row)
	{
		for (int x = shaft.X; x <= shaft.Right; x++)
			if (TileQuery.IsSolid(x, row) && TileQuery.TouchesLava(x, row))
				return true;
		return false;
	}

	public static bool BreathesUnderwater(Bot bot) => bot.Avatar.gills || bot.Avatar.accMerman || bot.Avatar.accDivingHelm;

	public static bool NeedsWaterBlast(Bot bot, Hellevator shaft) =>
		!BreathesUnderwater(bot) && shaft.WaterAbove(shaft.WorkRow()) > Hellevator.DesperateDiveRows;

	public static bool ShaftTooFlooded(Bot bot, Hellevator shaft)
	{
		if (Main.GameUpdateCount < shaft.FloodedUntil)
			return !BreathesUnderwater(bot);
		if (!NeedsWaterBlast(bot, shaft) || shaft.WaterBlasts < Hellevator.MaxWaterBlasts)
			return false;
		shaft.FloodedUntil = Main.GameUpdateCount + Hellevator.FloodPauseTicks;
		shaft.WaterBlasts = 0;
		BotDiagnostics.Log(bot, $"hellevator at x={shaft.X} is still flooded {shaft.WaterAbove(shaft.WorkRow())} rows deep after {Hellevator.MaxWaterBlasts} blasts, leaving it for a while");
		return true;
	}

	public static PlanStep ShaftWork(Bot bot, Hellevator shaft)
	{
		if (!CanDigRow(bot, shaft, shaft.WorkRow()))
			return PlanStep.Blocked;
		if (ShaftTooFlooded(bot, shaft))
			return PlanStep.Idle;
		if (NeedsWaterBlast(bot, shaft) && !bot.Hands.Explosives.Carries)
			return ObtainBombs(bot, WaterBombRestock);
		if (RowTouchesLava(shaft, shaft.WorkRow()) && !bot.Hands.Explosives.Carries && ObtainBombs(bot, BombRestock) is var lavaBombs and not (BlockedStep or IdleStep))
			return lavaBombs;
		int carried = Inventory.Items(bot.Avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack);
		bool roped = Bots.Tasks.RopeColumnTask.MissingRow(shaft.RopeX, shaft.RopeTop, shaft.RopeBottom()) == null;
		if (carried == 0 && !roped)
			platformRestock.Add(bot.Id);
		else if (carried >= ShaftPlatformRestock || roped)
			platformRestock.Remove(bot.Id);
		if (platformRestock.Contains(bot.Id) && ProgressionPlanner.ObtainPlatforms(bot, ShaftPlatformRestock) is var platforms and not BlockedStep and not IdleStep)
			return platforms;
		if (!Hellevator.LavaBelow(shaft.X, shaft.WorkRow()))
			return new DigShaftStep(shaft);
		if (shaft.LavaBlasts >= Hellevator.MaxLavaBlasts)
			return PlanStep.Idle;
		return bot.Hands.Explosives.Carries ? new DigShaftStep(shaft) : ObtainBombs(bot, BombRestock);
	}

	static bool IsChiefDigger(Bot bot, Hellevator shaft)
	{
		int row = shaft.WorkRow();
		return BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive && CanDigRow(member, shaft, row))
			.MaxBy(member => member.Personality.Mining) == bot;
	}

	public static PlanStep PlanBridgeHelp(Bot bot)
	{
		var shaft = bot.Faction.Hellevator;
		bool underworldReachable = bot.CenterTile.Y >= Main.UnderworldLayer || shaft?.IsComplete == true;
		if (!underworldReachable)
			return PlanStep.Idle;
		Point anchor;
		if (shaft?.IsComplete == true)
		{
			anchor = new Point(shaft.X, Main.UnderworldLayer);
			if (bot.Faction.Hellbridge is { } planned && (planned.Y < Hellbridge.FightRow - BridgeDepthTolerance
				|| !planned.IsComplete && planned.BuiltLength < BridgeReplanLength && System.Math.Abs(planned.CenterX - shaft.X) > BridgeShaftTolerance))
				bot.Faction.Hellbridge = null;
		}
		else if (KnownLandmark(bot, Landmark.Underworld) is Point underworld)
			anchor = underworld;
		else
			return new ScoutStep(Landmark.Underworld, true);
		var bridge = bot.Faction.Hellbridge ??= Hellbridge.Plan(anchor);
		if (shaft != null)
			shaft.LandingRow = bridge.Y;
		return bridge.IsComplete ? PlanStep.Idle : BridgeWork(bot, bridge);
	}

	const int BridgeShaftTolerance = 30;
	const int BridgeReplanLength = 10;
	const int BridgeDepthTolerance = 10;
	const int BridgeMaterialRestock = 300;
	const int BridgePlatformStone = 8;

	static PlanStep BridgeWork(Bot bot, Hellbridge bridge)
	{
		if (!bot.Hands.HasBridgeMaterial)
			return ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, BridgeMaterialRestock);
		if (Inventory.Count(bot.Avatar, ItemID.StonePlatform) == 0 && Inventory.Count(bot.Avatar, ItemID.StoneBlock) >= BridgePlatformStone
			&& ProgressionPlanner.ObtainPlatforms(bot, BridgePlatformStone * 8) is CraftStep craft)
			return craft;
		int preferred = BridgeDirection(bot);
		int direction = bridge.SideComplete(preferred) ? -preferred : preferred;
		if (!bot.Hands.Explosives.Carries && Bots.Tasks.BuildBridgeTask.BlocksWithoutBombs(bot, bridge, bridge.Frontier(direction)))
		{
			if (!bridge.SideComplete(-direction) && !Bots.Tasks.BuildBridgeTask.BlocksWithoutBombs(bot, bridge, bridge.Frontier(-direction)))
				direction = -direction;
			else
				return ProgressionPlanner.Obtain(bot, ItemID.Bomb, BombRestock);
		}
		return new BuildBridgeStep(bridge, direction);
	}
}
