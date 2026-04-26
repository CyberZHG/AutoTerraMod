using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class FleeTask : BotTask
{
	const float ThreatRadius = 16f * 25f;
	const float FleeDistance = 16f * 30f;

	NPC threat;

	public FleeTask(Bot bot) : base(bot) { }

	public override string NameKey => "Flee";
	public override bool IsUrgent => true;

	public override float Evaluate()
	{
		if (HealthFraction >= Traits.FleeHealthFraction)
			return 0f;
		threat = Engagement.NearestHostile(Bot, ThreatRadius, false);
		if (threat == null || HoldingArena(threat) || Engagement.KitingWall(Bot) || InBossFight() || Navigation.PathFinder.InsideMaze(Bot.CenterTile))
			return 0f;
		Detail = threat.GivenOrTypeName;
		return 90f + (1f - Traits.Courage) * 20f;
	}

	const float BossFightRadius = 16f * 120f;

	bool InBossFight() =>
		Bosses.BossCatalog.NearestActiveBoss(Bot.Center, BossFightRadius) is NPC boss && !Bot.IsIgnoring(boss)
		&& (!Bosses.BossCredit.SummonedByOther(boss, Bot.FactionId) || Vector2.Distance(Bot.Center, boss.Center) <= 16f * 50f);

	bool HoldingArena(NPC npc) =>
		Bosses.BossCatalog.ChallengeOf(npc.type) is { Arena: World.Landmark arena } && Bosses.BossChallenge.InArena(Avatar, arena);

	public override void Tick()
	{
		Bot.Hands.DrinkHealingPotion();
		if (threat == null || !threat.active)
		{
			IsFinished = true;
			return;
		}
		var center = Bot.Center;
		int away = center.X >= threat.Center.X ? 1 : -1;
		Bot.Motor.MoveTo(center + new Vector2(away * FleeDistance, 0f), 16f);
	}
}

public sealed class FightTask : BotTask
{
	const float CloseThreatDistance = 16f * 6f;

	const float BrawlerThreshold = 0.5f;

	NPC target;
	bool closeThreat;

	public FightTask(Bot bot) : base(bot) { }

	public override string NameKey => "Fight";
	public override bool IsUrgent => closeThreat;

	const float BossEngagementRange = 16f * 400f;
	const float ForeignBossReach = 16f * 50f;
	const float BossApproachDistance = 16f * 60f;
	const float BossPriority = 115f;
	const float InterruptPriority = 95f;
	const float AmbushRadius = 16f * 10f;
	const float BlockingRadius = 16f * 5f;
	const float BossDrinkHealthFraction = 0.5f;
	const float DrinkHealthFraction = 0.35f;

	public override float Evaluate()
	{
		closeThreat = false;
		var boss = Bosses.BossCatalog.NearestActiveBoss(Bot.Center, BossEngagementRange) ?? Bosses.BossCredit.EngagedBoss(Bot);
		if (boss != null && Bosses.BossCredit.SummonedByOther(boss, Bot.FactionId) && Vector2.Distance(Bot.Center, boss.Center) > ForeignBossReach)
			boss = null;
		if (boss != null && !Bot.IsIgnoring(boss))
		{
			if (BossFightPest() is NPC pest)
			{
				target = pest;
				closeThreat = true;
				Detail = pest.GivenOrTypeName;
				return BossPriority;
			}
			target = boss;
			closeThreat = true;
			Detail = boss.GivenOrTypeName;
			return BossPriority;
		}
		var interferer = Interferer();
		if (interferer != null)
		{
			target = interferer;
			closeThreat = true;
			Detail = interferer.GivenOrTypeName;
			return HealthFraction < Traits.FleeHealthFraction ? InterruptPriority * 0.3f : InterruptPriority;
		}
		float aggroRadius = Traits.Combat >= BrawlerThreshold ? 16f * (8f + Traits.Combat * 22f) : CloseThreatDistance;
		if (GuardingRally())
			aggroRadius = System.Math.Max(aggroRadius, RallyGuardRadius);
		target = Engagement.NearestHostile(Bot, aggroRadius, true);
		float score;
		if (target != null)
		{
			closeThreat = Vector2.Distance(Bot.Center, target.Center) < CloseThreatDistance;
			score = closeThreat ? 70f + Traits.Combat * 20f : 30f + Traits.Combat * 50f;
		}
		else if (Traits.Cooperation > 0.4f && (target = Engagement.ThreatToTeammates(Bot, 16f * 30f, 16f * 12f)) != null)
		{
			score = 30f + Traits.Cooperation * 40f;
		}
		else
		{
			return 0f;
		}
		if (GuardingRally() && Vector2.Distance(Bot.Center, target.Center) < RallyGuardRadius)
			score = System.Math.Max(score, RallyGuardPriority);
		Detail = target.GivenOrTypeName;
		return HealthFraction < Traits.FleeHealthFraction ? score * 0.3f : score;
	}

	const float RallyGuardPriority = 94f;
	const float RallyGuardRadius = 16f * 25f;
	const float RallyGuardReach = 16f * 40f;

	bool GuardingRally() => Bot.Faction.Rally is { } rally && Vector2.Distance(Bot.Center, rally.point) < RallyGuardReach;

	const float PestRadius = 16f * 6f;

	NPC BossFightPest()
	{
		var attacker = Bot.Vitals.RecentAttacker;
		if (attacker != null && attacker.active && !Bosses.BossCatalog.IsBossPart(attacker) && Engagement.IsHostileTo(Bot, attacker) && !Bot.IsIgnoring(attacker)
			&& Vector2.Distance(Bot.Center, attacker.Center) < AmbushRadius)
			return attacker;
		var near = Engagement.NearestHostile(Bot, PestRadius, true);
		return near != null && !Bosses.BossCatalog.IsBossPart(near) ? near : null;
	}

	NPC Interferer()
	{
		var attacker = Bot.Vitals.RecentAttacker;
		if (attacker != null && Engagement.IsHostileTo(Bot, attacker) && !Bot.IsIgnoring(attacker) && Vector2.Distance(Bot.Center, attacker.Center) < AmbushRadius)
			return attacker;
		if (Bot.Motor.Destination is not Vector2 destination)
			return null;
		var blocker = Engagement.NearestHostile(Bot, BlockingRadius, true);
		if (blocker == null)
			return null;
		bool ahead = System.Math.Sign(blocker.Center.X - Bot.Center.X) == System.Math.Sign(destination.X - Bot.Center.X);
		return ahead ? blocker : null;
	}

	public override void Tick()
	{
		bool bossPhase = target != null && target.active && Bosses.BossCatalog.IsBossPart(target);
		if (target == null || (!bossPhase && !Engagement.IsHostileTo(Bot, target)) || Bot.IsIgnoring(target))
		{
			IsFinished = true;
			return;
		}
		if (HealthFraction < (Bosses.BossCatalog.IsBossPart(target) ? BossDrinkHealthFraction : DrinkHealthFraction))
			Bot.Hands.DrinkHealingPotion();
		if (Bosses.BossCatalog.IsBossPart(target))
			Bot.Hands.DrinkBattlePotions();
		if (Bosses.BossCatalog.IsBossPart(target) && Vector2.Distance(Bot.Center, target.Center) > BossApproachDistance)
		{
			TravelOrTunnel(target.Bottom, BossApproachDistance * 0.5f);
			return;
		}
		Engagement.Fight(Bot, target);
	}
}
