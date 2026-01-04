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
		recipesByResult = Main.recipe
			.Take(Recipe.numRecipes)
			.Where(recipe => !recipe.Disabled && recipe.createItem.type > ItemID.None)
			.GroupBy(recipe => recipe.createItem.type)
			.ToDictionary(group => group.Key, group => group.ToList());
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
