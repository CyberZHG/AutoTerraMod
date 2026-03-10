using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Planning;

public static class ProgressionPlanner
{
	const int MaxDepth = 10;

	static ProgressionGoal[] ladder;

	public static void Warm() => ladder ??= ProgressionGoals.Build();

	public static void Reset()
	{
		ladder = null;
		upgradeGoals = null;
		expeditions.Clear();
		stuckPlans.Clear();
	}

	const int ExpeditionCommitmentTicks = 3 * 3600;

	static readonly Dictionary<int, (ProgressionGoal goal, uint until)> expeditions = new();

	const double SlowPlanMilliseconds = 10.0;

	public static PlanStep NextStep(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, out ProgressionGoal source)
	{
		long began = System.Diagnostics.Stopwatch.GetTimestamp();
		resolveCalls = 0;
		var result = NextStepInner(bot, isSuppressed, out source);
		double total = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		if (total > SlowPlanMilliseconds)
			BotDiagnostics.Log(bot, $"slow plan {total:0.0}ms toward {source?.Describe() ?? "nothing"} ({resolveCalls} resolves, step {result})");
		return result;
	}

	static PlanStep NextStepInner(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, out ProgressionGoal source)
	{
		ladder ??= ProgressionGoals.Build();
		foreach (var goal in ladder)
		{
			using var probe = Probes.Measure(goal.ProbeLabel);
			if (!goal.IsUrgent(bot) || isSuppressed(goal))
				continue;
			var step = PlanUnlessStuck(bot, goal);
			if (step is not BlockedStep and not IdleStep)
			{
				source = goal;
				return step;
			}
		}
		if (bot.UpgradeAfterDeath)
		{
			expeditions.Remove(bot.Id);
			if (UpgradeAfterDeath(bot, isSuppressed, out source) is PlanStep chore)
				return chore;
			bot.UpgradeAfterDeath = false;
		}
		if (ContinueExpedition(bot, isSuppressed) is PlanStep ongoing)
		{
			source = expeditions[bot.Id].goal;
			return ongoing;
		}
		if (ReadyUpgrade(bot, isSuppressed) is (ProgressionGoal upgrade, PlanStep ready))
		{
			source = upgrade;
			return ready;
		}
		var focus = FocusTrack(bot);
		if (WalkLadder(bot, isSuppressed, focus, out source) is PlanStep focused)
			return focused;
		if (WalkLadder(bot, isSuppressed, null, out source) is PlanStep any)
			return any;
		source = null;
		return PlanStep.Idle;
	}

	const uint TrackTurnTicks = 7200;
	static readonly GearTrack[] Tracks = { GearTrack.Tools, GearTrack.Armor, GearTrack.Weapons, GearTrack.Accessories };

	static GearTrack FocusTrack(Bot bot) => Tracks[(int)((Main.GameUpdateCount / TrackTurnTicks + (uint)bot.Id) % (uint)Tracks.Length)];

	static PlanStep WalkLadder(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, GearTrack? focus, out ProgressionGoal source)
	{
		foreach (var goal in ladder)
		{
			if (focus is GearTrack track && goal.Track != GearTrack.None && goal.Track != track)
				continue;
			using var probe = Probes.Measure(goal.ProbeLabel);
			if (isSuppressed(goal) || goal.NeedsSurface && bot.CommittedToMining || !goal.AppliesTo(bot) || goal.IsMet(bot))
				continue;
			var step = PlanUnlessStuck(bot, goal);
			if (step is not BlockedStep and not IdleStep)
			{
				if (goal.IsExpedition)
					expeditions[bot.Id] = (goal, Main.GameUpdateCount + ExpeditionCommitmentTicks);
				source = goal;
				return step;
			}
		}
		source = null;
		return null;
	}

	const int StuckRetryTicks = 240;

	static readonly Dictionary<(int bot, ProgressionGoal goal), (uint until, PlanStep step)> stuckPlans = new();

	static PlanStep PlanUnlessStuck(Bot bot, ProgressionGoal goal)
	{
		if (stuckPlans.TryGetValue((bot.Id, goal), out var stuck) && Main.GameUpdateCount < stuck.until)
			return stuck.step;
		var step = goal.Plan(bot);
		if (step is BlockedStep or IdleStep)
			stuckPlans[(bot.Id, goal)] = (Main.GameUpdateCount + StuckRetryTicks + (uint)(bot.Id * 37 % 120), step);
		else
			stuckPlans.Remove((bot.Id, goal));
		return step;
	}

	public static void Unstick(Bot bot)
	{
		foreach (var key in stuckPlans.Keys)
		{
			if (key.bot == bot.Id)
				stuckPlans.Remove(key);
		}
	}

	const int UpgradeScanTicks = 600;

	static readonly Dictionary<int, (uint at, ProgressionGoal goal)> readyUpgrades = new();

	const int UpgradeChecksPerStep = 3;

	static readonly Dictionary<int, int> upgradeCursor = new();
	static List<ProgressionGoal> upgradeGoals;

	static (ProgressionGoal, PlanStep)? ReadyUpgrade(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed)
	{
		if (readyUpgrades.TryGetValue(bot.Id, out var cached) && cached.goal != null && Main.GameUpdateCount - cached.at < UpgradeScanTicks)
		{
			if (!isSuppressed(cached.goal) && !cached.goal.IsMet(bot) && IsHandsOn(cached.goal.Plan(bot)) is PlanStep again)
				return (cached.goal, again);
			readyUpgrades.Remove(bot.Id);
		}
		upgradeGoals ??= ladder.Where(goal => ProgressionGoals.Unwrap(goal) is GearGoal or OwnGoal or CageGoal).ToList();
		if (upgradeGoals.Count == 0)
			return null;
		int cursor = upgradeCursor.GetValueOrDefault(bot.Id);
		for (int checkedGoals = 0; checkedGoals < UpgradeChecksPerStep; checkedGoals++)
		{
			var goal = upgradeGoals[(cursor + checkedGoals) % upgradeGoals.Count];
			using var probe = Probes.Measure(goal.ProbeLabel);
			if (isSuppressed(goal) || !goal.AppliesTo(bot) || goal.IsMet(bot))
				continue;
			if (IsHandsOn(goal.Plan(bot)) is PlanStep step)
			{
				upgradeCursor[bot.Id] = (cursor + checkedGoals + 1) % upgradeGoals.Count;
				readyUpgrades[bot.Id] = (Main.GameUpdateCount, goal);
				return (goal, step);
			}
		}
		upgradeCursor[bot.Id] = (cursor + UpgradeChecksPerStep) % upgradeGoals.Count;
		return null;
	}

	static ProgressionGoal craftableUpgrade;

	static PlanStep UpgradeAfterDeath(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, out ProgressionGoal source)
	{
		if (!bot.StoreAfterDeath)
		{
			source = null;
			return null;
		}
		craftableUpgrade ??= ladder.First(goal => ProgressionGoals.Unwrap(goal) is CraftableUpgradeGoal);
		source = craftableUpgrade;
		if (!isSuppressed(craftableUpgrade) && !craftableUpgrade.IsMet(bot))
			return craftableUpgrade.Plan(bot);
		upgradeGoals ??= ladder.Where(goal => ProgressionGoals.Unwrap(goal) is GearGoal or OwnGoal or CageGoal).ToList();
		for (int pass = 0; pass * UpgradeChecksPerStep < upgradeGoals.Count; pass++)
		{
			if (ReadyUpgrade(bot, isSuppressed) is (ProgressionGoal goal, PlanStep step))
			{
				source = goal;
				return step;
			}
		}
		source = null;
		return null;
	}

	static PlanStep IsHandsOn(PlanStep step) => step is CraftStep or WithdrawStep or PlaceStationStep ? step : null;

	static PlanStep ContinueExpedition(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed)
	{
		if (!expeditions.TryGetValue(bot.Id, out var expedition))
			return null;
		var goal = expedition.goal;
		if (Main.GameUpdateCount > expedition.until || isSuppressed(goal) || !goal.AppliesTo(bot) || goal.IsMet(bot))
		{
			expeditions.Remove(bot.Id);
			return null;
		}
		var step = goal.Plan(bot);
		if (step is BlockedStep or IdleStep)
		{
			expeditions.Remove(bot.Id);
			return null;
		}
		return step;
	}

	public static PlanStep Obtain(Bot bot, int itemType, int amount)
	{
		var context = Context.Rent(bot);
		return context.Release(Resolve(context, itemType, amount, 0) ?? PlanStep.Idle);
	}

	public static PlanStep ObtainAny(Bot bot, IEnumerable<int> options)
	{
		var context = Context.Rent(bot);
		return context.Release(ObtainAny(context, options, 0));
	}

	public static PlanStep ObtainAnyAmount(Bot bot, IReadOnlyList<int> strongestFirst, int amount, bool fresh = false)
	{
		PlanStep cheapest = null;
		var context = Context.Rent(bot);
		context.Fresh = fresh;
		foreach (int option in strongestFirst)
		{
			var step = Resolve(context, option, amount, 0);
			if (step == null)
				return context.Release(PlanStep.Idle);
			if (step is CraftStep or WithdrawStep or BuyStep)
				return context.Release(step);
			if (step is not BlockedStep && step is not GatherStep { Source: NpcSource { IsRare: true } })
				cheapest = step;
		}
		return context.Release(cheapest ?? PlanStep.Blocked);
	}

	public static PlanStep PlanStation(Bot bot, IEnumerable<int> stationItems)
	{
		var context = Context.Rent(bot);
		return context.Release(PlanStation(context, stationItems, 0));
	}

	static PlanStep ObtainAny(Context context, IEnumerable<int> options, int depth, bool allowGrind = true)
	{
		PlanStep grind = null;
		var ranked = RankByReadiness(context, options);
		try
		{
			foreach (var (option, _) in ranked)
			{
				var step = Resolve(context, option, 1, depth);
				if (step == null)
					return PlanStep.Idle;
				if (step is GatherStep { Source: NpcSource { IsRare: true } })
					grind ??= step;
				else if (step is not BlockedStep)
					return step;
			}
		}
		finally
		{
			ranked.Clear();
			rankPool.Push(ranked);
		}
		return allowGrind && grind != null ? grind : PlanStep.Blocked;
	}

	static readonly Stack<List<(int type, int readiness)>> rankPool = new();

	static List<(int type, int readiness)> RankByReadiness(Context context, IEnumerable<int> options)
	{
		var ranked = rankPool.Count > 0 ? rankPool.Pop() : new List<(int, int)>();
		foreach (int option in options)
			ranked.Add((option, context.Readiness(option)));
		if (ranked.Count > 1)
			StableSort(ranked);
		return ranked;
	}

	static void StableSort(List<(int type, int readiness)> ranked)
	{
		for (int index = 1; index < ranked.Count; index++)
		{
			var item = ranked[index];
			int at = index - 1;
			while (at >= 0 && ranked[at].readiness < item.readiness)
			{
				ranked[at + 1] = ranked[at];
				at--;
			}
			ranked[at + 1] = item;
		}
	}

	static PlanStep PlanStation(Context context, IEnumerable<int> stationItems, int depth)
	{
		foreach (int item in stationItems)
		{
			if (context.Count(item) > 0)
				return new PlaceStationStep(item);
		}
		return ObtainAny(context, stationItems, depth, allowGrind: false);
	}

	static int resolveCalls;

	static PlanStep Resolve(Context context, int itemType, int amount, int depth)
	{
		resolveCalls++;
		if (context.Resolves > ResolveBudget)
			return PlanStep.Blocked;
		var avatar = context.Bot.Avatar;
		int have = context.Count(itemType) + Equipment.WornCount(avatar, itemType);
		if (have >= amount)
			return null;
		int stored = context.Fresh && depth == 0 ? 0 : BaseStorage.Count(context.Bot.Faction, itemType);
		bool awayFromBase = !Shops.WithinShoppingTrip(context.Bot, context.Bot.Faction.BaseCenter);
		if (stored > 0 && !awayFromBase)
			return new WithdrawStep(itemType, Math.Min(stored, amount - have));
		var local = ResolveWithoutStorage(context, itemType, amount, have, depth);
		return stored > 0 && local is BlockedStep && !context.Bot.CommittedToMining ? new WithdrawStep(itemType, Math.Min(stored, amount - have)) : local;
	}

	const int ResolveBudget = 600;

	static PlanStep ResolveWithoutStorage(Context context, int itemType, int amount, int have, int depth)
	{
		if (++context.Resolves > ResolveBudget || context.Blocked.Contains(itemType))
			return PlanStep.Blocked;
		if (depth > MaxDepth || !context.Visiting.Add(itemType))
			return PlanStep.Blocked;
		PlanStep result = null;
		try
		{
			return result = ResolveFresh(context, itemType, amount, have, depth);
		}
		finally
		{
			context.Visiting.Remove(itemType);
			if (result is BlockedStep && context.Resolves <= ResolveBudget)
				context.Blocked.Add(itemType);
		}
	}

	static PlanStep ResolveFresh(Context context, int itemType, int amount, int have, int depth)
	{
		if (CanAffordEasily(context.Bot, itemType, amount - have))
			return PlanPurchase(context.Bot, itemType, amount - have);
		var source = ResourceSources.For(itemType);
		if (source is TileSource { IsTree: false } tiles && !CanMineAny(context.Bot, tiles))
			return PlanStep.Blocked;
		if (source is TileSource { IsTree: true } trees && context.Bot.CenterTile.Y > World.TileQuery.SurfaceY(context.Bot.CenterTile.X) + UndergroundMargin
			&& Systems.BotWorldSystem.KnowledgeOf(context.Bot.Faction).NearestKnown(context.Bot.CenterTile, trees.TileTypes, NoExclusions, NearbyTreeTiles) == null)
			return PlanStep.Blocked;
		if (source != null)
			return UnknownGround(context.Bot, source) is World.Landmark ground ? new ScoutStep(ground, false) : new GatherStep(itemType, amount - have, source);
		foreach (var recipe in RecipeIndex.For(itemType))
		{
			var step = PlanRecipe(context, recipe, depth);
			if (step is not BlockedStep)
				return step;
		}
		return PlanPurchase(context.Bot, itemType, amount - have);
	}

	static bool CanMineAny(Bot bot, TileSource tiles)
	{
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
		foreach (int tile in tiles.TileTypes)
		{
			if (Navigation.PickRequirement.MinimumPick(tile, 0, Main.maxTilesY) <= pick)
				return true;
		}
		return false;
	}

	static PlanStep PlanRecipe(Context context, Recipe recipe, int depth)
	{
		if (!ConditionsAchievable(context.Bot, recipe))
			return PlanStep.Blocked;
		var avatar = context.Bot.Avatar;
		bool deep = Outpost.IsDeep(context.Bot);
		foreach (int tile in recipe.requiredTile)
		{
			var stationItems = Stations.ItemsPlacing(tile);
			bool portable = Bots.Tasks.CraftTask.IsPortableStationTile(tile);
			if (portable && stationItems.Any(item => context.Count(item) > 0))
				continue;
			if (deep && !StationNearby(context.Bot, tile))
				return portable && stationItems.Count > 0 ? PlanStation(context, stationItems, depth + 1) : PlanStep.Blocked;
			if (Stations.IsReachableStation(context.Bot.Faction, tile, context.Bot.CenterTile))
				continue;
			return stationItems.Count == 0 ? PlanStep.Blocked : PlanStation(context, stationItems, depth + 1);
		}
		foreach (var required in recipe.requiredItem)
		{
			var accepted = RecipeIndex.AcceptedTypes(recipe, required);
			int have = context.CountAny(accepted);
			foreach (int type in accepted)
				have += Equipment.WornCount(avatar, type);
			if (have >= required.stack)
				continue;
			return ResolveIngredient(context, accepted, required.stack, have, depth);
		}
		return new CraftStep(recipe);
	}

	const float DeepStationReach = 16f * 60f;

	static bool StationNearby(Bot bot, int tile) =>
		Stations.LocateAnywhere(bot.Faction, tile, bot.CenterTile) is Microsoft.Xna.Framework.Point station
		&& Microsoft.Xna.Framework.Vector2.Distance(bot.Center, station.ToWorldCoordinates()) <= DeepStationReach;

	static PlanStep ResolveIngredient(Context context, IEnumerable<int> accepted, int needed, int have, int depth)
	{
		var avatar = context.Bot.Avatar;
		var ranked = rankPool.Count > 0 ? rankPool.Pop() : new List<(int, int)>();
		try
		{
			foreach (int type in accepted)
				ranked.Add((type, context.Count(type)));
			StableSort(ranked);
			foreach (var (candidate, held) in ranked)
			{
				if (context.Visiting.Contains(candidate))
					continue;
				var step = Resolve(context, candidate, needed - have + held, depth + 1);
				if (step != null && step is not BlockedStep)
					return step;
			}
			return PlanStep.Blocked;
		}
		finally
		{
			ranked.Clear();
			rankPool.Push(ranked);
		}
	}

	const int EasyPurchaseShare = 4;
	const int UndergroundMargin = 20;
	const int NearbyTreeTiles = 40;
	static readonly HashSet<Point> NoExclusions = new();

	public static bool CanAffordEasily(Bot bot, int itemType, int amount)
	{
		if (amount <= 0 || !Shops.IsSold(itemType))
			return false;
		long coins = Utils.CoinsCount(out _, bot.Avatar.inventory);
		return coins > 0 && Shops.Offer(bot, itemType) is var (seller, price) && Shops.WithinShoppingTrip(bot, seller.Center) && (long)price * amount * EasyPurchaseShare <= coins;
	}

	static PlanStep PlanPurchase(Bot bot, int itemType, int amount)
	{
		if (Shops.Offer(bot, itemType) is not var (seller, price) || !Shops.WithinShoppingTrip(bot, seller.Center))
			return PlanStep.Blocked;
		long coins = Utils.CoinsCount(out _, bot.Avatar.inventory);
		return coins >= (long)price * amount ? new BuyStep(itemType, amount, seller.type) : PlanStep.Blocked;
	}

	static World.Landmark? UnknownGround(Bot bot, ResourceSource source)
	{
		var ground = source switch { TileSource tiles => tiles.Ground, NpcSource npcs => npcs.Ground, _ => null };
		return ground is World.Landmark landmark && BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(landmark) == null ? landmark : null;
	}

	static bool ConditionsAchievable(Bot bot, Recipe recipe)
	{
		foreach (var condition in recipe.Conditions)
		{
			if (condition != Condition.NearWater || World.WaterFinder.NearestShore(bot.Faction.BaseTile) == null)
				return false;
		}
		return true;
	}

	public static bool NeedsWater(Recipe recipe) => recipe.Conditions.Contains(Condition.NearWater);

	static int Readiness(Context context, int itemType)
	{
		var bot = context.Bot;
		var recipe = RecipeIndex.For(itemType).FirstOrDefault();
		if (recipe == null)
			return context.Count(itemType);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		int score = 0;
		foreach (var required in recipe.requiredItem)
		{
			score += 4 * Math.Min(required.stack, context.CountAny(RecipeIndex.AcceptedTypes(recipe, required)));
			var subRecipe = RecipeIndex.For(required.type).FirstOrDefault();
			if (subRecipe == null)
				continue;
			foreach (var raw in subRecipe.requiredItem)
			{
				score += Math.Min(raw.stack * required.stack, context.Count(raw.type));
				if (ResourceSources.For(raw.type) is TileSource tiles && knowledge.KnowsAny(tiles.TileTypes))
					score += 10;
			}
		}
		return score;
	}

	const uint BlockedMemoTicks = 600;
	static readonly Dictionary<int, (uint at, HashSet<int> items)> blockedByBot = new();

	static HashSet<int> BlockedFor(Bot bot)
	{
		if (blockedByBot.TryGetValue(bot.Id, out var memo) && Main.GameUpdateCount - memo.at < BlockedMemoTicks)
			return memo.items;
		var items = memo.items ?? new HashSet<int>();
		items.Clear();
		blockedByBot[bot.Id] = (Main.GameUpdateCount, items);
		return items;
	}

	sealed class Context
	{
		static Context idle;

		public Bot Bot { get; private set; }
		public bool Fresh { get; set; }
		public HashSet<int> Visiting { get; } = new();
		public HashSet<int> Blocked { get; private set; }
		public int Resolves { get; set; }
		readonly Dictionary<int, int> held = new();
		readonly Dictionary<int, int> readiness = new();

		public static Context Rent(Bot bot)
		{
			var context = idle ?? new Context();
			idle = null;
			context.Bot = bot;
			context.Fresh = false;
			context.Visiting.Clear();
			context.Blocked = BlockedFor(bot);
			context.Resolves = 0;
			context.readiness.Clear();
			context.counted = false;
			return context;
		}

		bool counted;

		public int Count(int itemType)
		{
			if (!counted)
			{
				counted = true;
				held.Clear();
				foreach (var item in Inventory.Items(Bot.Avatar))
					held[item.type] = held.GetValueOrDefault(item.type) + item.stack;
			}
			return held.GetValueOrDefault(itemType);
		}

		public int CountAny(IReadOnlyList<int> itemTypes)
		{
			int total = 0;
			for (int index = 0; index < itemTypes.Count; index++)
				total += Count(itemTypes[index]);
			return total;
		}

		public int Readiness(int itemType)
		{
			if (!readiness.TryGetValue(itemType, out int score))
				readiness[itemType] = score = ProgressionPlanner.Readiness(this, itemType);
			return score;
		}

		public PlanStep Release(PlanStep step)
		{
			Bot = null;
			idle = this;
			return step;
		}
	}
}
