using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public readonly record struct BillLine(BuildAction Placement, int ItemType, int Amount);

public static class ConstructionBill
{
	const int ScaffoldPlatforms = 20;

	static readonly BuildAction[] GatherOrder =
	{
		BuildAction.PlaceWall,
		BuildAction.PlaceDoor,
		BuildAction.PlaceTorch,
		BuildAction.PlaceTable,
		BuildAction.PlaceChair,
		BuildAction.PlaceBed,
		BuildAction.PlaceBlock
	};

	public static List<BillLine> For(HouseBlueprint structure, HouseDesign materials)
	{
		var operations = structure.PendingOperations(materials).ToList();
		var counts = operations
			.Select(operation => HouseMaterials.ReplacementFor(operation.Action))
			.Where(HouseMaterials.IsNeeded)
			.GroupBy(placement => placement)
			.ToDictionary(group => group.Key, group => group.Count());
		var bill = GatherOrder
			.Where(counts.ContainsKey)
			.Select(placement => new BillLine(placement, HouseMaterials.DefaultItem(placement, materials), counts[placement]))
			.ToList();
		if (operations.Any(operation => HouseMaterials.IsNeeded(operation.Action) && structure.IsOutOfReachFromFloor(operation.Tile)))
			bill.Insert(0, new BillLine(BuildAction.RemoveScaffold, ItemID.WoodPlatform, ScaffoldPlatforms));
		return bill;
	}

	public static BillLine? FirstShortfall(Bot bot, List<BillLine> bill, HouseDesign materials)
	{
		foreach (var line in bill)
		{
			int onHand = line.Placement == BuildAction.RemoveScaffold
				? Inventory.Count(bot.Avatar, line.ItemType)
				: HouseMaterials.CountAvailable(bot.Avatar, line.Placement, materials);
			int stored = BaseStorage.Count(bot.Faction, line.ItemType);
			if (onHand + stored < line.Amount)
				return line with { Amount = line.Amount - stored };
		}
		return null;
	}
}
