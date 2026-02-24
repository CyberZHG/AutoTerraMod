using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Bots;

public static class AvatarStorage
{
	public static TagCompound Save(Player avatar) => new()
	{
		["name"] = avatar.name,
		["skinVariant"] = avatar.skinVariant,
		["hair"] = avatar.hair,
		["hairColor"] = avatar.hairColor,
		["skinColor"] = avatar.skinColor,
		["eyeColor"] = avatar.eyeColor,
		["shirtColor"] = avatar.shirtColor,
		["underShirtColor"] = avatar.underShirtColor,
		["pantsColor"] = avatar.pantsColor,
		["shoeColor"] = avatar.shoeColor,
		["lifeMax"] = avatar.statLifeMax,
		["life"] = avatar.statLife,
		["manaMax"] = avatar.statManaMax,
		["lifeCrystals"] = avatar.ConsumedLifeCrystals,
		["lifeFruit"] = avatar.ConsumedLifeFruit,
		["manaCrystals"] = avatar.ConsumedManaCrystals,
		["inventory"] = SaveSlots(avatar.inventory),
		["armor"] = SaveSlots(avatar.armor),
		["miscEquips"] = SaveSlots(avatar.miscEquips)
	};

	public static Player Load(TagCompound tag)
	{
		var avatar = new Player
		{
			name = tag.GetString("name"),
			skinVariant = tag.GetInt("skinVariant"),
			hair = tag.GetInt("hair"),
			hairColor = tag.Get<Color>("hairColor"),
			skinColor = tag.Get<Color>("skinColor"),
			eyeColor = tag.Get<Color>("eyeColor"),
			shirtColor = tag.Get<Color>("shirtColor"),
			underShirtColor = tag.Get<Color>("underShirtColor"),
			pantsColor = tag.Get<Color>("pantsColor"),
			shoeColor = tag.Get<Color>("shoeColor"),
			statLifeMax = tag.GetInt("lifeMax"),
			statManaMax = tag.GetInt("manaMax")
		};
		avatar.statLife = tag.GetInt("life");
		avatar.ConsumedLifeCrystals = tag.GetInt("lifeCrystals");
		avatar.ConsumedLifeFruit = tag.GetInt("lifeFruit");
		avatar.ConsumedManaCrystals = tag.GetInt("manaCrystals");
		LoadSlots(avatar.inventory, tag.GetList<TagCompound>("inventory"));
		LoadSlots(avatar.armor, tag.GetList<TagCompound>("armor"));
		LoadSlots(avatar.miscEquips, tag.GetList<TagCompound>("miscEquips"));
		return avatar;
	}

	static List<TagCompound> SaveSlots(Item[] items) => items
		.Select((item, slot) => (item, slot))
		.Where(entry => !entry.item.IsAir)
		.Select(entry => new TagCompound { ["slot"] = entry.slot, ["item"] = ItemIO.Save(entry.item) })
		.ToList();

	static void LoadSlots(Item[] items, IList<TagCompound> saved)
	{
		foreach (var entry in saved)
		{
			int slot = entry.GetInt("slot");
			if (slot < items.Length)
				items[slot] = ItemIO.Load(entry.GetCompound("item"));
		}
	}
}
