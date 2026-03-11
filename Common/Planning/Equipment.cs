using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Planning;

public enum GearKind
{
	Pickaxe,
	Head,
	Body,
	Legs,
	MeleeWeapon,
	RangedWeapon,
	MagicWeapon,
	SummonWeapon,
	Hook
}

public static class Equipment
{
	static readonly (int armorSlot, GearKind kind)[] ArmorSlots =
	{
		(0, GearKind.Head),
		(1, GearKind.Body),
		(2, GearKind.Legs)
	};

	public static int PowerOf(Item item, GearKind kind)
	{
		if (item == null || item.IsAir)
			return 0;
		return kind switch
		{
			GearKind.Pickaxe => item.pick,
			GearKind.Head => item.headSlot >= 0 && !item.vanity ? item.defense : 0,
			GearKind.Body => item.bodySlot >= 0 && !item.vanity ? item.defense : 0,
			GearKind.Legs => item.legSlot >= 0 && !item.vanity ? item.defense : 0,
			GearKind.MeleeWeapon => BotHands.StyleOf(item) == CombatStyle.Melee && !BotHands.IsProjectileWeapon(item) && item.pick == 0 && item.axe == 0 ? item.damage : 0,
			GearKind.RangedWeapon => BotHands.IsRangedWeapon(item) ? item.damage : 0,
			GearKind.MagicWeapon => BotHands.IsMagicWeapon(item) ? item.damage : 0,
			GearKind.SummonWeapon => BotHands.IsSummonWeapon(item) ? item.damage : 0,
			GearKind.Hook => item.shoot > ProjectileID.None && item.shoot < Main.projHook.Length && Main.projHook[item.shoot] ? (int)BotGrapple.RangeOf(item.shoot) : 0,
			_ => 0
		};
	}

	public static int SamplePower(int itemType, GearKind kind) =>
		ContentSamples.ItemsByType.TryGetValue(itemType, out var sample) ? PowerOf(sample, kind) : 0;

	public static int BestPower(Player avatar, GearKind kind)
	{
		int best = 0;
		foreach (var item in Inventory.Items(avatar))
			best = System.Math.Max(best, PowerOf(item, kind));
		for (int slot = 0; slot < 3; slot++)
			best = System.Math.Max(best, PowerOf(avatar.armor[slot], kind));
		if (kind == GearKind.Hook)
			best = System.Math.Max(best, PowerOf(avatar.miscEquips[HookSlot], kind));
		return best;
	}

	public const int FirstAccessorySlot = 3;
	public const int LastAccessorySlot = 9;

	public static int AccessoryScore(Item item) =>
		item == null || item.IsAir || !item.accessory || item.vanity || item.type == ItemID.ClothierVoodooDoll
			? 0
			: 1 + item.defense * 10 + System.Math.Max(0, item.rare) * 5 + (item.wingSlot > 0 ? WingPreference : 0);

	const int WingPreference = 60;

	static bool WearsOtherWings(Player avatar, int exceptSlot)
	{
		for (int slot = FirstAccessorySlot; slot <= LastAccessorySlot; slot++)
		{
			if (slot != exceptSlot && avatar.armor[slot].wingSlot > 0 && !avatar.armor[slot].IsAir)
				return true;
		}
		return false;
	}

	public static int WornCount(Player avatar, int itemType)
	{
		int count = 0;
		for (int slot = FirstAccessorySlot; slot <= LastAccessorySlot; slot++)
		{
			if (avatar.armor[slot].type == itemType)
				count++;
		}
		foreach (var item in avatar.miscEquips)
		{
			if (item.type == itemType)
				count++;
		}
		return count;
	}

	public static void UnequipIngredients(Player avatar, Recipe recipe)
	{
		foreach (var required in recipe.requiredItem)
		{
			var accepted = RecipeIndex.AcceptedTypes(recipe, required);
			for (int slot = FirstAccessorySlot; slot <= LastAccessorySlot && Inventory.CountAny(avatar, accepted) < required.stack; slot++)
			{
				if (accepted.Contains(avatar.armor[slot].type) && Inventory.Add(avatar, avatar.armor[slot].Clone()).IsAir)
					avatar.armor[slot].TurnToAir();
			}
			for (int slot = 0; slot < avatar.miscEquips.Length && Inventory.CountAny(avatar, accepted) < required.stack; slot++)
			{
				if (accepted.Contains(avatar.miscEquips[slot].type) && Inventory.Add(avatar, avatar.miscEquips[slot].Clone()).IsAir)
					avatar.miscEquips[slot].TurnToAir();
			}
		}
	}

	public static void EquipBestAccessories(Player avatar, ICollection<int> reserved)
	{
		for (int armorSlot = FirstAccessorySlot; armorSlot <= LastAccessorySlot; armorSlot++)
		{
			if (!avatar.IsItemSlotUnlockedAndUsable(armorSlot) || avatar.armor[armorSlot].type == ItemID.ClothierVoodooDoll)
				continue;
			int equippedScore = AccessoryScore(avatar.armor[armorSlot]);
			int bestSlot = -1;
			for (int slot = 0; slot < Inventory.StorageSlots; slot++)
			{
				var candidate = avatar.inventory[slot];
				if (!candidate.accessory || candidate.IsAir)
					continue;
				int score = reserved.Contains(candidate.type) || candidate.wingSlot > 0 && WearsOtherWings(avatar, armorSlot) ? 0 : AccessoryScore(candidate);
				if (score > equippedScore && WornCount(avatar, candidate.type) == 0)
				{
					equippedScore = score;
					bestSlot = slot;
				}
			}
			if (bestSlot >= 0)
				Utils.Swap(ref avatar.inventory[bestSlot], ref avatar.armor[armorSlot]);
		}
	}

	public const int HookSlot = 4;
	const int PetSlot = 0, LightPetSlot = 1, CartSlot = 2, MountSlot = 3;

	static int MiscSlotFor(Item item)
	{
		if (item.IsAir)
			return -1;
		if (PowerOf(item, GearKind.Hook) > 0)
			return HookSlot;
		if (item.buffType > 0 && Main.vanityPet[item.buffType])
			return PetSlot;
		if (item.buffType > 0 && Main.lightPet[item.buffType])
			return LightPetSlot;
		if (item.mountType >= 0)
			return MountID.Sets.Cart[item.mountType] ? CartSlot : MountSlot;
		return -1;
	}

	public static void EquipMiscGear(Player avatar, ICollection<int> reserved)
	{
		for (int slot = 0; slot < Inventory.StorageSlots; slot++)
		{
			var candidate = avatar.inventory[slot];
			int miscSlot = MiscSlotFor(candidate);
			if (miscSlot < 0 || reserved.Contains(candidate.type))
				continue;
			var worn = avatar.miscEquips[miscSlot];
			bool better = worn.IsAir || (miscSlot == HookSlot && PowerOf(candidate, GearKind.Hook) > PowerOf(worn, GearKind.Hook));
			if (better)
				Utils.Swap(ref avatar.inventory[slot], ref avatar.miscEquips[miscSlot]);
		}
	}

	public static void EquipBestArmor(Player avatar)
	{
		foreach (var (armorSlot, kind) in ArmorSlots)
		{
			int equippedPower = PowerOf(avatar.armor[armorSlot], kind);
			int bestSlot = -1;
			for (int slot = 0; slot < Inventory.StorageSlots; slot++)
			{
				int power = PowerOf(avatar.inventory[slot], kind);
				if (power > equippedPower)
				{
					equippedPower = power;
					bestSlot = slot;
				}
			}
			if (bestSlot >= 0)
				Utils.Swap(ref avatar.inventory[bestSlot], ref avatar.armor[armorSlot]);
		}
	}
}
