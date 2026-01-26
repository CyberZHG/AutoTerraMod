using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Systems;
using Terraria;

namespace AutoTerraMod.Common.Planning;

public static class ProgressionPlanner
{
	const int MaxDepth = 10;

	static ProgressionGoal[] ladder;

	public static void Reset()
	{
		ladder = null;
		expeditions.Clear();
	}

	const int ExpeditionCommitmentTicks = 3 * 3600;

	static readonly Dictionary<int, (ProgressionGoal goal, uint until)> expeditions = new();

	public static PlanStep NextStep(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, out ProgressionGoal source)
	{
		ladder ??= ProgressionGoals.Build();
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
		foreach (var goal in ladder)
		{
			if (isSuppressed(goal) || !goal.AppliesTo(bot) || goal.IsMet(bot))
				continue;
			var step = goal.Plan(bot);
			if (step is not BlockedStep and not IdleStep)
			{
				if (goal.IsExpedition)
					expeditions[bot.Id] = (goal, Main.GameUpdateCount + ExpeditionCommitmentTicks);
				source = goal;
				return step;
			}
		}
		source = null;
		return PlanStep.Idle;
	}

	const int UpgradeScanTicks = 600;

	static readonly Dictionary<int, (uint at, ProgressionGoal goal)> readyUpgrades = new();

	static (ProgressionGoal, PlanStep)? ReadyUpgrade(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed)
	{
		if (readyUpgrades.TryGetValue(bot.Id, out var cached) && Main.GameUpdateCount - cached.at < UpgradeScanTicks)
		{
			if (cached.goal == null)
				return null;
			if (!isSuppressed(cached.goal) && !cached.goal.IsMet(bot) && IsHandsOn(cached.goal.Plan(bot)) is PlanStep again)
				return (cached.goal, again);
		}
		foreach (var goal in ladder)
		{
			if (ProgressionGoals.Unwrap(goal) is not (GearGoal or OwnGoal) || isSuppressed(goal) || !goal.AppliesTo(bot) || goal.IsMet(bot))
				continue;
			if (IsHandsOn(goal.Plan(bot)) is PlanStep step)
			{
				readyUpgrades[bot.Id] = (Main.GameUpdateCount, goal);
				return (goal, step);
			}
		}
		readyUpgrades[bot.Id] = (Main.GameUpdateCount, null);
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

	public static PlanStep Obtain(Bot bot, int itemType, int amount) =>
		Resolve(new Context(bot), itemType, amount, 0) ?? PlanStep.Idle;

	public static PlanStep ObtainAny(Bot bot, IEnumerable<int> options) => ObtainAny(new Context(bot), options, 0);

	public static PlanStep PlanStation(Bot bot, IEnumerable<int> stationItems) => PlanStation(new Context(bot), stationItems, 0);

	static PlanStep ObtainAny(Context context, IEnumerable<int> options, int depth)
	{
		PlanStep grind = null;
		foreach (int option in options.OrderByDescending(option => Readiness(context.Bot, option)))
		{
			var step = Resolve(context, option, 1, depth);
			if (step == null)
				return PlanStep.Idle;
			if (step is GatherStep { Source: NpcSource { IsRare: true } })
				grind ??= step;
			else if (step is not BlockedStep)
				return step;
		}
		return grind ?? PlanStep.Blocked;
	}

	static PlanStep PlanStation(Context context, IEnumerable<int> stationItems, int depth)
	{
		var items = stationItems.ToList();
		int held = items.FirstOrDefault(item => Inventory.Count(context.Bot.Avatar, item) > 0);
		return held > 0 ? new PlaceStationStep(held) : ObtainAny(context, items, depth);
	}

	static PlanStep Resolve(Context context, int itemType, int amount, int depth)
	{
		var avatar = context.Bot.Avatar;
		int have = Inventory.Count(avatar, itemType) + Equipment.WornCount(avatar, itemType);
		if (have >= amount)
			return null;
		int stored = BaseStorage.Count(context.Bot.Faction, itemType);
		if (stored > 0)
			return new WithdrawStep(itemType, Math.Min(stored, amount - have));
		if (depth > MaxDepth || !context.Visiting.Add(itemType))
			return PlanStep.Blocked;
		try
		{
			var source = ResourceSources.For(itemType);
			if (source is TileSource { IsTree: false } tiles && tiles.TileTypes.All(tile => Navigation.PickRequirement.MinimumPick(tile, 0, Main.maxTilesY) > (context.Bot.Hands.BestPickaxe?.pick ?? 0)))
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
		finally
		{
			context.Visiting.Remove(itemType);
		}
	}

	static PlanStep PlanRecipe(Context context, Recipe recipe, int depth)
	{
		if (!ConditionsAchievable(context.Bot, recipe))
			return PlanStep.Blocked;
		var avatar = context.Bot.Avatar;
		foreach (int tile in recipe.requiredTile)
		{
			if (Stations.IsReachableStation(context.Bot.Faction, tile, context.Bot.CenterTile))
				continue;
			var stationItems = Stations.ItemsPlacing(tile);
			return stationItems.Count == 0 ? PlanStep.Blocked : PlanStation(context, stationItems, depth + 1);
		}
		foreach (var required in recipe.requiredItem)
		{
			var accepted = RecipeIndex.AcceptedTypes(recipe, required);
			int have = Inventory.CountAny(avatar, accepted) + accepted.Sum(type => Equipment.WornCount(avatar, type));
			if (have >= required.stack)
				continue;
			foreach (int candidate in accepted.OrderByDescending(type => Inventory.Count(avatar, type)))
			{
				if (context.Visiting.Contains(candidate))
					continue;
				int target = required.stack - have + Inventory.Count(avatar, candidate);
				var step = Resolve(context, candidate, target, depth + 1);
				if (step != null && step is not BlockedStep)
					return step;
			}
			return PlanStep.Blocked;
		}
		return new CraftStep(recipe);
	}

	static PlanStep PlanPurchase(Bot bot, int itemType, int amount)
	{
		if (Shops.Offer(bot, itemType) is not var (seller, price))
			return PlanStep.Blocked;
		long coins = Utils.CoinsCount(out _, bot.Avatar.inventory);
		return coins >= (long)price * amount ? new BuyStep(itemType, amount, seller.type) : PlanStep.Blocked;
	}

	static World.Landmark? UnknownGround(Bot bot, ResourceSource source)
	{
		var ground = source switch { TileSource tiles => tiles.Ground, NpcSource npcs => npcs.Ground, _ => null };
		return ground is World.Landmark landmark && BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(landmark) == null ? landmark : null;
	}

	static bool ConditionsAchievable(Bot bot, Recipe recipe) => recipe.Conditions.All(condition =>
		condition == Condition.NearWater && World.WaterFinder.NearestShore(bot.Faction.BaseTile) != null);

	public static bool NeedsWater(Recipe recipe) => recipe.Conditions.Contains(Condition.NearWater);

	static int Readiness(Bot bot, int itemType)
	{
		var recipe = RecipeIndex.For(itemType).FirstOrDefault();
		if (recipe == null)
			return Inventory.Count(bot.Avatar, itemType);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		int score = 0;
		foreach (var required in recipe.requiredItem)
		{
			score += 4 * Math.Min(required.stack, Inventory.CountAny(bot.Avatar, RecipeIndex.AcceptedTypes(recipe, required)));
			var subRecipe = RecipeIndex.For(required.type).FirstOrDefault();
			if (subRecipe == null)
				continue;
			foreach (var raw in subRecipe.requiredItem)
			{
				score += Math.Min(raw.stack * required.stack, Inventory.Count(bot.Avatar, raw.type));
				if (ResourceSources.For(raw.type) is TileSource tiles && knowledge.KnowsAny(tiles.TileTypes))
					score += 10;
			}
		}
		return score;
	}

	sealed class Context
	{
		public Context(Bot bot) => Bot = bot;
		public Bot Bot { get; }
		public HashSet<int> Visiting { get; } = new();
	}
}
