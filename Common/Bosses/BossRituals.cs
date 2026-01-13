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

	static readonly int[] Hammers =
	{
		ItemID.WoodenHammer, ItemID.CopperHammer, ItemID.TinHammer, ItemID.IronHammer, ItemID.LeadHammer,
		ItemID.SilverHammer, ItemID.TungstenHammer, ItemID.GoldHammer, ItemID.PlatinumHammer
	};

	static readonly HashSet<int> OrbTiles = new() { TileID.ShadowOrbs };
	static readonly HashSet<int> LarvaTiles = new() { TileID.Larva };

	static HashSet<int> ShrineTilesOf(Landmark landmark) => landmark == Landmark.Jungle ? LarvaTiles : OrbTiles;

	public static bool KnowsShrine(Bot bot, Landmark landmark) => KnownShrine(bot, landmark) != null;

	static Point? KnownShrine(Bot bot, Landmark landmark) =>
		BotWorldSystem.KnowledgeOf(bot.Faction).NearestKnown(bot.Faction.BaseTile, ShrineTilesOf(landmark), new HashSet<Point>(), ShrineSearchRadius);

	public static IEnumerable<BossTrigger> InPreferredOrder(Bot bot, BossChallenge challenge) =>
		challenge.Triggers.OrderBy(trigger => trigger is SummonItemTrigger item && Inventory.Count(bot.Avatar, item.Item) > 0 ? 0 : 1);

	public static PlanStep Plan(Bot bot, BossChallenge challenge, BossTrigger trigger) => trigger switch
	{
		SummonItemTrigger item => PlanSummonItem(bot, challenge, item),
		SmashOrbsTrigger orbs => PlanSmashOrbs(bot, challenge, orbs),
		SmashLarvaTrigger larva => PlanSmashLarva(bot, challenge, larva),
		CurseOldManTrigger curse => PlanCurse(bot, challenge, curse),
		VoodooDollTrigger voodoo => PlanVoodoo(bot, challenge, voodoo),
		ClothierDollTrigger clothier => PlanClothier(bot, challenge, clothier),
		_ => PlanStep.Blocked
	};

	static Point? KnownLandmark(Bot bot, Landmark landmark) => BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(landmark);

	static PlanStep PlanSummonItem(Bot bot, BossChallenge challenge, SummonItemTrigger trigger)
	{
		if (Inventory.Count(bot.Avatar, trigger.Item) == 0)
			return ProgressionPlanner.Obtain(bot, trigger.Item, 1);
		if (!challenge.CanSummonNow)
			return PlanStep.Idle;
		if (trigger.Place is not Landmark landmark)
			return new SummonBossStep(challenge, trigger, bot.Faction.BaseCenter, null);
		return Grounds.SurfaceSpotNear(BotWorldSystem.KnowledgeOf(bot.Faction), landmark, bot.Faction.BaseTile) is Vector2 place
			? new SummonBossStep(challenge, trigger, place, null)
			: new ScoutStep(landmark, false);
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
		return challenge.CanSummonNow ? new SummonBossStep(challenge, trigger, dungeon.ToWorldCoordinates(), null) : PlanStep.Idle;
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
		if (bridge == null || !bridge.IsComplete)
			return PlanStep.Idle;
		bool inUnderworld = bot.CenterTile.Y >= Main.UnderworldLayer;
		if (Inventory.Count(bot.Avatar, ItemID.GuideVoodooDoll) == 0)
			return inUnderworld ? ProgressionPlanner.Obtain(bot, ItemID.GuideVoodooDoll, 1) : new TravelStep(bridge.Center);
		return new SummonBossStep(challenge, trigger, bridge.Center, null);
	}

	const int ShaftOffsetFromBase = 24;

	static Hellevator PlanShaft(Factions.Faction faction)
	{
		int x = faction.BaseTile.X + ShaftOffsetFromBase;
		return new Hellevator(x, TileQuery.SurfaceY(x) - 1);
	}

	public static int BridgeDirection(Bot bot) => bot.Id % 2 == 0 ? 1 : -1;

	public static bool PreparingForWall(Factions.Faction faction) =>
		BossCredit.NextChallengeFor(faction)?.BossType == NPCID.WallofFlesh && NPC.AnyNPCs(NPCID.Guide);

	public static PlanStep PlanShaftWork(Bot bot)
	{
		var shaft = bot.Faction.Hellevator ??= PlanShaft(bot.Faction);
		if (shaft.IsComplete || bot.CenterTile.Y >= Main.UnderworldLayer || !IsChiefDigger(bot))
			return PlanStep.Idle;
		return new DigShaftStep(shaft);
	}

	static bool IsChiefDigger(Bot bot) =>
		BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive).MaxBy(member => member.Personality.Mining) == bot;

	public static PlanStep PlanBridgeHelp(Bot bot)
	{
		var shaft = bot.Faction.Hellevator;
		bool underworldReachable = bot.CenterTile.Y >= Main.UnderworldLayer || shaft?.IsComplete == true;
		if (!underworldReachable)
			return PlanStep.Idle;
		if (KnownLandmark(bot, Landmark.Underworld) is not Point underworld)
			return new ScoutStep(Landmark.Underworld, true);
		var bridge = bot.Faction.Hellbridge ??= Hellbridge.Plan(underworld);
		return bridge.IsComplete ? PlanStep.Idle : BridgeWork(bot, bridge);
	}

	const int BridgeMaterialRestock = 100;

	static PlanStep BridgeWork(Bot bot, Hellbridge bridge)
	{
		if (!bot.Hands.HasBridgeMaterial)
			return ProgressionPlanner.Obtain(bot, ItemID.WoodPlatform, BridgeMaterialRestock);
		return new BuildBridgeStep(bridge, BridgeDirection(bot));
	}
}
