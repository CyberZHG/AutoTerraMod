using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class Kit
{
	public const int TorchReserve = 30;
	const int RopeReserve = 60;
	const int PlatformReserve = 20;

	public static List<Item> ItemsToStore(Bot bot)
	{
		var avatar = bot.Avatar;
		var essentials = new HashSet<Item>
		{
			bot.Hands.BestPickaxe,
			bot.Hands.BestAxe,
			bot.Hands.BestWeapon(),
			BackupMelee(bot),
			Inventory.Items(avatar).Where(item => Equipment.PowerOf(item, GearKind.Hook) > 0).MaxBy(item => Equipment.PowerOf(item, GearKind.Hook))
		};
		var plannedTypes = bot.Brain.PlannedItemTypes;
		int ammoType = Inventory.Items(avatar).Where(BotHands.IsRangedWeapon).Select(weapon => weapon.useAmmo).FirstOrDefault();
		var keptByReserve = new Dictionary<int, int>();
		int ammoKept = 0;
		var toStore = new List<Item>();
		foreach (var item in Inventory.Items(avatar).ToList())
		{
			bool keep = essentials.Contains(item)
				|| item.IsACoin
				|| (item.potion && item.healLife > 0)
				|| Junk.IsBossSummon(item)
				|| (StatBoosters.IsBooster(item.type) && StatBoosters.KeepsForSelf(bot, item.type))
				|| (ammoType > 0 && item.ammo == ammoType && WithinAmmoReserve(item, ref ammoKept))
				|| plannedTypes.Contains(item.type)
				|| Tasks.CraftTask.IsPortableStation(item)
				|| item.type == ItemID.TeleportationPotion
				|| Tasks.BotTask.IsRecall(item)
				|| Explosives.IsExplosive(item)
				|| item.type == ItemID.Acorn
				|| item.type == ItemID.Bed || Outpost.IsDoor(item) || Outpost.IsHouseWall(item)
				|| Tasks.DecorateTask.IsWantedDecor(bot, item)
				|| (Tasks.NaturalistTask.StudiesCritters(bot) && item.makeNPC > 0)
				|| (Tasks.CollectorTask.CollectsArmor(bot) && (item.type == ItemID.Mannequin || !item.vanity && (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0)))
				|| WithinReserve(item, keptByReserve);
			if (!keep || IsSurplusTreasure(bot, item))
				toStore.Add(item);
		}
		return toStore;
	}

	const int HotbarSlots = 10;

	static Item BackupMelee(Bot bot) =>
		Inventory.Items(bot.Avatar).Where(item => Equipment.PowerOf(item, GearKind.MeleeWeapon) > 0).MaxBy(item => Equipment.PowerOf(item, GearKind.MeleeWeapon));

	public static void ArrangeHotbar(Bot bot)
	{
		var avatar = bot.Avatar;
		var bag = Inventory.Items(avatar);
		var wanted = new List<Item>
		{
			bot.Hands.BestPickaxe,
			bot.Hands.BestWeapon(),
			BackupMelee(bot),
			bot.Hands.BestAxe,
			bot.Hands.BestHammer,
			bag.FirstOrDefault(item => item.createTile >= 0 && TileID.Sets.Torch[item.createTile]),
			bag.FirstOrDefault(item => item.buffType > 0 && Main.lightPet[item.buffType]),
			bag.FirstOrDefault(item => item.buffType > 0 && Main.vanityPet[item.buffType]),
			bag.Where(item => item.potion && item.healLife > 0).MaxBy(item => item.healLife),
			bag.FirstOrDefault(item => item.type is ItemID.Rope or ItemID.WebRope)
		};
		int hotbar = 0;
		foreach (var item in wanted.Where(item => item != null && !item.IsAir).Distinct())
		{
			if (hotbar >= HotbarSlots)
				break;
			int slot = Inventory.FindSlot(avatar, item);
			if (slot < 0 || slot >= Inventory.MainSlotCount)
				continue;
			if (slot != hotbar)
				Utils.Swap(ref avatar.inventory[slot], ref avatar.inventory[hotbar]);
			hotbar++;
		}
	}

	public static bool IsSurplusTreasure(Bot bot, Item item)
	{
		if (bot.Brain.PlannedItemTypes.Contains(item.type))
			return false;
		if (Tasks.OpportunisticMineTask.GemDrops.ContainsValue(item.type))
			return Equipment.BestPower(bot.Avatar, GearKind.Hook) > 0;
		if (item.createTile < 0 || !Tasks.OpportunisticMineTask.OreTiers.TryGetValue(item.createTile, out int tier))
			return false;
		return tier < Tasks.OpportunisticMineTask.PickTier(bot.Hands.BestPickaxe?.pick ?? 0);
	}

	public static bool CarriesSurplusTreasure(Bot bot)
	{
		foreach (var item in Inventory.Items(bot.Avatar))
		{
			if (IsSurplusTreasure(bot, item))
				return true;
		}
		return false;
	}

	const int AmmoReserve = 400;

	static bool WithinAmmoReserve(Item item, ref int kept)
	{
		if (kept >= AmmoReserve)
			return false;
		kept += item.stack;
		return true;
	}

	static bool WithinReserve(Item item, Dictionary<int, int> keptByReserve)
	{
		int reserve = ReserveFor(item);
		if (reserve <= 0)
			return false;
		int category = item.createTile;
		keptByReserve.TryGetValue(category, out int kept);
		if (kept >= reserve)
			return false;
		keptByReserve[category] = kept + item.stack;
		return true;
	}

	static int ReserveFor(Item item)
	{
		if (item.createTile < 0)
			return 0;
		if (TileID.Sets.Torch[item.createTile])
			return TorchReserve;
		if (Main.tileRope[item.createTile])
			return RopeReserve;
		if (TileID.Sets.Platforms[item.createTile])
			return PlatformReserve;
		return 0;
	}

	public static HashSet<int> ComputePlannedItemTypes(Bot bot)
	{
		var types = new HashSet<int>();
		switch (bot.Brain.Plan)
		{
			case GatherStep gather:
				types.Add(gather.ItemType);
				break;
			case WithdrawStep withdraw:
				types.Add(withdraw.ItemType);
				break;
			case DeliverStep deliver:
				types.Add(deliver.ItemType);
				break;
			case PlaceStationStep place:
				types.Add(place.ItemType);
				break;
			case CraftStep craft:
				foreach (var required in craft.Recipe.requiredItem)
					types.UnionWith(RecipeIndex.AcceptedTypes(craft.Recipe, required));
				break;
		}
		if (bot.Faction.HouseDesign != null && HouseInspector.IsDesignatedBuilder(bot))
		{
			var materials = HouseInspector.MaterialsFor(bot.Faction);
			types.Add(materials.BlockItem);
			types.Add(materials.WallItem);
		}
		return types;
	}
}
