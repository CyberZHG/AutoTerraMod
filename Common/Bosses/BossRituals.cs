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

	static Point? KnownShrine(Bot bot, Landmark landmark) => KnownTile(bot, ShrineTilesOf(landmark));

	static Point? KnownTile(Bot bot, HashSet<int> tiles) =>
		BotWorldSystem.KnowledgeOf(bot.Faction).NearestKnown(bot.Faction.BaseTile, tiles, new HashSet<Point>(), ShrineSearchRadius);

	public static IEnumerable<BossTrigger> InPreferredOrder(Bot bot, BossChallenge challenge) =>
		challenge.Triggers.OrderBy(trigger => trigger switch
		{
			SummonItemTrigger item when Inventory.Count(bot.Avatar, item.Item) > 0 => 0,
			SmashOrbsTrigger when !CanMineEvilStone(bot) => 2,
			_ => 1
		});

	public static bool CanMineEvilStone(Bot bot) =>
		(bot.Hands.BestPickaxe?.pick ?? 0) >= Navigation.PickRequirement.MinimumPick(WorldGen.crimson ? TileID.Crimstone : TileID.Ebonstone, 0, 0);

	public static PlanStep Plan(Bot bot, BossChallenge challenge, BossTrigger trigger) => trigger switch
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
		if (Inventory.Count(bot.Avatar, trigger.Item) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Item, 1);
		if (KnownTile(bot, AltarTiles) is not Point altar)
			return new ScoutStep(trigger.Place, true);
		return new SummonBossStep(challenge, trigger, altar.ToWorldCoordinates(), altar);
	}

	static PlanStep PlanBulb(Bot bot, BossChallenge challenge, BulbTrigger trigger)
	{
		if (KnownTile(bot, BulbTiles) is Point bulb)
			return new SummonBossStep(challenge, trigger, bulb.ToWorldCoordinates(), bulb);
		return new ScoutStep(Landmark.Jungle, true);
	}

	public static bool IsFishingPole(Item item) => item.fishingPole > 0;

	static PlanStep PlanBait(Bot bot, BossChallenge challenge, BaitTrigger trigger)
	{
		if (!Inventory.Items(bot.Avatar).Any(IsFishingPole))
			return ProgressionPlanner.ObtainAny(bot, new int[] { ItemID.WoodFishingPole, ItemID.ReinforcedFishingPole, ItemID.FisherofSouls, ItemID.Fleshcatcher });
		if (Inventory.Count(bot.Avatar, trigger.Bait) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Bait, 1);
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

	static PlanStep PlanLacewing(Bot bot, BossChallenge challenge, LacewingTrigger trigger)
	{
		if (!challenge.CanSummonNow)
			return PlanStep.Idle;
		return Grounds.ArenaSpot(BotWorldSystem.KnowledgeOf(bot.Faction), Landmark.Hallow, bot.Faction.BaseTile) is Vector2 place
			? new SummonBossStep(challenge, trigger, place, null)
			: new ScoutStep(Landmark.Hallow, false);
	}

	static PlanStep PlanCultists(Bot bot, BossChallenge challenge, CultistRitualTrigger trigger)
	{
		if (KnownLandmark(bot, Landmark.Dungeon) is not Point dungeon)
			return new ScoutStep(Landmark.Dungeon, false);
		return new SummonBossStep(challenge, trigger, dungeon.ToWorldCoordinates(), null);
	}

	static readonly int[] Pillars = { NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust };

	public static NPC NearestPillar(Vector2 from) =>
		Main.npc.Where(npc => npc.active && Pillars.Contains(npc.type)).OrderBy(npc => Vector2.Distance(npc.Center, from)).FirstOrDefault();

	static PlanStep PlanPillars(Bot bot, BossChallenge challenge, PillarsTrigger trigger) =>
		NearestPillar(bot.Center) is NPC pillar ? new SummonBossStep(challenge, trigger, pillar.Bottom, null) : PlanStep.Blocked;

	static Point? KnownLandmark(Bot bot, Landmark landmark) => BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(landmark);

	static PlanStep PlanSummonItem(Bot bot, BossChallenge challenge, SummonItemTrigger trigger)
	{
		if (Inventory.Count(bot.Avatar, trigger.Item) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Item, 1);
		if (trigger.Place is not Landmark landmark)
		{
			var arena = Grounds.CampArena(bot.Faction);
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
			.Select(member => System.Math.Min(Vector2.Distance(member.Center, place) / 16f, Bots.Tasks.BotTask.CarriesRecall(member.Avatar) ? viaHome : float.MaxValue))
			.DefaultIfEmpty(0f).Max();
	}

	static PlanStep PlanSmashOrbs(Bot bot, BossChallenge challenge, SmashOrbsTrigger trigger)
	{
		if (bot.Hands.BestHammer == null)
			return ProgressionPlanner.ObtainAny(bot, Hammers);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		var orb = KnownShrine(bot, Landmarks.WorldEvil);
		if (orb is not Point shrine)
		{
			if (knowledge.FruitlessShrineSearches >= ShrineSearchesBeforeGivingUp)
				return PlanStep.Blocked;
			var evil = Landmarks.WorldEvil;
			return new ScoutStep(evil, knowledge.LandmarkOf(evil) != null);
		}
		bool final = WorldGen.shadowOrbCount >= 2;
		return final ? new SummonBossStep(challenge, trigger, shrine.ToWorldCoordinates(), shrine) : new SmashShrineStep(shrine);
	}

	static PlanStep PlanSmashLarva(Bot bot, BossChallenge challenge, SmashLarvaTrigger trigger)
	{
		if (KnownShrine(bot, Landmark.Jungle) is Point larva)
			return new SummonBossStep(challenge, trigger, larva.ToWorldCoordinates(), larva);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		if (knowledge.FruitlessShrineSearches >= ShrineSearchesBeforeGivingUp)
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
		if (faction.Hellevator is { } shaft && faction.Settlement is { } camp && (shaft.Right >= camp.Left && shaft.X <= camp.Right || shaft.TopY != camp.Floor - 1))
			faction.Hellevator = null;
		var planned = faction.Hellevator ??= PlanShaft(faction);
		planned.LandingRow = faction.Hellbridge?.Y;
		return planned;
	}

	static Hellevator PlanShaft(Factions.Faction faction)
	{
		if (faction.Settlement is { } camp)
			return new Hellevator(camp.Right + ShaftInApron, camp.Floor - 1);
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
		return ShaftWork(bot, shaft);
	}

	const int BombRestock = 3;
	const int ShaftPlatformRestock = 20;

	public static bool CanDigRow(Bot bot, Hellevator shaft, int row)
	{
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
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

	public static PlanStep ShaftWork(Bot bot, Hellevator shaft)
	{
		if (!CanDigRow(bot, shaft, shaft.FirstUnfinishedRow()))
			return PlanStep.Blocked;
		int carried = Inventory.Items(bot.Avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack);
		bool roped = Bots.Tasks.RopeColumnTask.MissingRow(shaft.RopeX, shaft.TopY, shaft.FirstImpassableRow() - 1) == null;
		if (carried == 0 && !roped)
			platformRestock.Add(bot.Id);
		else if (carried >= ShaftPlatformRestock || roped)
			platformRestock.Remove(bot.Id);
		if (platformRestock.Contains(bot.Id) && ProgressionPlanner.ObtainPlatforms(bot, ShaftPlatformRestock) is var platforms and not BlockedStep and not IdleStep)
			return platforms;
		if (!Hellevator.LavaBelow(shaft.X, shaft.FirstUnfinishedRow()))
			return new DigShaftStep(shaft);
		if (shaft.LavaBlasts >= Hellevator.MaxLavaBlasts)
			return PlanStep.Idle;
		return bot.Hands.Explosives.Carries ? new DigShaftStep(shaft) : ProgressionPlanner.Obtain(bot, ItemID.Bomb, BombRestock);
	}

	static bool IsChiefDigger(Bot bot, Hellevator shaft)
	{
		int row = shaft.FirstUnfinishedRow();
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
