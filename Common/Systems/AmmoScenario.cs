using System.Linq;
using AutoTerraMod.Common.Bots;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class AmmoScenario : ModSystem
{
	const int ReportIntervalTicks = 1800;

	static readonly int[] Shopkeepers = { NPCID.ArmsDealer, NPCID.Merchant };

	static bool Active => HeadlessTestMode.Scenario == "ammo";

	public static void Prepare()
	{
		if (!Active)
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			foreach (int type in Shopkeepers)
			{
				int index = NPC.NewNPC(new EntitySource_WorldEvent(), (int)faction.BaseCenter.X, (int)faction.BaseCenter.Y, type);
				Main.npc[index].homeless = false;
				Main.npc[index].homeTileX = faction.BaseTile.X;
				Main.npc[index].homeTileY = faction.BaseTile.Y;
			}
		}
		foreach (var bot in BotWorldSystem.Bots)
		{
			HeadlessTestMode.Give(bot, ItemID.FlintlockPistol, 1);
			HeadlessTestMode.Give(bot, ItemID.MusketBall, 20);
			HeadlessTestMode.Give(bot, ItemID.GoldCoin, 10);
			HeadlessTestMode.Give(bot, ItemID.Bone, 99);
			HeadlessTestMode.Give(bot, ItemID.WoodenSword, 1);
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info("ammo scenario prepared: flintlock + 20 musket balls + 10 gold each; Arms Dealer and Merchant at every base");
	}

	public override void PostUpdateWorld()
	{
		if (!Active || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		foreach (var bot in BotWorldSystem.Bots)
		{
			int bullets = Inventory.Items(bot.Avatar).Where(item => item.ammo == AmmoID.Bullet).Sum(item => item.stack);
			long coins = Utils.CoinsCount(out _, bot.Avatar.inventory);
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"ammo report {bot.Faction.Name}/{bot.Name}: bullets={bullets} coins={coins} weapon={bot.Hands.BestWeapon()?.Name} plan={bot.Brain.Plan}");
		}
	}
}
