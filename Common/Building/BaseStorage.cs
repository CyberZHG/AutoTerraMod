using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Building;

public enum StorageCategory
{
	OresAndBars,
	Blocks,
	WoodAndPlants,
	Materials,
	Equipment,
	Consumables,
	Misc
}

public static class BaseStorage
{
	public static bool IsStorageItem(Item item) => item.createTile >= 0 && TileID.Sets.BasicChest[item.createTile];

	const int CacheTicks = 60;

	static readonly Dictionary<int, (uint tick, List<int> indices)> chestCache = new();

	public static void Reset() => chestCache.Clear();

	public static IReadOnlyList<int> ChestIndices(Faction faction)
	{
		if (chestCache.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.tick < CacheTicks)
			return cached.indices.Where(index => Main.chest[index] != null).ToList();
		var indices = FindChestsInHouse(faction).ToList();
		chestCache[faction.Id] = (Main.GameUpdateCount, indices);
		return indices;
	}

	static IEnumerable<int> FindChestsInHouse(Faction faction)
	{
		var settlement = faction.Settlement;
		if (settlement == null)
			yield break;
		for (int index = 0; index < Main.maxChests; index++)
		{
			var chest = Main.chest[index];
			if (chest == null || Chest.IsLocked(chest.x, chest.y))
				continue;
			if (settlement.Storage.IsInterior(chest.x, chest.y) || settlement.Hall.IsInterior(chest.x, chest.y))
				yield return index;
		}
	}

	public static StorageCategory CategoryOfChest(Faction faction, Chest chest)
	{
		int slot = faction.Settlement.Storage.ChestSlots().ToList().FindIndex(spot => spot.X == chest.x);
		return slot >= 0 && slot < (int)StorageCategory.Misc ? (StorageCategory)slot : StorageCategory.Misc;
	}

	static bool IsWood(Item item) =>
		RecipeGroup.recipeGroups.TryGetValue(RecipeGroupID.Wood, out var wood) && wood.ContainsItem(item.type);

	public static StorageCategory CategoryOf(Item item)
	{
		if (item.createTile >= 0 && (TileID.Sets.Ore[item.createTile] || item.createTile == TileID.MetalBars || item.createTile == TileID.ExposedGems))
			return StorageCategory.OresAndBars;
		if (IsWood(item) || (item.createTile >= 0 && (TileID.Sets.TreeSapling[item.createTile] || Main.tileCut[item.createTile])))
			return StorageCategory.WoodAndPlants;
		if (item.createTile >= 0 && Main.tileSolid[item.createTile] && !Main.tileFrameImportant[item.createTile])
			return StorageCategory.Blocks;
		if (item.damage > 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0 || item.accessory)
			return StorageCategory.Equipment;
		if (item.consumable || item.potion || item.ammo > 0)
			return StorageCategory.Consumables;
		if (item.material)
			return StorageCategory.Materials;
		return StorageCategory.Misc;
	}

	public static IEnumerable<Chest> Chests(Faction faction) => ChestIndices(faction).Select(index => Main.chest[index]);

	public static int Count(Faction faction, int itemType) =>
		Chests(faction).SelectMany(chest => chest.item).Where(item => item != null && item.type == itemType).Sum(item => item.stack);

	public static bool HasRoom(Faction faction) => Chests(faction).Any(chest => chest.item.Any(item => item == null || item.IsAir));

	public static int FreeSlots(Faction faction) => Chests(faction).Sum(chest => chest.item.Count(item => item == null || item.IsAir));

	public static int FullChestCount(Faction faction) => Chests(faction).Count(chest => chest.item.All(item => item != null && !item.IsAir));

	public static Chest ChestWith(Faction faction, int itemType, Vector2 near) => Chests(faction)
		.Where(chest => chest.item.Any(item => item != null && item.type == itemType))
		.OrderBy(chest => Vector2.DistanceSquared(near, TileOf(chest).ToWorldCoordinates()))
		.FirstOrDefault();

	public static Chest ChestWithRoom(Faction faction, Vector2 near) => Chests(faction)
		.Where(chest => chest.item.Any(item => item == null || item.IsAir))
		.OrderBy(chest => Vector2.DistanceSquared(near, TileOf(chest).ToWorldCoordinates()))
		.FirstOrDefault();

	public static Point TileOf(Chest chest) => new(chest.x, chest.y);

	public static Item Deposit(Chest chest, Item incoming)
	{
		foreach (var held in chest.item.Where(held => held != null && !held.IsAir && held.type == incoming.type && held.stack < held.maxStack))
		{
			if (!ItemLoader.CanStack(held, incoming))
				continue;
			int moved = Math.Min(held.maxStack - held.stack, incoming.stack);
			held.stack += moved;
			incoming.stack -= moved;
			if (incoming.stack <= 0)
				return new Item();
		}
		for (int slot = 0; slot < chest.item.Length; slot++)
		{
			if (chest.item[slot] != null && !chest.item[slot].IsAir)
				continue;
			chest.item[slot] = incoming.Clone();
			return new Item();
		}
		return incoming;
	}

	public static int Withdraw(Player avatar, Chest chest, int itemType, int amount)
	{
		int taken = 0;
		for (int slot = 0; slot < chest.item.Length && taken < amount; slot++)
		{
			var held = chest.item[slot];
			if (held == null || held.IsAir || held.type != itemType)
				continue;
			int portion = Math.Min(held.stack, amount - taken);
			var moving = held.Clone();
			moving.stack = portion;
			var leftover = Inventory.Add(avatar, moving);
			int moved = portion - (leftover.IsAir ? 0 : leftover.stack);
			held.stack -= moved;
			taken += moved;
			if (held.stack <= 0)
				chest.item[slot] = new Item();
			if (!leftover.IsAir)
				break;
		}
		return taken;
	}
}
