using System;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class HeadlessTestMode : ModSystem
{
	public static bool Enabled => Main.dedServ && Environment.GetEnvironmentVariable("AUTOTERRA_HEADLESS") == "1";
	public static string Scenario => Enabled ? Environment.GetEnvironmentVariable("AUTOTERRA_SCENARIO") : null;

	static readonly int[] BossScenarioKit =
	{
		ItemID.PlatinumBroadsword, ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves
	};

	public static void PrepareScenario()
	{
		if (Scenario != "boss")
			return;
		foreach (var bot in BotWorldSystem.Bots)
		{
			Bots.Junk.TrashExcess(bot);
			foreach (int type in BossScenarioKit)
				Give(bot, type, 1);
			Give(bot, ItemID.LesserHealingPotion, 10);
			if (bot.Avatar.statLifeMax < 200)
				Give(bot, ItemID.LifeCrystal, (200 - bot.Avatar.statLifeMax) / 20);
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			var summoner = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id).MaxBy(bot => bot.Personality.Combat);
			if (summoner != null)
				Give(summoner, ItemID.SlimeCrown, 1);
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info("headless scenario 'boss' prepared");
	}

	static void Give(Bots.Bot bot, int type, int stack)
	{
		int missing = stack - Bots.Inventory.Count(bot.Avatar, type) - bot.Avatar.armor.Count(item => item.type == type);
		if (missing <= 0)
			return;
		var item = new Item();
		item.SetDefaults(type);
		item.stack = missing;
		if (!Bots.Inventory.HasRoomFor(bot.Avatar, item))
			bot.Avatar.inventory.Take(Bots.Inventory.MainSlotCount).Where(held => held.createTile >= 0).MaxBy(held => held.stack)?.TurnToAir();
		if (!Bots.Inventory.Add(bot.Avatar, item).IsAir)
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"scenario: no room for {item.Name} in {bot.Name}'s inventory");
	}

	public override void Load()
	{
		if (Enabled)
			On_Netplay.UpdateConnectedClients += KeepWorldRunningWithoutClients;
	}

	static void KeepWorldRunningWithoutClients(On_Netplay.orig_UpdateConnectedClients orig)
	{
		if (Netplay.Clients.Any(client => client.IsConnected() || client.IsActive))
			orig();
		Netplay.HasClients = true;
	}

	public override void PostUpdateWorld()
	{
		if (Enabled && Main.netMode == NetmodeID.Server && Main.GameUpdateCount % 3600 == 0)
			Mod.Logger.Info($"headless test running: {Main.GameUpdateCount / 60}s of game time");
	}
}
