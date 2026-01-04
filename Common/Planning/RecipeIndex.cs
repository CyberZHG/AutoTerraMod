using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Planning;

public sealed class RecipeIndex : ModSystem
{
	static Dictionary<int, List<Recipe>> recipesByResult = new();

	public override void PostSetupRecipes()
	{
		var allRecipes = Main.recipe
			.Take(Recipe.numRecipes)
			.Where(recipe => !recipe.Disabled && recipe.createItem.type > ItemID.None)
			.ToList();
		var ingredientsByResult = allRecipes
			.GroupBy(recipe => recipe.createItem.type)
			.ToDictionary(group => group.Key, group => group.SelectMany(recipe => recipe.requiredItem.Select(item => item.type)).ToHashSet());
		recipesByResult = allRecipes
			.GroupBy(recipe => recipe.createItem.type)
			.ToDictionary(group => group.Key, group => PreferForwardRecipes(group.ToList(), ingredientsByResult));
	}

	static List<Recipe> PreferForwardRecipes(List<Recipe> recipes, Dictionary<int, HashSet<int>> ingredientsByResult)
	{
		bool IsReverse(Recipe recipe) => recipe.requiredItem.Any(required =>
			ingredientsByResult.TryGetValue(required.type, out var ingredients) && ingredients.Contains(recipe.createItem.type));
		var forward = recipes.Where(recipe => !IsReverse(recipe)).ToList();
		return forward.Count > 0 ? forward : recipes;
	}

	public override void Unload() => recipesByResult = new();

	public static IReadOnlyList<Recipe> For(int itemType) =>
		recipesByResult.TryGetValue(itemType, out var recipes) ? recipes : Array.Empty<Recipe>();

	public static bool IsCraftable(int itemType) => recipesByResult.ContainsKey(itemType);

	public static IReadOnlyList<int> AcceptedTypes(Recipe recipe, Item required)
	{
		foreach (int groupId in recipe.acceptedGroups)
		{
			if (!RecipeGroup.recipeGroups.TryGetValue(groupId, out var group) || !group.ContainsItem(required.type))
				continue;
			return group.ValidItems.OrderBy(type => type == required.type ? 0 : 1).ThenBy(type => type).ToList();
		}
		return new[] { required.type };
	}
}
