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

	public static bool KnowsShrine(Bot bot) =>
		BotWorldSystem.KnowledgeOf(bot.Faction).NearestKnown(bot.Faction.BaseTile, OrbTiles, new HashSet<Point>(), ShrineSearchRadius) != null;

	public static IEnumerable<BossTrigger> InPreferredOrder(Bot bot, BossChallenge challenge) =>
		challenge.Triggers.OrderBy(trigger => trigger is SummonItemTrigger item && Inventory.Count(bot.Avatar, item.Item) > 0 ? 0 : 1);

	public static PlanStep Plan(Bot bot, BossChallenge challenge, BossTrigger trigger) => trigger switch
	{
		SummonItemTrigger item => PlanSummonItem(bot, challenge, item),
		SmashOrbsTrigger orbs => PlanSmashOrbs(bot, challenge, orbs),
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
		var orb = knowledge.NearestKnown(bot.Faction.BaseTile, OrbTiles, new HashSet<Point>(), ShrineSearchRadius);
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
		if (KnownLandmark(bot, Landmark.Underworld) is not Point underworld)
			return new ScoutStep(Landmark.Underworld, true);
		bool inUnderworld = bot.CenterTile.Y >= Main.UnderworldLayer;
		if (Inventory.Count(bot.Avatar, ItemID.GuideVoodooDoll) == 0)
			return inUnderworld ? ProgressionPlanner.Obtain(bot, ItemID.GuideVoodooDoll, 1) : new TravelStep(underworld.ToWorldCoordinates());
		return new SummonBossStep(challenge, trigger, underworld.ToWorldCoordinates(), null);
	}
}
