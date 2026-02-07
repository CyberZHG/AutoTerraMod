using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public static class Inventory
{
	public const int StorageSlots = 58;
	const int MainSlots = 50;
	public const int MainSlotCount = MainSlots;

	public static InventoryItems Items(Player avatar) => new(avatar.inventory, StorageSlots);

	public static int Count(Player avatar, int type)
	{
		int count = 0;
		foreach (var item in Items(avatar))
		{
			if (item.type == type)
				count += item.stack;
		}
		return count;
	}

	public static int CountAny(Player avatar, IEnumerable<int> types)
	{
		if (types is IReadOnlyList<int> list)
		{
			int total = 0;
			for (int index = 0; index < list.Count; index++)
				total += Count(avatar, list[index]);
			return total;
		}
		var accepted = types as ISet<int> ?? types.ToHashSet();
		int count = 0;
		foreach (var item in Items(avatar))
		{
			if (accepted.Contains(item.type))
				count += item.stack;
		}
		return count;
	}

	public static bool Owns(Player avatar, int type)
	{
		if (Count(avatar, type) > 0)
			return true;
		foreach (var item in avatar.armor)
		{
			if (item.type == type)
				return true;
		}
		return false;
	}

	public static int FindSlot(Player avatar, Item item) => Array.IndexOf(avatar.inventory, item);

	public static int UsedSlots(Player avatar)
	{
		int used = 0;
		for (int slot = 0; slot < MainSlots; slot++)
		{
			if (!avatar.inventory[slot].IsAir)
				used++;
		}
		return used;
	}

	public static bool HasRoomFor(Player avatar, Item incoming)
	{
		for (int slot = 0; slot < MainSlots; slot++)
		{
			var item = avatar.inventory[slot];
			if (item.IsAir || CanMerge(item, incoming))
				return true;
		}
		return false;
	}

	public static Item Add(Player avatar, Item incoming)
	{
		for (int slot = 0; slot < MainSlots && incoming.stack > 0; slot++)
		{
			var held = avatar.inventory[slot];
			if (!CanMerge(held, incoming))
				continue;
			int moved = Math.Min(held.maxStack - held.stack, incoming.stack);
			held.stack += moved;
			incoming.stack -= moved;
		}
		for (int slot = 0; slot < MainSlots && incoming.stack > 0; slot++)
		{
			if (!avatar.inventory[slot].IsAir)
				continue;
			avatar.inventory[slot] = incoming.Clone();
			incoming.TurnToAir();
		}
		if (incoming.stack <= 0)
			incoming.TurnToAir();
		return incoming;
	}

	static readonly int[] CoinTypes = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };

	public static void AddCoins(Player avatar, long copper)
	{
		int[] split = Utils.CoinsSplit(copper);
		for (int tier = 0; tier < CoinTypes.Length; tier++)
		{
			if (split[tier] > 0)
				Add(avatar, new Item(CoinTypes[tier], split[tier]));
		}
	}

	public static bool Consume(Player avatar, IEnumerable<int> acceptedTypes, int amount)
	{
		var accepted = acceptedTypes as ISet<int> ?? acceptedTypes.ToHashSet();
		if (CountAny(avatar, accepted) < amount)
			return false;
		for (int slot = 0; slot < StorageSlots && amount > 0; slot++)
		{
			var item = avatar.inventory[slot];
			if (item.IsAir || !accepted.Contains(item.type))
				continue;
			int taken = Math.Min(item.stack, amount);
			item.stack -= taken;
			amount -= taken;
			if (item.stack <= 0)
				item.TurnToAir();
		}
		return true;
	}

	public static bool ConsumeOne(Player avatar, Item item)
	{
		if (item.IsAir)
			return false;
		item.stack--;
		if (item.stack <= 0)
			item.TurnToAir();
		return true;
	}

	static bool CanMerge(Item held, Item incoming) =>
		!held.IsAir && held.type == incoming.type && held.stack < held.maxStack && ItemLoader.CanStack(held, incoming);
}

public readonly struct InventoryItems : IEnumerable<Item>
{
	readonly Item[] slots;
	readonly int count;

	public InventoryItems(Item[] slots, int count)
	{
		this.slots = slots;
		this.count = Math.Min(count, slots.Length);
	}

	public Enumerator GetEnumerator() => new(slots, count);

	IEnumerator<Item> IEnumerable<Item>.GetEnumerator() => GetEnumerator();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

	public struct Enumerator : IEnumerator<Item>
	{
		readonly Item[] slots;
		readonly int count;
		int index;

		public Enumerator(Item[] slots, int count)
		{
			this.slots = slots;
			this.count = count;
			index = -1;
		}

		public Item Current => slots[index];

		object System.Collections.IEnumerator.Current => Current;

		public bool MoveNext()
		{
			while (++index < count)
			{
				if (!slots[index].IsAir)
					return true;
			}
			return false;
		}

		public void Reset() => index = -1;

		public void Dispose()
		{
		}
	}
}
