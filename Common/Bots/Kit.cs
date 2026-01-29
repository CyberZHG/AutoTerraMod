using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class Kit
{
	const int TorchReserve = 30;
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
			Inventory.Items(avatar).Where(item => Equipment.PowerOf(item, GearKind.Hook) > 0).MaxBy(item => Equipment.PowerOf(item, GearKind.Hook))
		};
		var plannedTypes = bot.Brain.PlannedItemTypes;
		int ammoType = Inventory.Items(avatar).Where(BotHands.IsRangedWeapon).Select(weapon => weapon.useAmmo).FirstOrDefault();
		var keptByReserve = new Dictionary<int, int>();
		var toStore = new List<Item>();
		foreach (var item in Inventory.Items(avatar).ToList())
		{
			bool keep = essentials.Contains(item)
				|| item.IsACoin
				|| (item.potion && item.healLife > 0)
				|| Junk.IsBossSummon(item)
				|| (StatBoosters.IsBooster(item.type) && StatBoosters.KeepsForSelf(bot, item.type))
				|| (ammoType > 0 && item.ammo == ammoType)
				|| plannedTypes.Contains(item.type)
				|| Tasks.DecorateTask.IsWantedDecor(bot, item)
				|| (Tasks.NaturalistTask.StudiesCritters(bot) && item.makeNPC > 0)
				|| (Tasks.CollectorTask.CollectsArmor(bot) && !item.vanity && (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0))
				|| WithinReserve(item, keptByReserve);
			if (!keep)
				toStore.Add(item);
		}
		return toStore;
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
