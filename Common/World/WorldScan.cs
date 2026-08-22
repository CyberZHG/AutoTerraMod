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

	public static Point? NearestBuriedTile(Point origin, int radius, ISet<int> tileTypes, Func<int, int, bool> accept, ISet<Point> excluded)
	{
		using var _ = Systems.Perf.Measure(Systems.PerfSection.WorldScans);
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = Math.Max(origin.X - radius, 5); x <= Math.Min(origin.X + radius, Main.maxTilesX - 5); x++)
		{
			for (int y = Math.Max(origin.Y - radius, 5); y <= Math.Min(origin.Y + radius, Main.maxTilesY - 5); y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile || !tileTypes.Contains(tile.TileType))
					continue;
				var candidate = new Point(x, y);
				long distance = DistanceSq(origin, candidate);
				if (distance < bestDistance && !excluded.Contains(candidate) && accept(x, y))
				{
					bestDistance = distance;
					best = candidate;
				}
			}
		}
		return best;
	}

	public static void Observe(Point origin, FactionKnowledge knowledge)
	{
		knowledge.MarkExplored(origin);
		ForEachVisibleTile(origin, (x, y, tileType) =>
		{
			if (TileID.Sets.Ore[tileType] || IsTreasure(tileType) || TileQuery.IsTreeBase(x, y) || IsBossShrine(tileType) || IsForage(x, y, tileType) || LootableFurniture.Contains(tileType))
				knowledge.Remember(new Point(x, y), tileType);
			if (tileType == TileID.Tombstones)
				knowledge.Remember(new Point(x, y), tileType);
			if (tileType == TileID.DemonAltar)
				SharedAltar.Claim(x, y);
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

	public static readonly HashSet<int> LootableFurniture = new() { TileID.Hellforge };

	public static bool IsBossShrine(int tileType) => tileType is TileID.DemonAltar or TileID.ShadowOrbs or TileID.Larva or TileID.PlanteraBulb or TileID.LihzahrdAltar;

	public static readonly Dictionary<int, (int tileType, int frameX)> ForageTiles = new()
	{
		[ItemID.Mushroom] = (TileID.Plants, 144),
		[ItemID.VileMushroom] = (TileID.CorruptPlants, 144),
		[ItemID.ViciousMushroom] = (TileID.CrimsonPlants, 270)
	};

	public static readonly Dictionary<int, int> HerbStyles = new()
	{
		[ItemID.Daybloom] = 0,
		[ItemID.Moonglow] = 1,
		[ItemID.Blinkroot] = 2,
		[ItemID.Deathweed] = 3,
		[ItemID.Waterleaf] = 4,
		[ItemID.Fireblossom] = 5,
		[ItemID.Shiverthorn] = 6
	};

	public static bool IsHerb(int x, int y, int style) =>
		Main.tile[x, y].TileType is TileID.MatureHerbs or TileID.BloomingHerbs && Main.tile[x, y].TileFrameX / 18 == style;

	public static bool IsForage(int x, int y, int tileType)
	{
		if (tileType is TileID.MatureHerbs or TileID.BloomingHerbs)
			return true;
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
