using System;
using System.Collections.Generic;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class WorldScan
{
	public const int ViewHalfWidth = 50;
	public const int ViewHalfHeight = 30;

	public static Point? NearestVisibleTile(Point origin, ISet<int> tileTypes, Func<int, int, bool> accept, ISet<Point> excluded, FactionKnowledge knowledge)
	{
		using var _ = Systems.Perf.Measure(Systems.PerfSection.WorldScans);
		Point? best = null;
		long bestDistance = long.MaxValue;
		ForEachVisibleTile(origin, (x, y, tileType) =>
		{
			if (TileID.Sets.Ore[tileType] || IsTreasure(tileType))
				knowledge?.Remember(new Point(x, y), tileType);
			if (!tileTypes.Contains(tileType) || !accept(x, y))
				return;
			var candidate = new Point(x, y);
			if (excluded.Contains(candidate))
				return;
			long distance = DistanceSq(origin, candidate);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = candidate;
			}
		});
		return best;
	}

	public static void Observe(Point origin, FactionKnowledge knowledge)
	{
		ForEachVisibleTile(origin, (x, y, tileType) =>
		{
			if (TileID.Sets.Ore[tileType] || IsTreasure(tileType) || TileQuery.IsTreeBase(x, y))
				knowledge.Remember(new Point(x, y), tileType);
		});
	}

	static void ForEachVisibleTile(Point origin, Action<int, int, int> visit)
	{
		int left = Math.Max(origin.X - ViewHalfWidth, 5);
		int right = Math.Min(origin.X + ViewHalfWidth, Main.maxTilesX - 5);
		int top = Math.Max(origin.Y - ViewHalfHeight, 5);
		int bottom = Math.Min(origin.Y + ViewHalfHeight, Main.maxTilesY - 5);
		for (int x = left; x <= right; x++)
		{
			for (int y = top; y <= bottom; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && TileQuery.IsExposed(x, y))
					visit(x, y, tile.TileType);
			}
		}
	}

	public static readonly HashSet<int> TreasureTiles = new() { TileID.Heart, TileID.LifeFruit };

	public static bool IsTreasure(int tileType) => TreasureTiles.Contains(tileType);

	public static long DistanceSq(Point a, Point b)
	{
		long dx = a.X - b.X;
		long dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}
}
