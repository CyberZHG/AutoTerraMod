using System.Linq;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class StatBoosters
{
	const int LifeCrystalBonus = 20;
	const int LifeFruitBonus = 5;
	const int ManaCrystalBonus = 20;
	const int LifeFruitMax = 20;

	const float SharingCooperation = 0.6f;

	public static readonly int[] BoosterTypes = { ItemID.LifeCrystal, ItemID.LifeFruit, ItemID.ManaCrystal, ItemID.FallenStar };

	public static bool IsBooster(int itemType) => BoosterTypes.Contains(itemType);

	public static bool KeepsForSelf(Bot bot, int itemType)
	{
		var avatar = bot.Avatar;
		return itemType switch
		{
			ItemID.LifeCrystal => WantsLifeCrystals(avatar) && !LeavesForNeedierMate(bot, mate => mate.ConsumedLifeCrystals < avatar.ConsumedLifeCrystals),
			ItemID.LifeFruit => WantsLifeFruit(avatar) && !LeavesForNeedierMate(bot, mate => mate.ConsumedLifeFruit < avatar.ConsumedLifeFruit),
			ItemID.ManaCrystal or ItemID.FallenStar => avatar.ConsumedManaCrystals < Player.ManaCrystalMax
				&& !LeavesForNeedierMate(bot, mate => mate.ConsumedManaCrystals < avatar.ConsumedManaCrystals),
			_ => false
		};
	}

	static bool LeavesForNeedierMate(Bot bot, System.Func<Player, bool> isNeedier) =>
		bot.Personality.Cooperation >= SharingCooperation
		&& Systems.BotWorldSystem.Bots.Any(mate => mate != bot && mate.FactionId == bot.FactionId && isNeedier(mate.Avatar));

	public static bool CarriesBoostersToShare(Bot bot) =>
		Inventory.Items(bot.Avatar).Any(item => IsBooster(item.type) && !KeepsForSelf(bot, item.type));

	public static bool WantsLifeCrystals(Player avatar) => avatar.ConsumedLifeCrystals < Player.LifeCrystalMax;

	public static bool WantsLifeFruit(Player avatar) => !WantsLifeCrystals(avatar) && avatar.ConsumedLifeFruit < LifeFruitMax;

	public static void ConsumeUsable(Bot bot)
	{
		var avatar = bot.Avatar;
		while (KeepsForSelf(bot, ItemID.LifeCrystal) && TryConsume(bot, ItemID.LifeCrystal))
		{
			avatar.UseHealthMaxIncreasingItem(LifeCrystalBonus);
			avatar.ConsumedLifeCrystals++;
			Announce(bot, CombatText.HealLife, LifeCrystalBonus);
		}
		while (KeepsForSelf(bot, ItemID.LifeFruit) && TryConsume(bot, ItemID.LifeFruit))
		{
			avatar.UseHealthMaxIncreasingItem(LifeFruitBonus);
			avatar.ConsumedLifeFruit++;
			Announce(bot, CombatText.HealLife, LifeFruitBonus);
		}
		while (KeepsForSelf(bot, ItemID.ManaCrystal) && TryConsume(bot, ItemID.ManaCrystal))
		{
			avatar.UseManaMaxIncreasingItem(ManaCrystalBonus);
			avatar.ConsumedManaCrystals++;
			Announce(bot, CombatText.HealMana, ManaCrystalBonus);
		}
	}

	static bool TryConsume(Bot bot, int itemType)
	{
		var item = Inventory.Items(bot.Avatar).FirstOrDefault(candidate => candidate.type == itemType);
		if (item == null)
			return false;
		if (item.UseSound.HasValue)
			SoundEngine.PlaySound(item.UseSound.Value, bot.Center);
		Inventory.ConsumeOne(bot.Avatar, item);
		BotDiagnostics.Log(bot, $"used {Lang.GetItemNameValue(itemType)}");
		return true;
	}

	static void Announce(Bot bot, Microsoft.Xna.Framework.Color color, int amount)
	{
		var body = bot.Npc;
		if (body != null)
			CombatText.NewText(body.Hitbox, color, amount);
	}
}
