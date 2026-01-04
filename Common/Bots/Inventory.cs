using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public static class Inventory
{
	public const int StorageSlots = 58;
	const int MainSlots = 50;

	public static IEnumerable<Item> Items(Player avatar) => avatar.inventory.Take(StorageSlots).Where(item => !item.IsAir);

	public static int Count(Player avatar, int type) => Items(avatar).Where(item => item.type == type).Sum(item => item.stack);

	public static int CountAny(Player avatar, IEnumerable<int> types)
	{
		var accepted = types as ISet<int> ?? types.ToHashSet();
		return Items(avatar).Where(item => accepted.Contains(item.type)).Sum(item => item.stack);
	}

	public static bool Owns(Player avatar, int type) => Count(avatar, type) > 0 || avatar.armor.Any(item => item.type == type);

	public static int FindSlot(Player avatar, Item item) => Array.IndexOf(avatar.inventory, item);

	public static int UsedSlots(Player avatar) => avatar.inventory.Take(MainSlots).Count(item => !item.IsAir);

	public static bool HasRoomFor(Player avatar, Item incoming) =>
		avatar.inventory.Take(MainSlots).Any(item => item.IsAir || CanMerge(item, incoming));

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
