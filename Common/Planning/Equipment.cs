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
			GearKind.Hook => item.shoot > ProjectileID.None && item.shoot < Main.projHook.Length && Main.projHook[item.shoot] ? (int)BotGrapple.RangeOf(item.shoot) : 0,
			_ => 0
		};
	}

	public static int SamplePower(int itemType, GearKind kind) =>
		ContentSamples.ItemsByType.TryGetValue(itemType, out var sample) ? PowerOf(sample, kind) : 0;

	public static int BestPower(Player avatar, GearKind kind) =>
		Inventory.Items(avatar).Concat(avatar.armor.Take(3)).Select(item => PowerOf(item, kind)).DefaultIfEmpty(0).Max();

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
