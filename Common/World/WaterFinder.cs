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

	public static void Reset()
	{
		cache.Clear();
		honey = null;
		unreachableHoney.Clear();
	}

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
		var best = ScanShore(anchor, anchor.X - SearchHalfWidth, anchor.X + SearchHalfWidth) ?? OceanShore(anchor);
		cache[anchor] = (Main.GameUpdateCount, best);
		return best;
	}

	static bool PillarBetween(int fromX, int toX)
	{
		foreach (var npc in Main.npc)
		{
			if (!npc.active || !System.Array.Exists(Bosses.BossRituals.Pillars, type => type == npc.type))
				continue;
			int x = (int)(npc.Center.X / 16f);
			if (x > System.Math.Min(fromX, toX) && x < System.Math.Max(fromX, toX))
				return true;
		}
		return false;
	}

	const int WorldEdgeMargin = 45;
	const int OceanWidth = 380;

	static Point? OceanShore(Point anchor)
	{
		var left = PillarBetween(anchor.X, OceanWidth) ? null : ScanShore(anchor, WorldEdgeMargin, OceanWidth);
		var right = PillarBetween(anchor.X, Main.maxTilesX - OceanWidth) ? null : ScanShore(anchor, Main.maxTilesX - OceanWidth, Main.maxTilesX - WorldEdgeMargin);
		if (left is not Point l)
			return right;
		if (right is not Point r)
			return left;
		return WorldScan.DistanceSq(anchor, l) <= WorldScan.DistanceSq(anchor, r) ? left : right;
	}

	static Point? ScanShore(Point anchor, int left, int right)
	{
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = System.Math.Max(left, WorldEdgeMargin); x <= System.Math.Min(right, Main.maxTilesX - WorldEdgeMargin); x++)
		{
			for (int y = anchor.Y - SearchRowsAbove; y <= anchor.Y + SearchRowsBelow; y++)
			{
				if (!IsWaterSurface(x, y))
					continue;
				var surface = new Point(x, y - 1);
				if (Bosses.BossRituals.InPillarZone(surface.ToWorldCoordinates()))
					continue;
				long distance = WorldScan.DistanceSq(anchor, surface);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = surface;
				}
			}
		}
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

	public static Point? NearestLavaSurface(Point center, int radius, int minY = 0, System.Func<Point, bool> accept = null) => NearestLiquidSurface(center, radius, LiquidID.Lava, minY, accept);

	public static Point? NearestWaterSurface(Point center, int radius) => NearestLiquidSurface(center, radius, LiquidID.Water);

	static Point? NearestLiquidSurface(Point center, int radius, int liquidType, int minY = 0, System.Func<Point, bool> accept = null)
	{
		Point? best = null;
		long bestDistance = long.MaxValue;
		for (int x = center.X - radius; x <= center.X + radius; x++)
		{
			for (int y = center.Y - radius; y <= center.Y + radius; y++)
			{
				if (y < minY || !IsLiquid(x, y, liquidType) || IsLiquid(x, y - 1, liquidType) || TileQuery.IsSolid(x, y - 1))
					continue;
				var surface = new Point(x, y);
				if (accept != null && !accept(surface))
					continue;
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

	static bool IsHoney(int x, int y) => IsLiquid(x, y, LiquidID.Honey);

	public static bool IsNearHoney(Point center)
	{
		for (int x = center.X - AdjacentRange; x <= center.X + AdjacentRange; x++)
		{
			for (int y = center.Y - AdjacentRange; y <= center.Y + AdjacentRange; y++)
			{
				if (IsHoney(x, y))
					return true;
			}
		}
		return false;
	}

	const uint HoneyCacheTicks = 3600 * 10;
	const int HoneySpanMargin = 60;
	static (uint tick, List<Point> surfaces)? honey;

	static readonly HashSet<Point> unreachableHoney = new();
	const int UnreachableHoneyRadius = 12;

	public static void MarkHoneyUnreachable(Point spot) => unreachableHoney.Add(spot);

	public static Point? NearestHoney(Point from)
	{
		if (honey is not { } cached || Main.GameUpdateCount - cached.tick > HoneyCacheTicks)
		{
			honey = cached = (Main.GameUpdateCount, ScanHoney());
			unreachableHoney.Clear();
		}
		Point? best = null;
		long bestDistance = long.MaxValue;
		foreach (var spot in cached.surfaces)
		{
			if (unreachableHoney.Any(bad => System.Math.Abs(bad.X - spot.X) <= UnreachableHoneyRadius && System.Math.Abs(bad.Y - spot.Y) <= UnreachableHoneyRadius))
				continue;
			long distance = DistanceSq(from, spot);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = spot;
			}
		}
		return best;
	}

	static long DistanceSq(Point a, Point b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);

	static List<Point> ScanHoney()
	{
		var found = new List<Point>();
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (Systems.BotWorldSystem.KnowledgeOf(faction).SpanOf(Landmark.Jungle) is not var (minX, maxX))
				continue;
			for (int x = System.Math.Max(minX - HoneySpanMargin, 10); x <= System.Math.Min(maxX + HoneySpanMargin, Main.maxTilesX - 10); x++)
			{
				for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
				{
					if (IsHoney(x, y) && !IsHoney(x, y - 1) && !TileQuery.IsSolid(x, y - 1))
						found.Add(new Point(x, y - 1));
				}
			}
			break;
		}
		return found;
	}

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
