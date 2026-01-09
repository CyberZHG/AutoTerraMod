using System;
using AutoTerraMod.Common.Bots.Tasks;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class BotBrain
{
	const int ReconsiderInterval = 30;
	const float CurrentTaskBias = 1.15f;
	const int MinimumCommitmentTicks = 180;
	const int ObserveEveryReconsiders = 4;
	const int GoalSuppressionTicks = 3600;
	const int QuickFailureTicks = 30;
	const int QuickFailuresBeforeSuppression = 5;
	const int FailureLoopSuppressionTicks = 3600;

	readonly Bot bot;
	readonly BotTask[] tasks;
	BotTask current;
	int reconsiderTimer;
	uint committedUntil;
	uint currentStartedAt;
	int quickFailures;
	string lastDecisionSignature;
	ProgressionGoal currentGoal;
	readonly System.Collections.Generic.Dictionary<ProgressionGoal, uint> suppressedGoals = new();
	int reconsiderCount;
	bool frameFailureLogged;

	public BotBrain(Bot bot)
	{
		this.bot = bot;
		tasks = new BotTask[]
		{
			new EscapeTask(bot),
			new SummonTask(bot),
			new RallyTask(bot),
			new ScoutTask(bot),
			new SmashShrineTask(bot),
			new TravelTask(bot),
			new FleeTask(bot),
			new FightTask(bot),
			new PickupTask(bot),
			new CraftTask(bot),
			new BuildTask(bot),
			new GatherTask(bot),
			new HuntTask(bot),
			new LootTask(bot),
			new TreasureTask(bot),
			new StoreTask(bot),
			new DeliverTask(bot),
			new TidyCampTask(bot),
			new WithdrawTask(bot),
			new ReturnHomeTask(bot),
			new ExploreTask(bot),
			new IdleTask(bot)
		};
		reconsiderTimer = bot.Id % ReconsiderInterval;
	}

	public BotTask Current => current;
	public PlanStep Plan { get; private set; } = PlanStep.Idle;
	public string GoalName => currentGoal?.Describe() ?? "none";
	public System.Collections.Generic.HashSet<int> PlannedItemTypes { get; private set; } = new();

	public BotTask Override { get; set; }

	public void Tick(NPC body)
	{
		bot.Vitals.Tick(body);
		if (!bot.IsAlive)
			return;
		if (Override != null)
		{
			bot.Hands.Tick();
			Override.Tick();
			bot.Motor.Tick(body);
			AnimateAvatar(body);
			return;
		}
		bot.Hands.Tick();
		bot.Watchdog.Tick();
		if (--reconsiderTimer <= 0 || current == null || current.IsFinished)
			Reconsider();
		current?.Tick();
		bot.Motor.Tick(body);
		bot.Hands.CollectNearbyItems();
		AnimateAvatar(body);
	}

	public void SuppressCurrentGoal()
	{
		if (currentGoal != null)
			suppressedGoals[currentGoal] = Main.GameUpdateCount + GoalSuppressionTicks;
	}

	public void ForceReconsider()
	{
		reconsiderTimer = 0;
		committedUntil = 0;
		current?.Stop();
		current = null;
	}

	bool IsSuppressed(ProgressionGoal goal) =>
		suppressedGoals.TryGetValue(goal, out uint until) && Main.GameUpdateCount < until;

	public void Reset()
	{
		current?.Stop();
		current = null;
		Plan = PlanStep.Idle;
	}

	void Reconsider()
	{
		reconsiderTimer = ReconsiderInterval;
		BreakFailureLoops();
		Equipment.EquipBestArmor(bot.Avatar);
		StatBoosters.ConsumeUsable(bot);
		TreePlanter.PlantIfUseful(bot);
		if (++reconsiderCount % ObserveEveryReconsiders == 0)
		{
			using var _ = Systems.Perf.Measure(Systems.PerfSection.WorldScans);
			World.WorldScan.Observe(bot.CenterTile, Systems.BotWorldSystem.KnowledgeOf(bot.Faction));
		}
		Junk.TrashExcessIfCrowded(bot);
		TorchPlacer.LightUpIfDark(bot);
		using (Systems.Perf.Measure(Systems.PerfSection.Planner))
			Plan = ProgressionPlanner.NextStep(bot, IsSuppressed, out currentGoal);
		PlannedItemTypes = Kit.ComputePlannedItemTypes(bot);

		BotTask best = null;
		float bestScore = 0f;
		foreach (var task in tasks)
		{
			float score = task.Evaluate();
			if (task == current && !task.IsFinished)
				score *= CurrentTaskBias;
			if (score > bestScore)
			{
				best = task;
				bestScore = score;
			}
		}
		if (best == current && current is { IsFinished: false })
			return;
		bool stillCommitted = current is { IsFinished: false } && Main.GameUpdateCount < committedUntil;
		if (stillCommitted && best is not { IsUrgent: true })
			return;
		current?.Stop();
		current = best;
		current?.Start();
		currentStartedAt = Main.GameUpdateCount;
		committedUntil = Main.GameUpdateCount + MinimumCommitmentTicks;
		string signature = $"{current?.NameKey}|{current?.Detail}|{Plan}";
		if (signature != lastDecisionSignature)
			BotDiagnostics.LogDecision(bot, current, Plan);
		lastDecisionSignature = signature;
	}

	void BreakFailureLoops()
	{
		if (current is not { IsFinished: true })
			return;
		if (Main.GameUpdateCount - currentStartedAt > QuickFailureTicks)
		{
			quickFailures = 0;
			return;
		}
		if (++quickFailures < QuickFailuresBeforeSuppression || currentGoal == null)
			return;
		suppressedGoals[currentGoal] = Main.GameUpdateCount + FailureLoopSuppressionTicks;
		BotDiagnostics.Log(bot, $"{current.NameKey} keeps failing instantly, pausing its goal for a minute");
		quickFailures = 0;
	}

	void AnimateAvatar(NPC body)
	{
		BotVitals.SyncAvatar(body, bot.Avatar);
		try
		{
			bot.Avatar.PlayerFrame();
		}
		catch (Exception exception)
		{
			if (frameFailureLogged)
				return;
			frameFailureLogged = true;
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"PlayerFrame failed for {bot.Name}", exception);
		}
	}
}
