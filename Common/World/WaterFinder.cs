using System.Collections.Generic;
using System.Linq;
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

	const int PondCandidates = 40;

	public static Point? NearestPond(Point center, int radius, int minimumSize)
	{
		var candidates = new List<Point>();
		for (int x = center.X - radius; x <= center.X + radius; x++)
		{
			for (int y = center.Y - radius; y <= center.Y + radius; y++)
			{
				if (WorldGen.InWorld(x, y, 5) && IsWater(x, y) && !IsWater(x, y - 1) && !TileQuery.IsSolid(x, y - 1))
					candidates.Add(new Point(x, y));
			}
		}
		return candidates.OrderBy(tile => WorldScan.DistanceSq(center, tile)).Take(PondCandidates)
			.Cast<Point?>().FirstOrDefault(tile => PondSize(tile.Value, minimumSize) >= minimumSize);
	}

	public static int PondSize(Point surface, int cap)
	{
		var seen = new HashSet<Point> { surface };
		var frontier = new Queue<Point>();
		frontier.Enqueue(surface);
		while (frontier.Count > 0 && seen.Count < cap)
		{
			var tile = frontier.Dequeue();
			foreach (var next in new[] { new Point(tile.X + 1, tile.Y), new Point(tile.X - 1, tile.Y), new Point(tile.X, tile.Y + 1), new Point(tile.X, tile.Y - 1) })
			{
				if (!seen.Contains(next) && WorldGen.InWorld(next.X, next.Y, 5) && IsWater(next.X, next.Y) && seen.Add(next))
					frontier.Enqueue(next);
			}
		}
		return seen.Count;
	}

	public static Point? NearestLavaSurface(Point center, int radius) => NearestLiquidSurface(center, radius, LiquidID.Lava);

	public static Point? NearestWaterSurface(Point center, int radius) => NearestLiquidSurface(center, radius, LiquidID.Water);

	static Point? NearestLiquidSurface(Point center, int radius, int liquidType)
	{
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = center.X - radius; x <= center.X + radius; x++)
		{
			for (int y = center.Y - radius; y <= center.Y + radius; y++)
			{
				if (!IsLiquid(x, y, liquidType) || IsLiquid(x, y - 1, liquidType) || TileQuery.IsSolid(x, y - 1))
					continue;
				var surface = new Point(x, y);
				long distance = WorldScan.DistanceSq(center, surface);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = surface;
				}
			}
		}
		return best;
	}

	static bool IsWater(int x, int y) => IsLiquid(x, y, LiquidID.Water);

	static bool IsLiquid(int x, int y, int liquidType)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount > 0 && tile.LiquidType == liquidType;
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
