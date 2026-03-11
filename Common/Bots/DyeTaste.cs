using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class DyeTaste
{
	const float FavoriteHueDistance = 30f;
	const int Slots = 10;

	static readonly (string token, float hue)[] Hues =
	{
		("SkyBlue", 200f), ("GreenFlame", 120f), ("BlueFlame", 220f), ("Flame", 25f), ("Pink", 320f), ("Violet", 285f),
		("Purple", 270f), ("Blue", 220f), ("Cyan", 180f), ("Teal", 165f), ("Green", 120f), ("Lime", 90f),
		("Yellow", 60f), ("Orange", 30f), ("Red", 0f)
	};

	static readonly Dictionary<int, float> hueByDye = new();

	static float? HueOf(int itemType)
	{
		if (hueByDye.TryGetValue(itemType, out float cached))
			return float.IsNaN(cached) ? null : cached;
		string name = ItemID.Search.TryGetName(itemType, out var found) ? found : "";
		float hue = float.NaN;
		if (name.EndsWith("Dye") && !name.Contains("Hair"))
		{
			foreach (var (token, value) in Hues)
			{
				if (name.Contains(token))
				{
					hue = value;
					break;
				}
			}
		}
		hueByDye[itemType] = hue;
		return float.IsNaN(hue) ? null : hue;
	}

	static float HueOf(Color color) => Main.rgbToHsl(color).X * 360f;

	static float Distance(float a, float b)
	{
		float difference = Math.Abs(a - b) % 360f;
		return difference > 180f ? 360f - difference : difference;
	}

	public static float? Mismatch(Faction faction, Item item) =>
		!item.IsAir && HueOf(item.type) is float hue ? Distance(hue, HueOf(faction.Color)) : null;

	public static bool IsFavorite(Faction faction, Item item) => Mismatch(faction, item) <= FavoriteHueDistance;

	public static int FavoriteOnSale(Bot bot)
	{
		int dyeType = ItemID.None;
		float best = FavoriteHueDistance;
		foreach (var (itemType, _, _) in Planning.Shops.CurrentOffers(bot))
		{
			if (Mismatch(bot.Faction, ContentSamples.ItemsByType[itemType]) is float mismatch && mismatch <= best)
			{
				best = mismatch;
				dyeType = itemType;
			}
		}
		return dyeType;
	}

	public static bool CarriesFavorite(Bot bot) => Inventory.Items(bot.Avatar).Any(item => IsFavorite(bot.Faction, item));

	public static int MissingDyes(Player avatar)
	{
		int missing = 0;
		for (int slot = 0; slot < Slots; slot++)
		{
			if (!avatar.armor[slot].IsAir && avatar.dye[slot].IsAir)
				missing++;
		}
		return missing;
	}

	public static void Wear(Bot bot)
	{
		var avatar = bot.Avatar;
		for (int slot = 0; slot < Slots; slot++)
		{
			if (avatar.armor[slot].IsAir)
				continue;
			float worn = Mismatch(bot.Faction, avatar.dye[slot]) ?? float.MaxValue;
			Item better = null;
			float betterMismatch = Math.Min(worn, FavoriteHueDistance + 0.01f);
			for (int index = 0; index < Inventory.StorageSlots; index++)
			{
				var item = avatar.inventory[index];
				if (Mismatch(bot.Faction, item) is float mismatch && mismatch < betterMismatch)
				{
					better = item;
					betterMismatch = mismatch;
				}
			}
			if (better == null)
				continue;
			var old = avatar.dye[slot];
			avatar.dye[slot] = better.Clone();
			avatar.dye[slot].stack = 1;
			if (--better.stack <= 0)
				better.TurnToAir();
			if (!old.IsAir)
				Inventory.Add(avatar, old);
		}
	}
}
