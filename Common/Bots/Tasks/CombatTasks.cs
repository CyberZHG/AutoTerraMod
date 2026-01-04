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
		if (threat == null)
			return 0f;
		Detail = threat.GivenOrTypeName;
		return 90f + (1f - Traits.Courage) * 20f;
	}

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

	public override float Evaluate()
	{
		closeThreat = false;
		float aggroRadius = Traits.Combat >= BrawlerThreshold ? 16f * (8f + Traits.Combat * 22f) : CloseThreatDistance;
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
		Detail = target.GivenOrTypeName;
		return HealthFraction < Traits.FleeHealthFraction ? score * 0.3f : score;
	}

	public override void Tick()
	{
		if (target == null || !Engagement.IsHostileTarget(target) || Bot.IsIgnoring(target))
		{
			IsFinished = true;
			return;
		}
		Engagement.Fight(Bot, target);
	}
}
