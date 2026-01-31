using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class EscapeScenario : ModSystem
{
	const int ReportIntervalTicks = 1800;
	const int SurfaceMarginTiles = 5;
	const int MinDepthBelowSurface = 60;
	const int MaxDepthBelowSurface = 160;

	static readonly int[] RecallItems = { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone, ItemID.RecallPotion, ItemID.PotionOfReturn };
	static readonly Dictionary<int, uint> escapedAt = new();
	static readonly HashSet<int> requested = new();
	static readonly HashSet<int> died = new();
	static uint startedAt;

	static bool Active => HeadlessTestMode.Scenario == "escape";

	public static void Prepare()
	{
		if (!Active)
			return;
		startedAt = Main.GameUpdateCount;
		foreach (var bot in BotWorldSystem.Bots)
		{
			HeadlessTestMode.Give(bot, ItemID.DirtBlock, 99);
			HeadlessTestMode.Give(bot, ItemID.LesserHealingPotion, 10);
			bot.Avatar.armor[0].SetDefaults(ItemID.IronHelmet);
			bot.Avatar.armor[1].SetDefaults(ItemID.IronChainmail);
			bot.Avatar.armor[2].SetDefaults(ItemID.IronGreaves);
			var pocket = FindPocket(bot.Faction.BaseTile.X + (bot.Id % 2 == 0 ? -1 : 1) * (30 + bot.Id * 17), new Terraria.Utilities.UnifiedRandom(bot.Id));
			bot.SavedFeetPosition = new Vector2(pocket.X * 16f + 16f, pocket.Y * 16f);
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"escape scenario: {bot.Faction.Name}/{bot.Name} starts in a pocket at {pocket}, {pocket.Y - TileQuery.SurfaceY(pocket.X)} tiles below the surface");
		}
	}

	static Point FindPocket(int nearX, Terraria.Utilities.UnifiedRandom random)
	{
		for (int attempt = 0; attempt < 100000; attempt++)
		{
			int x = Utils.Clamp(nearX + random.Next(-40, 41), 100, Main.maxTilesX - 100);
			int y = TileQuery.SurfaceY(x) + random.Next(MinDepthBelowSurface, MaxDepthBelowSurface);
			if (IsPocket(x, y))
				return new Point(x, y - 1);
		}
		return new Point(nearX, TileQuery.SurfaceY(nearX) - 2);
	}

	static bool IsPocket(int x, int y)
	{
		if (!TileQuery.IsSolid(x, y) || !TileQuery.IsSolid(x + 1, y))
			return false;
		for (int dy = 1; dy <= 3; dy++)
		{
			for (int dx = 0; dx <= 1; dx++)
			{
				var tile = Main.tile[x + dx, y - dy];
				if (tile.HasTile && Main.tileSolid[tile.TileType] || tile.LiquidAmount > 0)
					return false;
			}
		}
		return true;
	}

	public override void PostUpdateWorld()
	{
		if (!Active)
			return;
		foreach (var bot in BotWorldSystem.Bots)
		{
			foreach (var item in bot.Avatar.inventory.Where(item => RecallItems.Contains(item.type)))
				item.TurnToAir();
			if (escapedAt.ContainsKey(bot.Id) || died.Contains(bot.Id))
				continue;
			if (!bot.IsAlive)
			{
				if (requested.Contains(bot.Id))
					died.Add(bot.Id);
				continue;
			}
			var here = bot.CenterTile;
			if (here.Y <= TileQuery.SurfaceY(here.X) + SurfaceMarginTiles)
			{
				escapedAt[bot.Id] = Main.GameUpdateCount;
				ModContent.GetInstance<AutoTerraMod>().Logger.Info($"escape scenario: {bot.Faction.Name}/{bot.Name} reached the surface at {here} after {(Main.GameUpdateCount - startedAt) / 60}s");
				continue;
			}
			requested.Add(bot.Id);
			if (!bot.Watchdog.EscapeRequested)
				bot.Watchdog.RequestEscape();
		}
		if ((Main.GameUpdateCount - startedAt) % ReportIntervalTicks == 0)
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"escape report: {escapedAt.Count}/{BotWorldSystem.Bots.Count} escaped, {died.Count} died; still below: {string.Join(", ", BotWorldSystem.Bots.Where(bot => !escapedAt.ContainsKey(bot.Id) && !died.Contains(bot.Id)).Select(bot => $"{bot.Name}@{bot.CenterTile}({bot.CenterTile.Y - TileQuery.SurfaceY(bot.CenterTile.X)} deep, {bot.Brain.Current?.NameKey}, {bot.Motor.Describe()})"))}");
	}
}
