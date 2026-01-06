using System.Linq;
using AutoTerraMod.Common.Bosses;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class SummonTask : BotTask
{
	const int RallyTicks = 1800;
	const float GatherRadius = 16f * 20f;
	const float ArrivalRadius = 16f * 10f;
	const float StuckAcceptRadius = 16f * 25f;

	SummonBossStep step;

	public SummonTask(Bot bot) : base(bot) { }

	public override string NameKey => "Summon";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SummonBossStep summon)
			return 0f;
		step = summon;
		Detail = Lang.GetNPCNameValue(summon.BossType);
		return 85f;
	}

	public override void Start()
	{
		base.Start();
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until)
			Bot.Faction.Rally = (Bot.Faction.BaseCenter, Main.GameUpdateCount + RallyTicks);
	}

	public override void Tick()
	{
		var challenge = BossCatalog.Challenges.FirstOrDefault(candidate => candidate.BossType == step.BossType);
		if (challenge == null || !challenge.CanSummonNow(Avatar) || Inventory.Count(Avatar, step.SummonItem) == 0)
		{
			IsFinished = true;
			return;
		}
		var home = Bot.Faction.BaseCenter;
		float distanceHome = Vector2.Distance(Bot.Center, home);
		bool closeEnough = distanceHome <= ArrivalRadius || (Bot.Motor.IsStuck && distanceHome <= StuckAcceptRadius);
		if (!closeEnough)
		{
			Bot.Motor.MoveTo(home, ArrivalRadius * 0.5f);
			return;
		}
		Bot.Motor.Stop();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId && member.IsAlive).ToList();
		bool everyoneHere = team.All(member => Vector2.Distance(member.Center, home) < GatherRadius);
		bool stillWaiting = Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		if (!everyoneHere && stillWaiting)
		{
			Bot.Watchdog.RecordProgress();
			return;
		}
		Summon(challenge, team.Count);
		IsFinished = true;
	}

	void Summon(BossChallenge challenge, int teamSize)
	{
		var item = Inventory.Items(Avatar).First(candidate => candidate.type == step.SummonItem);
		using (AvatarPresence.Enter())
			NPC.SpawnOnPlayer(Avatar.whoAmI, challenge.BossType);
		Inventory.ConsumeOne(Avatar, item);
		Bot.Faction.Rally = null;
		Bot.Watchdog.RecordProgress();
		BotDiagnostics.Log(Bot, $"summoned {challenge.Name} with {teamSize} teammates");
	}
}

public sealed class RallyTask : BotTask
{
	const float ArrivalRadius = 16f * 8f;

	public RallyTask(Bot bot) : base(bot) { }

	public override string NameKey => "Rally";

	public override float Evaluate() =>
		Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until && Bot.Brain.Plan is not SummonBossStep ? 80f : 0f;

	public override void Tick()
	{
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until)
		{
			IsFinished = true;
			return;
		}
		Bot.Motor.MoveTo(rally.point, ArrivalRadius);
		if (Bot.Motor.HasArrived)
			Bot.Watchdog.RecordProgress();
	}
}
