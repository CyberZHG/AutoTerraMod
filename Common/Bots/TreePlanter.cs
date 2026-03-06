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

	const int GroveGap = 3;
	const int GroundSearchAbove = 15;
	const int GroundSearchBelow = 10;

	public static bool WantsSaplings(Building.Settlement settlement) => CountSaplings(settlement) < MaxSaplings;

	public static Point? FindSpot(Bot bot, Building.Settlement settlement, Item acorn)
	{
		Point? best = null;
		long bestDistance = long.MaxValue;
		var here = bot.CenterTile;
		for (int x = settlement.Left - GroveMargin; x <= settlement.Right + GroveMargin; x++)
		{
			if (x >= settlement.Left - GroveGap && x <= settlement.Right + GroveGap || Tasks.TidyCampTask.IsEntrance(settlement, x) || !WorldGen.InWorld(x, settlement.Floor, 10))
				continue;
			if (GroundAt(x, settlement.Floor) is not Point spot || !IsGoodPlantingSpot(spot) || !bot.Hands.CanPlaceAt(acorn, spot))
				continue;
			long distance = WorldScan.DistanceSq(here, spot);
			if (distance < bestDistance)
			{
				best = spot;
				bestDistance = distance;
			}
		}
		return best;
	}

	static Point? GroundAt(int x, int floor)
	{
		for (int y = floor - GroundSearchAbove; y <= floor + GroundSearchBelow; y++)
		{
			if (TileQuery.IsSolid(x, y + 1))
				return Main.tile[x, y].HasTile ? null : new Point(x, y);
		}
		return null;
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
