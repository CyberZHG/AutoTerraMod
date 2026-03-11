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
		[ItemID.StoneBlock] = 999,
		[ItemID.GraniteBlock] = 250,
		[ItemID.MarbleBlock] = 250,
		[ItemID.SandBlock] = 100,
		[ItemID.ClayBlock] = 100,
		[ItemID.Acorn] = 20
	};

	public static bool IsUnwanted(Bot bot, Item item)
	{
		if (bot.Brain.PlannedItemTypes.Contains(item.type) || IsBossSummon(item) || IsPlatform(item) || FitsExistingStack(bot.Avatar, item) || DyeTaste.IsFavorite(bot.Faction, item))
			return false;
		int reserve = IsOverflowing(bot) ? OverflowReserveFor(item) : ReserveFor(item);
		return Inventory.Count(bot.Avatar, item.type) >= reserve;
	}

	static bool FitsExistingStack(Player avatar, Item item)
	{
		foreach (var held in Inventory.Items(avatar))
		{
			if (held.type == item.type && held.stack < held.maxStack)
				return true;
		}
		return false;
	}

	static bool IsOverflowing(Bot bot) =>
		Inventory.UsedSlots(bot.Avatar) >= OverflowSlots && !Building.BaseStorage.HasRoom(bot.Faction);

	static int OverflowReserveFor(Item item) =>
		CraftingBlocks.Contains(item.type) ? int.MaxValue
		: IsNaturalBlock(item) ? OverflowNaturalBlockReserve
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
			Discard(bot, sample, excess, "inventory full");
		}
	}

	public static bool MakeRoomFor(Bot bot, Item incoming)
	{
		var planned = bot.Brain.PlannedItemTypes;
		var cheapest = Kit.ItemsToStore(bot)
			.Where(item => item.type != incoming.type && !planned.Contains(item.type))
			.MinBy(item => (long)item.value * item.stack);
		if (cheapest == null)
			return false;
		Discard(bot, cheapest, cheapest.stack, $"making room for {incoming.Name}");
		return Inventory.HasRoomFor(bot.Avatar, incoming);
	}

	public static void SellOutgrownLoot(Bot bot)
	{
		var shopkeeper = Planning.Shops.NearbyShopkeeper(bot.Center);
		if (shopkeeper == null)
			return;
		var hammer = bot.Hands.BestHammer;
		var loot = Kit.ItemsToStore(bot).Where(item => item != hammer && IsSellableLoot(item) && (item.damage <= 0 || Outclassed(bot, item)) && !UpgradesTeammate(bot, item)).ToList();
		if (loot.Count == 0)
			return;
		long earned = 0;
		foreach (var item in loot)
		{
			earned += Planning.Shops.SellPrice(item, item.stack);
			item.TurnToAir();
		}
		Inventory.AddCoins(bot.Avatar, earned);
		BotDiagnostics.Log(bot, $"sold {loot.Count} pieces of outgrown loot to the {shopkeeper.TypeName} for {earned} copper");
	}

	static readonly Planning.GearKind[] ShareableKinds =
	{
		Planning.GearKind.Head, Planning.GearKind.Body, Planning.GearKind.Legs, Planning.GearKind.Pickaxe,
		Planning.GearKind.MeleeWeapon, Planning.GearKind.RangedWeapon, Planning.GearKind.MagicWeapon, Planning.GearKind.SummonWeapon
	};

	static bool UpgradesTeammate(Bot bot, Item item)
	{
		foreach (var kind in ShareableKinds)
		{
			int power = Planning.Equipment.PowerOf(item, kind);
			if (power <= 0)
				continue;
			foreach (var mate in Systems.BotWorldSystem.Bots)
			{
				if (mate.FactionId == bot.FactionId && mate != bot && Planning.Equipment.BestPower(mate.Avatar, kind) < power)
					return true;
			}
		}
		return item.accessory && Building.BaseStorage.Count(bot.Faction, item.type) == 0;
	}

	static bool Outclassed(Bot bot, Item weapon) =>
		Inventory.Items(bot.Avatar).Any(other => other != weapon && other.damage > weapon.damage && other.DamageType == weapon.DamageType && other.useAmmo == weapon.useAmmo);

	static bool IsSellableLoot(Item item) =>
		item.value > 0 && !item.material && item.createTile < 0 && item.createWall <= 0 && !item.consumable && item.ammo == 0
		&& !item.questItem && item.fishingPole <= 0 && item.bait <= 0 && !IsBossSummon(item) && !item.IsACoin;

	static void Discard(Bot bot, Item sample, int amount, string reason)
	{
		var name = sample.Name;
		long price = Planning.Shops.SellPrice(sample, amount);
		var shopkeeper = price > 0 ? Planning.Shops.NearbyShopkeeper(bot.Center) : null;
		Inventory.Consume(bot.Avatar, new[] { sample.type }, amount);
		if (shopkeeper == null)
		{
			BotDiagnostics.Log(bot, $"trashed {name} x{amount} ({reason})");
			return;
		}
		Inventory.AddCoins(bot.Avatar, price);
		BotDiagnostics.Log(bot, $"sold {name} x{amount} to the {shopkeeper.TypeName} for {price} copper ({reason})");
	}

	public static bool IsPlatform(Item item) => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile];

	static readonly HashSet<int> RitualItems = new()
	{
		ItemID.EmpressButterfly, ItemID.TruffleWorm, ItemID.LihzahrdPowerCell, ItemID.TempleKey, ItemID.GuideVoodooDoll, ItemID.ClothierVoodooDoll
	};

	public static bool IsBossSummon(Item item) => ItemID.Sets.SortingPriorityBossSpawns[item.type] >= 0 || RitualItems.Contains(item.type) || item.questItem;

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
			Discard(bot, group.First(), excess, "excess");
		}
	}

	static readonly HashSet<int> CraftingBlocks = new() { ItemID.Obsidian };

	static int ReserveFor(Item item)
	{
		if (IsBossSummon(item) || IsPlatform(item) || CraftingBlocks.Contains(item.type))
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
