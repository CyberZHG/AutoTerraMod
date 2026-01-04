using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class Junk
{
	const int InventoryPressureSlots = 46;
	const int DefaultNaturalBlockReserve = 50;

	static readonly Dictionary<int, int> Reserves = new()
	{
		[ItemID.DirtBlock] = 60,
		[ItemID.StoneBlock] = 150,
		[ItemID.SandBlock] = 100,
		[ItemID.ClayBlock] = 100,
		[ItemID.Acorn] = 10
	};

	public static bool IsUnwanted(Bot bot, Item item) =>
		!bot.Brain.PlannedItemTypes.Contains(item.type)
		&& Inventory.Count(bot.Avatar, item.type) >= ReserveFor(item);

	public static void TrashExcessIfCrowded(Bot bot)
	{
		if (Inventory.UsedSlots(bot.Avatar) < InventoryPressureSlots)
			return;
		TrashExcess(bot);
	}

	public static void TrashExcess(Bot bot)
	{
		var planned = bot.Brain.PlannedItemTypes;
		var avatar = bot.Avatar;
		foreach (var group in Inventory.Items(avatar).GroupBy(item => item.type).ToList())
		{
			if (planned.Contains(group.Key))
				continue;
			int reserve = ReserveFor(group.First());
			int excess = group.Sum(item => item.stack) - reserve;
			if (excess <= 0)
				continue;
			Inventory.Consume(avatar, new[] { group.Key }, excess);
			BotDiagnostics.Log(bot, $"trashed {group.First().Name} x{excess}");
		}
	}

	static int ReserveFor(Item item)
	{
		if (Reserves.TryGetValue(item.type, out int reserve))
			return reserve;
		if (IsGrassSeed(item))
			return 0;
		if (IsNaturalBlock(item))
			return DefaultNaturalBlockReserve;
		return int.MaxValue;
	}

	static bool IsGrassSeed(Item item) => item.createTile >= 0 && TileID.Sets.Grass[item.createTile];

	static bool IsNaturalBlock(Item item) =>
		item.createTile >= 0
		&& !TileID.Sets.Ore[item.createTile]
		&& ResourceSources.For(item.type) is TileSource { IsTree: false };
}
