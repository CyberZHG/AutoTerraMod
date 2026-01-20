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
			if (TileID.Sets.Ore[tileType] || IsTreasure(tileType) || IsForage(x, y, tileType))
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
			if (TileID.Sets.Ore[tileType] || IsTreasure(tileType) || TileQuery.IsTreeBase(x, y) || IsBossShrine(tileType) || IsForage(x, y, tileType) || LootableFurniture.Contains(tileType))
				knowledge.Remember(new Point(x, y), tileType);
			if (Landmarks.Classify(x, y, tileType) is Landmark landmark)
				knowledge.RememberLandmark(landmark, new Point(x, y));
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

	static readonly HashSet<int> LootableFurniture = new() { TileID.Hellforge };

	public static bool IsBossShrine(int tileType) => tileType is TileID.DemonAltar or TileID.ShadowOrbs or TileID.Larva or TileID.PlanteraBulb or TileID.LihzahrdAltar;

	public static readonly Dictionary<int, (int tileType, int frameX)> ForageTiles = new()
	{
		[ItemID.Mushroom] = (TileID.Plants, 144),
		[ItemID.VileMushroom] = (TileID.CorruptPlants, 144),
		[ItemID.ViciousMushroom] = (TileID.CrimsonPlants, 270)
	};

	public static bool IsForage(int x, int y, int tileType)
	{
		foreach (var (forageTile, frameX) in ForageTiles.Values)
		{
			if (tileType == forageTile && Main.tile[x, y].TileFrameX == frameX)
				return true;
		}
		return false;
	}

	public static long DistanceSq(Point a, Point b)
	{
		long dx = a.X - b.X;
		long dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}
}
