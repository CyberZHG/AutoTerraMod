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
	static Dictionary<int, List<Recipe>> allRecipesByResult = new();

	public override void PostSetupRecipes()
	{
		var allRecipes = Main.recipe
			.Take(Recipe.numRecipes)
			.Where(recipe => !recipe.Disabled && recipe.createItem.type > ItemID.None)
			.ToList();
		var ingredientsByResult = allRecipes
			.GroupBy(recipe => recipe.createItem.type)
			.ToDictionary(group => group.Key, group => group.SelectMany(recipe => recipe.requiredItem.Select(item => item.type)).ToHashSet());
		allRecipesByResult = allRecipes.GroupBy(recipe => recipe.createItem.type).ToDictionary(group => group.Key, group => group.ToList());
		recipesByResult = allRecipes
			.GroupBy(recipe => recipe.createItem.type)
			.Select(group => (group.Key, recipes: ForwardRecipes(group.ToList(), ingredientsByResult)))
			.Where(entry => entry.recipes.Count > 0)
			.ToDictionary(entry => entry.Key, entry => entry.recipes);
	}

	static List<Recipe> ForwardRecipes(List<Recipe> recipes, Dictionary<int, HashSet<int>> ingredientsByResult)
	{
		bool IsReverse(Recipe recipe) => recipe.createItem.stack <= 1 && recipe.requiredItem.Any(required =>
			ingredientsByResult.TryGetValue(required.type, out var ingredients) && ingredients.Contains(recipe.createItem.type));
		return recipes.Where(recipe => !IsReverse(recipe)).ToList();
	}

	public override void Unload()
	{
		recipesByResult = new();
		allRecipesByResult = new();
		acceptedTypes.Clear();
	}

	static readonly Dictionary<(Recipe, int), int[]> acceptedTypes = new();

	public static IReadOnlyList<Recipe> For(int itemType) =>
		recipesByResult.TryGetValue(itemType, out var recipes) ? recipes : Array.Empty<Recipe>();

	public static bool IsCraftable(int itemType) => recipesByResult.ContainsKey(itemType);

	public static IReadOnlyList<Recipe> AnyFor(int itemType) =>
		allRecipesByResult.TryGetValue(itemType, out var recipes) ? recipes : Array.Empty<Recipe>();

	public static IReadOnlyList<int> AcceptedTypes(Recipe recipe, Item required)
	{
		if (!acceptedTypes.TryGetValue((recipe, required.type), out var types))
			acceptedTypes[(recipe, required.type)] = types = FindAcceptedTypes(recipe, required.type);
		return types;
	}

	static int[] FindAcceptedTypes(Recipe recipe, int requiredType)
	{
		foreach (int groupId in recipe.acceptedGroups)
		{
			if (!RecipeGroup.recipeGroups.TryGetValue(groupId, out var group) || !group.ContainsItem(requiredType))
				continue;
			return group.ValidItems.OrderBy(type => type == requiredType ? 0 : 1).ThenBy(type => type).ToArray();
		}
		return new[] { requiredType };
	}
}
