using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class Junk
{
	const int InventoryPressureSlots = 46;
	const int OverflowSlots = 49;
	const int OverflowNaturalBlockReserve = 20;
	const int OverflowCheapItemValue = 100;
	const int DefaultNaturalBlockReserve = 50;

	static readonly Dictionary<int, int> Reserves = new()
	{
		[ItemID.DirtBlock] = 60,
		[ItemID.StoneBlock] = 150,
		[ItemID.SandBlock] = 100,
		[ItemID.ClayBlock] = 100,
		[ItemID.Acorn] = 20
	};

	public static bool IsUnwanted(Bot bot, Item item)
	{
		if (bot.Brain.PlannedItemTypes.Contains(item.type) || IsBossSummon(item) || IsPlatform(item))
			return false;
		int reserve = IsOverflowing(bot) ? OverflowReserveFor(item) : ReserveFor(item);
		return Inventory.Count(bot.Avatar, item.type) >= reserve;
	}

	static bool IsOverflowing(Bot bot) =>
		Inventory.UsedSlots(bot.Avatar) >= OverflowSlots && !Building.BaseStorage.HasRoom(bot.Faction);

	static int OverflowReserveFor(Item item) =>
		IsNaturalBlock(item) ? OverflowNaturalBlockReserve
		: IsGrassSeed(item) || item.type == ItemID.Acorn || IsCheapClutter(item) ? 0
		: ReserveFor(item);

	public static void TrashExcessIfCrowded(Bot bot)
	{
		int used = Inventory.UsedSlots(bot.Avatar);
		if (used < InventoryPressureSlots)
			return;
		TrashExcess(bot);
		if (IsOverflowing(bot))
			TrashOverflow(bot);
	}

	static void TrashOverflow(Bot bot)
	{
		var planned = bot.Brain.PlannedItemTypes;
		var expendable = Kit.ItemsToStore(bot).ToHashSet();
		foreach (var group in Inventory.Items(bot.Avatar).GroupBy(item => item.type).ToList())
		{
			var sample = group.First();
			if (planned.Contains(group.Key) || !group.All(expendable.Contains))
				continue;
			int reserve = OverflowReserveFor(sample);
			int excess = group.Sum(item => item.stack) - reserve;
			if (excess <= 0)
				continue;
			Inventory.Consume(bot.Avatar, new[] { group.Key }, excess);
			BotDiagnostics.Log(bot, $"inventory full, trashed {sample.Name} x{excess}");
		}
	}

	public static bool IsPlatform(Item item) => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile];

	static readonly HashSet<int> RitualItems = new()
	{
		ItemID.EmpressButterfly, ItemID.TruffleWorm, ItemID.LihzahrdPowerCell, ItemID.TempleKey, ItemID.GuideVoodooDoll, ItemID.ClothierVoodooDoll
	};

	public static bool IsBossSummon(Item item) => ItemID.Sets.SortingPriorityBossSpawns[item.type] >= 0 || RitualItems.Contains(item.type);

	static bool IsCheapClutter(Item item) =>
		!IsBossSummon(item) && !item.material && item.damage <= 0 && item.createTile < 0 && item.createWall <= 0 && !item.accessory
		&& item.headSlot < 0 && item.bodySlot < 0 && item.legSlot < 0 && !item.potion && item.value < OverflowCheapItemValue;

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
		if (IsBossSummon(item) || IsPlatform(item))
			return int.MaxValue;
		if (item.createTile == TileID.Tombstones)
			return 0;
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
