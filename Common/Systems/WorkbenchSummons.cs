using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class WorkbenchSummons : ModSystem
{
	public static readonly Condition Enabled = new(Language.GetText("Mods.AutoTerraMod.Conditions.WorkbenchSummons"), () => ModContent.GetInstance<Config.AutoTerraConfig>().WorkbenchBossSummons);

	public override void AddRecipes()
	{
		var altarSummons = Main.recipe.Take(Recipe.numRecipes)
			.Where(recipe => recipe.requiredTile.Contains(TileID.DemonAltar) && ItemID.Sets.SortingPriorityBossSpawns[recipe.createItem.type] >= 0)
			.ToList();
		foreach (var original in altarSummons)
		{
			var copy = Recipe.Create(original.createItem.type, original.createItem.stack);
			foreach (var item in original.requiredItem)
				copy.AddIngredient(item.type, item.stack);
			copy.acceptedGroups.AddRange(original.acceptedGroups);
			foreach (int tile in original.requiredTile.Where(tile => tile >= 0 && tile != TileID.DemonAltar))
				copy.AddTile(tile);
			copy.AddTile(TileID.WorkBenches);
			foreach (var condition in original.Conditions)
				copy.AddCondition(condition);
			copy.AddCondition(Enabled);
			copy.Register();
		}
	}
}
