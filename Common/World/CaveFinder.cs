using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.World;

public static class CaveFinder
{
	const int RegionSize = 12;
	const int MinimumDistance = 10;
	const int PocketRadius = 1;

	public static Point RegionOf(Point tile) => new(tile.X / RegionSize, tile.Y / RegionSize);

	public static Point? NearestUnvisitedCave(Point origin, ISet<Point> visitedRegions, int direction = 0, int deepest = int.MaxValue)
	{
		int top = direction == 0 ? Math.Max(origin.Y, (int)Main.worldSurface) : Math.Max(origin.Y - WorldScan.ViewHalfHeight, (int)Main.worldSurface);
		int bottom = Math.Min(Math.Min(origin.Y + WorldScan.ViewHalfHeight, Main.maxTilesY - 210), deepest);
		int left = direction > 0 ? origin.X + MinimumDistance : origin.X - WorldScan.ViewHalfWidth;
		int right = direction < 0 ? origin.X - MinimumDistance : origin.X + WorldScan.ViewHalfWidth;
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int y = top; y <= bottom; y += 2)
		{
			for (int x = left; x <= right; x += 2)
			{
				var candidate = new Point(x, y);
				long distance = WorldScan.DistanceSq(origin, candidate);
				if (distance < MinimumDistance * MinimumDistance || distance >= bestDistance)
					continue;
				if (visitedRegions.Contains(RegionOf(candidate)) || !IsOpenPocket(x, y))
					continue;
				best = candidate;
				bestDistance = distance;
			}
		}
		return best;
	}

	static bool IsOpenPocket(int x, int y)
	{
		for (int dx = -PocketRadius; dx <= PocketRadius; dx++)
		{
			for (int dy = -PocketRadius; dy <= PocketRadius; dy++)
			{
				if (!WorldGen.InWorld(x + dx, y + dy, 5) || TileQuery.IsSolid(x + dx, y + dy) || Main.tile[x + dx, y + dy].LiquidAmount > 0)
					return false;
			}
		}
		return Main.tile[x, y].WallType != 0 || y > Main.worldSurface;
	}
}
