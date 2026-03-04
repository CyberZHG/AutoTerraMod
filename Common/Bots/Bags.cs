using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public static class Bags
{
	public static void OpenOneAtHome(Bot bot)
	{
		var tile = bot.CenterTile;
		if (bot.Faction.Settlement?.StructureWithInterior(tile.X, tile.Y) == null)
			return;
		var avatar = bot.Avatar;
		foreach (var item in Inventory.Items(avatar))
		{
			if (!ItemID.Sets.OpenableBag[item.type] || !CanOpen(avatar, item))
				continue;
			string name = item.Name;
			Open(avatar, item);
			BotDiagnostics.Log(bot, $"opened {name}");
			return;
		}
	}

	static bool CanOpen(Player avatar, Item item) => item.type switch
	{
		ItemID.LockBox => Inventory.Count(avatar, ItemID.GoldenKey) > 0,
		ItemID.ObsidianLockbox => Inventory.Count(avatar, ItemID.ShadowKey) > 0,
		_ => true
	};

	static void Open(Player avatar, Item item)
	{
		int type = item.type;
		using (AvatarPresence.Enter())
		{
			if (ItemID.Sets.BossBag[type])
				avatar.OpenBossBag(type);
			else if (ItemID.Sets.IsFishingCrate[type])
				avatar.OpenFishingCrate(type);
			else if (type == ItemID.HerbBag)
				avatar.OpenHerbBag(type);
			else if (type == ItemID.CanOfWorms)
				avatar.OpenCanofWorms(type);
			else if (type == ItemID.Oyster)
				avatar.OpenOyster(type);
			else if (type == ItemID.GoodieBag)
				avatar.OpenGoodieBag(type);
			else if (type == ItemID.Present)
				avatar.OpenPresent(type);
			else if (type == ItemID.LockBox)
			{
				Inventory.Consume(avatar, new int[] { ItemID.GoldenKey }, 1);
				avatar.OpenLockBox(type);
			}
			else if (type == ItemID.ObsidianLockbox)
				avatar.OpenShadowLockbox(type);
			else
				avatar.DropFromItem(type);
		}
		if (ItemLoader.ConsumeItem(item, avatar))
			Inventory.ConsumeOne(avatar, item);
		SoundEngine.PlaySound(SoundID.Grab, avatar.Center);
	}
}
