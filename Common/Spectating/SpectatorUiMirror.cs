using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Spectating;

public sealed class SpectatorUiMirror : ModSystem
{
	public override void Load() => On_Main.DrawInterface += DrawInterfaceAsFollowedBot;

	static void DrawInterfaceAsFollowedBot(On_Main.orig_DrawInterface orig, Main self, GameTime gameTime)
	{
		var bot = SpectatorSystem.Followed;
		if (bot == null || Main.gameMenu)
		{
			orig(self, gameTime);
			return;
		}
		var spectator = Main.LocalPlayer;
		var original = PlayerView.Capture(spectator);
		try
		{
			PlayerView.Capture(bot.Avatar).AsReadOnlyCopy().ApplyTo(spectator);
			orig(self, gameTime);
		}
		finally
		{
			original.ApplyTo(spectator);
		}
	}

	readonly record struct PlayerView(
		Item[] Inventory, Item[] Armor, Item[] Dye, Item[] MiscEquips, Item[] MiscDyes,
		int[] BuffType, int[] BuffTime,
		int Life, int LifeMax, int LifeMax2, int Mana, int ManaMax, int ManaMax2,
		int Breath, int BreathMax, int SelectedItem, Player.DefenseStat Defense,
		int LifeCrystals, int LifeFruit, int ManaCrystals, bool Ghost, bool Dead)
	{
		public static PlayerView Capture(Player player) => new(
			player.inventory, player.armor, player.dye, player.miscEquips, player.miscDyes,
			player.buffType, player.buffTime,
			player.statLife, player.statLifeMax, player.statLifeMax2, player.statMana, player.statManaMax, player.statManaMax2,
			player.breath, player.breathMax, player.selectedItem, player.statDefense,
			player.ConsumedLifeCrystals, player.ConsumedLifeFruit, player.ConsumedManaCrystals, player.ghost, player.dead);

		public PlayerView AsReadOnlyCopy() => this with
		{
			Inventory = (Item[])Inventory.Clone(),
			Armor = (Item[])Armor.Clone(),
			Dye = (Item[])Dye.Clone(),
			MiscEquips = (Item[])MiscEquips.Clone(),
			MiscDyes = (Item[])MiscDyes.Clone(),
			BuffType = (int[])BuffType.Clone(),
			BuffTime = (int[])BuffTime.Clone(),
			Ghost = false,
			Dead = false
		};

		public void ApplyTo(Player player)
		{
			player.inventory = Inventory;
			player.armor = Armor;
			player.dye = Dye;
			player.miscEquips = MiscEquips;
			player.miscDyes = MiscDyes;
			player.buffType = BuffType;
			player.buffTime = BuffTime;
			player.statLife = Life;
			player.statLifeMax = LifeMax;
			player.statLifeMax2 = LifeMax2;
			player.statMana = Mana;
			player.statManaMax = ManaMax;
			player.statManaMax2 = ManaMax2;
			player.breath = Breath;
			player.breathMax = BreathMax;
			player.selectedItem = SelectedItem;
			player.statDefense = Defense;
			player.ConsumedLifeCrystals = LifeCrystals;
			player.ConsumedLifeFruit = LifeFruit;
			player.ConsumedManaCrystals = ManaCrystals;
			player.ghost = Ghost;
			player.dead = Dead;
		}
	}
}
