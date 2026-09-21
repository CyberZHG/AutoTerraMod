using System.Linq;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class WaterBottler
{
	const int MaxFills = 20;

	public static void FillWhileAtWater(Bot bot)
	{
		var avatar = bot.Avatar;
		if (Inventory.Count(avatar, ItemID.Bottle) == 0)
			return;
		if (bot.Brain.PlannedItemTypes.Contains(ItemID.BottledHoney))
		{
			if (World.WaterFinder.IsNearHoney(bot.CenterTile) && RecipeIndex.For(ItemID.BottledHoney).FirstOrDefault(ProgressionPlanner.NeedsHoney) is { } honey && Crafting.TryCraft(avatar, honey))
				BotDiagnostics.Log(bot, $"filled Bottled Honey at {bot.CenterTile}");
			return;
		}
		if (!World.WaterFinder.IsNearWater(bot.CenterTile))
			return;
		var recipe = RecipeIndex.For(ItemID.BottledWater).FirstOrDefault(ProgressionPlanner.NeedsWater);
		if (recipe == null)
			return;
		int filled = 0;
		while (filled < MaxFills && Crafting.TryCraft(avatar, recipe))
			filled++;
		if (filled > 0)
			BotDiagnostics.Log(bot, $"filled {filled} Bottled Water at {bot.CenterTile}");
	}
}
