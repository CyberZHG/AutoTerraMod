using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class WaterFinder
{
	const int AdjacentRange = 4;
	const int SearchHalfWidth = 250;
	const int SearchRowsAbove = 60;
	const int SearchRowsBelow = 80;
	const int MinimumPoolDepth = 2;
	const int CacheTicks = 3600;

	static readonly Dictionary<Point, (uint tick, Point? shore)> cache = new();

	public static void Reset() => cache.Clear();

	public static bool IsNearWater(Point center)
	{
		for (int x = center.X - AdjacentRange; x <= center.X + AdjacentRange; x++)
		{
			for (int y = center.Y - AdjacentRange; y <= center.Y + AdjacentRange; y++)
			{
				if (IsWater(x, y))
					return true;
			}
		}
		return false;
	}

	public static Point? NearestShore(Point anchor)
	{
		if (cache.TryGetValue(anchor, out var cached) && Main.GameUpdateCount - cached.tick < CacheTicks)
			return cached.shore;
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = anchor.X - SearchHalfWidth; x <= anchor.X + SearchHalfWidth; x++)
		{
			for (int y = anchor.Y - SearchRowsAbove; y <= anchor.Y + SearchRowsBelow; y++)
			{
				if (!IsWaterSurface(x, y))
					continue;
				var surface = new Point(x, y - 1);
				long distance = WorldScan.DistanceSq(anchor, surface);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = surface;
				}
			}
		}
		cache[anchor] = (Main.GameUpdateCount, best);
		return best;
	}

	static bool IsWater(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water;
	}

	static bool IsWaterSurface(int x, int y)
	{
		if (!IsWater(x, y) || IsWater(x, y - 1) || TileQuery.IsSolid(x, y - 1))
			return false;
		for (int depth = 1; depth < MinimumPoolDepth; depth++)
		{
			if (!IsWater(x, y + depth))
				return false;
		}
		return true;
	}
}
