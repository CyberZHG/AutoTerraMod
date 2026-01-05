using System;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class TreePlanter
{
	const int GroveMargin = 40;
	const int MaxSaplings = 12;
	const int SaplingSpacing = 3;
	const float NearBase = 16f * 40f;

	public static void PlantIfUseful(Bot bot)
	{
		var settlement = bot.Faction.Settlement;
		if (settlement == null || Vector2.Distance(bot.Center, bot.Faction.BaseCenter) > NearBase)
			return;
		var acorn = Inventory.Items(bot.Avatar).FirstOrDefault(item => item.type == ItemID.Acorn);
		if (acorn == null || CountSaplings(settlement) >= MaxSaplings)
			return;
		var here = bot.CenterTile;
		for (int dx = -Player.tileRangeX; dx <= Player.tileRangeX; dx++)
		{
			int x = here.X + dx;
			if (x >= settlement.Left - 2 && x <= settlement.Right + 2)
				continue;
			for (int y = here.Y - 3; y <= here.Y + 4; y++)
			{
				var spot = new Point(x, y);
				if (!IsGoodPlantingSpot(spot) || !bot.Hands.CanPlaceAt(acorn, spot))
					continue;
				if (bot.Hands.Place(acorn, spot))
				{
					BotDiagnostics.Log(bot, $"planted an acorn at {spot}");
					return;
				}
			}
		}
	}

	static bool IsGoodPlantingSpot(Point spot)
	{
		if (Main.tile[spot.X, spot.Y].HasTile || !Main.tile[spot.X, spot.Y + 1].HasTile)
			return false;
		for (int x = spot.X - SaplingSpacing; x <= spot.X + SaplingSpacing; x++)
		{
			for (int y = spot.Y - 2; y <= spot.Y; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && (tile.TileType == TileID.Saplings || TileQuery.IsChoppableTree(tile.TileType)))
					return false;
			}
		}
		return true;
	}

	static int CountSaplings(Building.Settlement settlement)
	{
		int count = 0;
		for (int x = settlement.Left - GroveMargin; x <= settlement.Right + GroveMargin; x++)
		{
			for (int y = settlement.Top - 10; y <= settlement.Floor + 10; y++)
			{
				if (WorldGen.InWorld(x, y, 5) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Saplings)
					count++;
			}
		}
		return count / 2;
	}
}
