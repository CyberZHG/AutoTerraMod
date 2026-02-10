using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Spectating;

public sealed class SpectatorUiMirror : ModSystem
{
	public override void Load() => On_Main.DrawInterface += DrawInterfaceAsFollowedBot;

	static readonly Snapshot snapshot = new();

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
		var mouseItem = Main.mouseItem;
		var trashItem = spectator.trashItem;
		bool mouseWasEmpty = mouseItem.IsAir;
		bool trashWasEmpty = trashItem.IsAir;
		int loadout = spectator.CurrentLoadoutIndex;
		try
		{
			snapshot.SyncFrom(bot.Avatar).ApplyTo(spectator);
			orig(self, gameTime);
			if (spectator.CurrentLoadoutIndex != loadout)
				spectator.TrySwitchingLoadout(loadout);
		}
		finally
		{
			original.ApplyTo(spectator);
			Main.mouseItem = mouseItem;
			spectator.trashItem = trashItem;
			if (mouseWasEmpty)
				mouseItem.TurnToAir();
			if (trashWasEmpty)
				trashItem.TurnToAir();
		}
	}

	sealed class Snapshot
	{
		Item[] inventory, armor, dye, miscEquips, miscDyes;
		int[] buffType, buffTime;

		public PlayerView SyncFrom(Player bot)
		{
			Sync(ref inventory, bot.inventory);
			Sync(ref armor, bot.armor);
			Sync(ref dye, bot.dye);
			Sync(ref miscEquips, bot.miscEquips);
			Sync(ref miscDyes, bot.miscDyes);
			Sync(ref buffType, bot.buffType);
			Sync(ref buffTime, bot.buffTime);
			return PlayerView.Capture(bot) with
			{
				Inventory = inventory,
				Armor = armor,
				Dye = dye,
				MiscEquips = miscEquips,
				MiscDyes = miscDyes,
				BuffType = buffType,
				BuffTime = buffTime,
				Ghost = false,
				Dead = false
			};
		}

		static void Sync(ref Item[] copy, Item[] source)
		{
			if (copy == null || copy.Length != source.Length)
				copy = new Item[source.Length];
			for (int i = 0; i < source.Length; i++)
			{
				var real = source[i];
				var shown = copy[i];
				if (shown != null && shown.type == real.type && shown.stack == real.stack && shown.prefix == real.prefix && shown.favorited == real.favorited)
					continue;
				copy[i] = real.IsAir ? new Item() : real.Clone();
			}
		}

		static void Sync(ref int[] copy, int[] source)
		{
			if (copy == null || copy.Length != source.Length)
				copy = new int[source.Length];
			System.Array.Copy(source, copy, source.Length);
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
