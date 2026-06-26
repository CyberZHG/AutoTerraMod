using System;
using AutoTerraMod.Common.Bots.Tasks;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
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
			new SurveyTask(bot),
			new SmashShrineTask(bot),
			new TravelTask(bot),
			new BuildBridgeTask(bot),
			new DigShaftTask(bot),
			new RopeColumnTask(bot),
			new OutpostTask(bot),
			new ClaimOutpostTask(bot),
			new FleeTask(bot),
			new FightTask(bot),
			new PickupTask(bot),
			new OpportunisticMineTask(bot),
			new CraftTask(bot),
			new BuildTask(bot),
			new GatherTask(bot),
			new HuntTask(bot),
			new LootTask(bot),
			new FillTileTask(bot),
			new SkyArenaTask(bot),
			new TreasureTask(bot),
			new RescueTask(bot),
			new BuyTask(bot),
			new FishingTask(bot),
			new DecorateTask(bot),
			new CollectorTask(bot),
			new NaturalistTask(bot),
			new StoreTask(bot),
			new DeliverTask(bot),
			new TidyCampTask(bot),
			new PlantTreesTask(bot),
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
	public bool WorksOnBoss => currentGoal is BossGoal;
	public bool PoolingForChampion => currentGoal is ChampionCraftGoal or ChampionPoolGoal or HandDownGoal;
	public bool OnExpedition => currentGoal?.IsExpedition == true || Plan is BuildBridgeStep or DigShaftStep
		|| (bot.CenterTile.Y >= Main.UnderworldLayer && Bosses.BossRituals.PreparingForWall(bot.Faction));
	public System.Collections.Generic.HashSet<int> PlannedItemTypes { get; private set; } = new();

	public BotTask Override { get; set; }

	const float QuickHealFraction = 0.4f;
	const float BossQuickHealFraction = 0.6f;
	const float BossNearbyRadius = 16f * 150f;

	void QuickHeal()
	{
		var avatar = bot.Avatar;
		if (avatar.statLife >= avatar.statLifeMax2 * BossQuickHealFraction)
			return;
		float threshold = Bosses.BossCatalog.NearestActiveBoss(bot.Center, BossNearbyRadius) != null ? BossQuickHealFraction : QuickHealFraction;
		if (avatar.statLife < avatar.statLifeMax2 * threshold)
			bot.Hands.DrinkHealingPotion();
	}

	public void Tick(NPC body)
	{
		using (Probes.Measure("Vitals"))
			bot.Vitals.Tick(body);
		if (!bot.IsAlive)
			return;
		bot.TrackMiningSession();
		if (Override != null)
		{
			bot.Hands.Tick();
			Override.Tick();
			bot.Motor.Tick(body);
			AnimateAvatar(body);
			return;
		}
		using (Probes.Measure("Hands"))
			bot.Hands.Tick();
		using (Probes.Measure("QuickHeal"))
			QuickHeal();
		using (Probes.Measure("Summon"))
		{
			if (bot.Hands.BestSummonWeapon() is Item staff)
				bot.Hands.Summon(staff);
		}
		using (Probes.Measure("Watchdog"))
			bot.Watchdog.Tick();
		if ((--reconsiderTimer <= 0 || current == null || current.IsFinished) && !TryReconsider())
			reconsiderTimer = 1;
		if (current is { IsFinished: false })
			using (Probes.Measure(current.ProbeLabel))
				current.Tick();
		if (current is not FightTask)
			using (Probes.Measure("Reflex"))
				Engagement.Reflex(bot);
		using (Probes.Measure("Motor"))
			bot.Motor.Tick(body);
		using (Probes.Measure("Collect"))
			bot.Hands.CollectNearbyItems();
		using (Probes.Measure("Animate"))
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
		ProgressionPlanner.Unstick(bot);
		current?.Stop();
		current = null;
		Plan = PlanStep.Idle;
	}

	const double ReconsiderBudgetMilliseconds = 4.0;

	static uint budgetTick;
	static long spentThisTick;

	bool TryReconsider()
	{
		if (budgetTick != Main.GameUpdateCount)
		{
			budgetTick = Main.GameUpdateCount;
			spentThisTick = 0;
		}
		if (spentThisTick * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= ReconsiderBudgetMilliseconds)
			return false;
		long began = System.Diagnostics.Stopwatch.GetTimestamp();
		Reconsider();
		spentThisTick += System.Diagnostics.Stopwatch.GetTimestamp() - began;
		return true;
	}

	void Reconsider()
	{
		reconsiderTimer = ReconsiderInterval;
		BreakFailureLoops();
		using (Probes.Measure("Equip"))
		{
			Equipment.EquipBestArmor(bot.Avatar);
			Equipment.EquipBestAccessories(bot.Avatar, PlannedItemTypes);
			Equipment.EquipMiscGear(bot.Avatar, PlannedItemTypes);
			DyeTaste.Wear(bot);
			Kit.ArrangeHotbar(bot);
		}
		using (Probes.Measure("Upkeep"))
		{
			StatBoosters.ConsumeUsable(bot);
			Bags.OpenOneAtHome(bot);
			BannerHanger.HangWhereFound(bot);
			bot.Hands.DrinkObsidianSkinIfInHell();
			Inventory.TidySpecialSlots(bot.Avatar);
		}
		if (++reconsiderCount % ObserveEveryReconsiders == 0)
		{
			using var _ = Systems.Perf.Measure(Systems.PerfSection.WorldScans);
			using var probe = Probes.Measure("Observe");
			World.WorldScan.Observe(bot.CenterTile, Systems.BotWorldSystem.KnowledgeOf(bot.Faction));
		}
		using (Probes.Measure("Junk"))
		{
			Junk.TrashExcessIfCrowded(bot);
			Junk.SellOutgrownLoot(bot);
		}
		using (Probes.Measure("Torch"))
			TorchPlacer.LightUpIfDark(bot);
		WaterBottler.FillWhileAtWater(bot);
		using (Systems.Perf.Measure(Systems.PerfSection.Planner))
		using (Probes.Measure("Planner"))
			Plan = ProgressionPlanner.NextStep(bot, IsSuppressed, out currentGoal);
		using (Probes.Measure("PlannedItems"))
			PlannedItemTypes = Kit.ComputePlannedItemTypes(bot);

		BotTask best = null;
		float bestScore = 0f;
		foreach (var task in tasks)
		{
			float score;
			using (Probes.Measure(task.EvaluateLabel))
				score = task.Evaluate();
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
		current?.Begin();
		currentStartedAt = Main.GameUpdateCount;
		committedUntil = Main.GameUpdateCount + MinimumCommitmentTicks;
		string signature = $"{current?.NameKey}|{current?.Detail}|{Plan}";
		if (signature != lastDecisionSignature)
			BotDiagnostics.LogDecision(bot, current, Plan);
		lastDecisionSignature = signature;
	}

	static bool ExecutesPlan(BotTask task) => task is SummonTask or ScoutTask or SurveyTask or SmashShrineTask or TravelTask or BuildBridgeTask
		or DigShaftTask or OutpostTask or CraftTask or BuildTask or GatherTask or HuntTask or BuyTask or DeliverTask or WithdrawTask;

	void BreakFailureLoops()
	{
		if (current is not { IsFinished: true } || !ExecutesPlan(current))
			return;
		if (current.Accomplished || Main.GameUpdateCount - currentStartedAt > QuickFailureTicks)
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
		bot.Avatar.pulley = bot.Motor.HoldsRope;
		if (bot.Avatar.pulley)
			bot.Avatar.pulleyDir = 1;
		try
		{
			bot.Avatar.PlayerFrame();
			if (bot.Avatar.wings > 0)
			{
				if (body.velocity.Y != 0f)
					bot.Avatar.WingFrame(bot.Motor.IsFlying);
				else
					bot.Avatar.wingFrame = 0;
			}
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
