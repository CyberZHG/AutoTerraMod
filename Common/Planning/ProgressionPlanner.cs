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

	public static void Reset() => ladder = null;

	public static PlanStep NextStep(Bot bot, System.Func<ProgressionGoal, bool> isSuppressed, out ProgressionGoal source)
	{
		ladder ??= ProgressionGoals.Build();
		foreach (var goal in ladder)
		{
			if (isSuppressed(goal) || !goal.AppliesTo(bot) || goal.IsMet(bot))
				continue;
			var step = goal.Plan(bot);
			if (step is not BlockedStep and not IdleStep)
			{
				source = goal;
				return step;
			}
		}
		source = null;
		return PlanStep.Idle;
	}

	public static PlanStep Obtain(Bot bot, int itemType, int amount) =>
		Resolve(new Context(bot), itemType, amount, 0) ?? PlanStep.Idle;

	public static PlanStep ObtainAny(Bot bot, IEnumerable<int> options) => ObtainAny(new Context(bot), options, 0);

	public static PlanStep PlanStation(Bot bot, IEnumerable<int> stationItems) => PlanStation(new Context(bot), stationItems, 0);

	static PlanStep ObtainAny(Context context, IEnumerable<int> options, int depth)
	{
		foreach (int option in options.OrderByDescending(option => Readiness(context.Bot, option)))
		{
			var step = Resolve(context, option, 1, depth);
			if (step == null)
				return PlanStep.Idle;
			if (step is not BlockedStep)
				return step;
		}
		return PlanStep.Blocked;
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
		int have = Inventory.Count(avatar, itemType);
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
			if (source != null)
				return new GatherStep(itemType, amount - have, source);
			foreach (var recipe in RecipeIndex.For(itemType))
			{
				var step = PlanRecipe(context, recipe, depth);
				if (step is not BlockedStep)
					return step;
			}
			return PlanStep.Blocked;
		}
		finally
		{
			context.Visiting.Remove(itemType);
		}
	}

	static PlanStep PlanRecipe(Context context, Recipe recipe, int depth)
	{
		if (recipe.Conditions.Count > 0)
			return PlanStep.Blocked;
		var avatar = context.Bot.Avatar;
		foreach (int tile in recipe.requiredTile)
		{
			if (Stations.BaseHas(context.Bot.Faction, tile))
				continue;
			var stationItems = Stations.ItemsPlacing(tile);
			return stationItems.Count == 0 ? PlanStep.Blocked : PlanStation(context, stationItems, depth + 1);
		}
		foreach (var required in recipe.requiredItem)
		{
			var accepted = RecipeIndex.AcceptedTypes(recipe, required);
			int have = Inventory.CountAny(avatar, accepted);
			if (have >= required.stack)
				continue;
			foreach (int candidate in accepted.OrderByDescending(type => Inventory.Count(avatar, type)))
			{
				int target = required.stack - have + Inventory.Count(avatar, candidate);
				var step = Resolve(context, candidate, target, depth + 1);
				if (step != null && step is not BlockedStep)
					return step;
			}
			return PlanStep.Blocked;
		}
		return new CraftStep(recipe);
	}

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
