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
	const int OverflowCheapItemValue = 100;
	public const int Plenty = 9999;

	static readonly Dictionary<int, int> Reserves = new()
	{
		[ItemID.Acorn] = 20
	};

	public static bool IsUnwanted(Bot bot, Item item)
	{
		if (Inventory.Count(bot.Avatar, item.type) >= Plenty)
			return true;
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
		: IsNaturalBlock(item) ? Plenty
		: IsGrassSeed(item) || item.type == ItemID.Acorn || IsCheapClutter(item) ? 0
		: ReserveFor(item);

	public static void TrashExcessIfCrowded(Bot bot)
	{
		TrashBeyondPlenty(bot);
		int used = Inventory.UsedSlots(bot.Avatar);
		if (used < InventoryPressureSlots)
			return;
		TrashExcess(bot);
		if (IsOverflowing(bot))
			TrashOverflow(bot);
		if (IsOverflowing(bot))
			DropHardestToUse(bot);
	}

	const uint AbandonedTicks = 36000;
	static readonly Dictionary<int, (int type, uint at)> abandoned = new();

	public static bool IsAbandoned(int index) =>
		abandoned.TryGetValue(index, out var drop) && Main.item[index].type == drop.type && Main.GameUpdateCount - drop.at < AbandonedTicks;

	static void DropHardestToUse(Bot bot)
	{
		var victim = Kit.ItemsToStore(bot)
			.Where(item => !IsBossSummon(item) && !CraftingBlocks.Contains(item.type) && !BotHands.IsBattlePotion(item.type) && !(item.potion && item.healLife > 0) && item.fishingPole <= 0 && !bot.Brain.PlannedItemTypes.Contains(item.type) && !DyeTaste.IsFavorite(bot.Faction, item))
			.OrderBy(Usefulness)
			.ThenBy(item => (long)item.value * item.stack)
			.FirstOrDefault();
		if (victim == null)
			return;
		string name = victim.Name;
		int stack = victim.stack;
		int index = Item.NewItem(bot.Avatar.GetSource_DropAsItem(), bot.Center, victim.type, stack, prefixGiven: victim.prefix);
		victim.TurnToAir();
		if (abandoned.Count > 400)
			abandoned.Clear();
		abandoned[index] = (Main.item[index].type, Main.GameUpdateCount);
		BotDiagnostics.Log(bot, $"dropped {name} x{stack} on the ground (inventory and storage full)");
	}

	static int Usefulness(Item item)
	{
		if (Planning.Stations.IsCraftingStation(item.createTile))
			return 6;
		if (item.createTile >= 0 && (TileID.Sets.Ore[item.createTile] || item.createTile == TileID.MetalBars))
			return 5;
		if (item.material)
			return 4;
		if (item.potion || IsBuffPotion(item) || item.ammo > 0 || item.accessory)
			return 3;
		if (item.damage > 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0 || !item.vanity && (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0))
			return 2;
		if (item.createTile >= 0 || item.createWall > 0)
			return 1;
		return 0;
	}

	static void TrashBeyondPlenty(Bot bot)
	{
		Inventory.MergeStacks(bot.Avatar);
		foreach (var group in Inventory.Items(bot.Avatar).GroupBy(item => item.type).ToList())
		{
			int excess = group.Sum(item => item.stack) - Plenty;
			if (excess > 0)
				Discard(bot, group.First(), excess, $"already {Plenty}");
		}
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

	const int OutdatedGearValue = 5000;

	const int KeptAccessoryCopies = 2;
	const int KeptFullStacks = 2;

	static bool IsStorageClutter(Bot bot, Item item, int copiesBefore, int amountBefore)
	{
		if (item.IsACoin)
			return false;
		if (item.maxStack > 1 && amountBefore >= Plenty)
			return true;
		if (IsBossSummon(item) || bot.Brain.PlannedItemTypes.Contains(item.type))
			return false;
		if (item.maxStack > 1)
			return amountBefore >= System.Math.Min(item.maxStack * KeptFullStacks, Plenty) || IsCheapClutter(item);
		if (item.accessory)
			return copiesBefore >= KeptAccessoryCopies;
		if (item.potion)
			return false;
		if (IsCheapClutter(item) || item.createTile == TileID.Statues || item.createTile == TileID.PressurePlates)
			return true;
		bool gear = item.damage > 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0;
		return gear && (copiesBefore > 0 || item.value < OutdatedGearValue) && !UpgradesTeammate(bot, item);
	}

	static IEnumerable<Item> StorageClutter(Bot bot, IEnumerable<Chest> chests)
	{
		var copies = new Dictionary<int, int>();
		var amounts = new Dictionary<int, int>();
		foreach (var chest in chests)
		{
			foreach (var item in chest.item)
			{
				if (item == null || item.IsAir)
					continue;
				copies.TryGetValue(item.type, out int copiesBefore);
				amounts.TryGetValue(item.type, out int amountBefore);
				copies[item.type] = copiesBefore + 1;
				amounts[item.type] = amountBefore + item.stack;
				if (IsStorageClutter(bot, item, copiesBefore, amountBefore))
					yield return item;
			}
		}
	}

	static bool HasLooseStacks(IEnumerable<Chest> chests)
	{
		var partial = new HashSet<int>();
		foreach (var chest in chests)
		{
			foreach (var item in chest.item)
			{
				if (item != null && !item.IsAir && item.stack < item.maxStack && !partial.Add(item.type))
					return true;
			}
		}
		return false;
	}

	static int MergeStacks(IEnumerable<Chest> chests)
	{
		var open = new Dictionary<int, Item>();
		int freed = 0;
		foreach (var chest in chests)
		{
			foreach (var item in chest.item)
			{
				if (item == null || item.IsAir || item.maxStack <= 1)
					continue;
				if (open.TryGetValue(item.type, out var target) && target.prefix == item.prefix)
				{
					int moved = System.Math.Min(item.stack, target.maxStack - target.stack);
					target.stack += moved;
					item.stack -= moved;
					if (item.stack <= 0)
					{
						item.TurnToAir();
						freed++;
						continue;
					}
				}
				if (item.stack < item.maxStack)
					open[item.type] = item;
			}
		}
		return freed;
	}

	public static bool StorageHasClutter(Bot bot, IEnumerable<Chest> chests) => HasLooseStacks(chests) || StorageClutter(bot, chests).Any();

	public static void PurgeStorage(Bot bot, IEnumerable<Chest> chests)
	{
		var list = chests.ToList();
		int merged = MergeStacks(list);
		var shopkeeper = Planning.Shops.NearbyShopkeeper(bot.Center);
		int purged = 0;
		long earned = 0;
		foreach (var item in StorageClutter(bot, list).ToList())
		{
			if (shopkeeper != null)
				earned += Planning.Shops.SellPrice(item, item.stack);
			item.TurnToAir();
			purged++;
		}
		if (purged == 0 && merged == 0)
			return;
		if (earned > 0)
			Inventory.AddCoins(bot.Avatar, earned);
		BotDiagnostics.Log(bot, $"merged {merged} and cleared {purged} cluttering stacks from storage (earned {earned} copper)");
	}

	static bool IsBuffPotion(Item item) => item.consumable && item.buffType > 0 && item.buffTime > 0;

	public static bool IsPlatform(Item item) => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile];

	static readonly HashSet<int> RitualItems = new()
	{
		ItemID.EmpressButterfly, ItemID.TruffleWorm, ItemID.LihzahrdPowerCell, ItemID.TempleKey, ItemID.GuideVoodooDoll, ItemID.ClothierVoodooDoll, ItemID.BugNet, ItemID.GoldenBugNet, ItemID.FireproofBugNet
	};

	public static bool IsBossSummon(Item item) => ItemID.Sets.SortingPriorityBossSpawns[item.type] >= 0 || RitualItems.Contains(item.type) || item.questItem;

	static bool IsCheapClutter(Item item) =>
		!IsBossSummon(item) && !item.material && item.damage <= 0 && item.createTile < 0 && item.createWall <= 0 && !item.accessory
		&& item.headSlot < 0 && item.bodySlot < 0 && item.legSlot < 0 && !item.potion && !IsBuffPotion(item) && item.value < OverflowCheapItemValue;

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

	static readonly HashSet<int> CraftingBlocks = new() { ItemID.Obsidian, ItemID.LunarOre, ItemID.LunarBar, ItemID.FragmentSolar, ItemID.FragmentVortex, ItemID.FragmentNebula, ItemID.FragmentStardust };

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
			return Plenty;
		return int.MaxValue;
	}

	static bool IsGrassSeed(Item item) => item.createTile >= 0 && TileID.Sets.Grass[item.createTile];

	static bool IsNaturalBlock(Item item) =>
		item.createTile >= 0
		&& !TileID.Sets.Ore[item.createTile]
		&& ResourceSources.For(item.type) is TileSource { IsTree: false };
}
