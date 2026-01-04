using System.Linq;
using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.DataStructures;

namespace AutoTerraMod.Common.Planning;

public static class Crafting
{
	public static bool HasIngredients(Player avatar, Recipe recipe) =>
		recipe.requiredItem.All(required => Inventory.CountAny(avatar, RecipeIndex.AcceptedTypes(recipe, required)) >= required.stack);

	public static bool HasStations(Player avatar, Recipe recipe) => recipe.requiredTile.All(tile => avatar.adjTile[tile]);

	public static bool TryCraft(Player avatar, Recipe recipe)
	{
		if (!HasIngredients(avatar, recipe) || !HasStations(avatar, recipe))
			return false;
		foreach (var required in recipe.requiredItem)
			Inventory.Consume(avatar, RecipeIndex.AcceptedTypes(recipe, required), required.stack);
		var crafted = recipe.createItem.Clone();
		crafted.Prefix(-1);
		var leftover = Inventory.Add(avatar, crafted);
		if (!leftover.IsAir)
			Item.NewItem(new EntitySource_Misc("AutoTerraMod:CraftOverflow"), avatar.Center, leftover);
		return true;
	}
}
