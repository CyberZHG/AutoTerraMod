using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class WarScenario : ModSystem
{
	const int EncampmentOffset = 40;
	const int ReportIntervalTicks = 3600;

	static readonly Dictionary<int, HashSet<Point>> builtTiles = new();
	static readonly Dictionary<int, int> storedItems = new();

	static bool Active => HeadlessTestMode.Scenario == "war";

	public static void Prepare()
	{
		if (!Active)
			return;
		var home = BotWorldSystem.Factions.First();
		foreach (var invader in BotWorldSystem.Factions.Skip(1))
		{
			int x = home.BaseTile.X + EncampmentOffset * (invader.Id % 2 == 0 ? -1 : 1);
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == invader.Id))
				bot.SavedFeetPosition = new Vector2(x * 16f + 8f, World.TileQuery.SurfaceY(x) * 16f);
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			builtTiles[faction.Id] = BuiltTiles(faction);
			storedItems[faction.Id] = StoredItems(faction);
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"war scenario prepared: relation={BotWorldSystem.Relation}, invaders camp {EncampmentOffset} tiles from {home.Name}'s base; built tiles {string.Join(", ", BotWorldSystem.Factions.Select(faction => $"{faction.Name} {builtTiles[faction.Id].Count}"))}");
	}

	static HashSet<Point> BuiltTiles(Factions.Faction faction)
	{
		var tiles = new HashSet<Point>();
		var settlement = faction.Settlement;
		if (settlement == null)
			return tiles;
		for (int x = settlement.Left; x <= settlement.Right; x++)
		{
			for (int y = settlement.Top; y <= settlement.Floor; y++)
			{
				if (settlement.IsShell(x, y) && (Main.tile[x, y].HasTile || Main.tile[x, y].WallType > 0))
					tiles.Add(new Point(x, y));
			}
		}
		return tiles;
	}

	static int StoredItems(Factions.Faction faction)
	{
		var settlement = faction.Settlement;
		if (settlement == null)
			return 0;
		return Main.chest.Where(chest => chest != null && settlement.Contains(chest.x, chest.y)).Sum(chest => chest.item.Where(item => item != null && !item.IsAir).Sum(item => item.stack));
	}

	public override void PostUpdateWorld()
	{
		if (!Active || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (!builtTiles.TryGetValue(faction.Id, out var before))
				continue;
			var now = BuiltTiles(faction);
			int lost = before.Count(tile => !now.Contains(tile));
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"war report {faction.Name}: built tiles lost {lost}/{before.Count}, stored items {storedItems[faction.Id]} -> {StoredItems(faction)}, alive {BotWorldSystem.Bots.Count(bot => bot.FactionId == faction.Id && bot.IsAlive)}");
		}
	}
}
