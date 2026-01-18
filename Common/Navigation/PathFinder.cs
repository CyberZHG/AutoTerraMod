using System;
using System.Collections.Generic;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Navigation;

public enum MoveKind
{
	Walk,
	Jump,
	Drop,
	Dig,
	Climb,
	Bridge,
	Pillar,
	Swim,
	RopeDown
}

public readonly record struct PathNode(Point Feet, MoveKind Arrival);

public sealed record PathResult(List<PathNode> Nodes, bool ReachesGoal)
{
	public static readonly PathResult None = new(new List<PathNode>(), false);
}

public readonly record struct NavigationAbilities(int PickPower, bool CanDig, bool CanBridge, bool CanPillar, bool CanRope = false, int MaxFall = PathFinder.MaxSafeFall, bool CanUnlock = false);

public static class PathFinder
{
	const int MaxExpandedNodes = 2000;
	const int MaxSearchRadius = 90;
	const int ExpansionBudgetPerTick = 3000;
	const float HeuristicWeight = 1.3f;
	const int LongTripTiles = 60;
	const int WaypointTiles = 50;
	const int WaypointRadius = 4;

	static readonly int[] Directions = { -1, 1 };
	static readonly Dictionary<Point, bool> hazardCache = new();
	static readonly Dictionary<Point, bool> diggableCache = new();
	static readonly Dictionary<Point, bool> bodyClearCache = new();
	static readonly Dictionary<Point, bool> supportedCache = new();
	static uint budgetTick;
	static int budgetUsed;

	public static bool HasBudget
	{
		get
		{
			if (budgetTick != Main.GameUpdateCount)
			{
				budgetTick = Main.GameUpdateCount;
				budgetUsed = 0;
			}
			return budgetUsed < ExpansionBudgetPerTick;
		}
	}
	const int MaxJumpRise = 5;
	const int MaxRopeClimb = 60;
	const int MaxRopeDescent = 120;
	const float RopeClimbBaseCost = 4f;
	const float RopeClimbCostPerTile = 1.2f;
	const int MaxJumpReach = 3;
	public const int MaxSafeFall = 24;
	const float DigCostPerTile = 3f;
	const float BridgeCost = 4f;
	const float HazardPenalty = 25f;
	const float PillarCost = 8f;
	const float PillarCostPerTile = 3f;
	const int MaxPillar = 30;
	const float SwimCost = 2f;
	const int SwimmableLiquid = 64;
	static readonly Point[] SwimOffsets = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1, -1), new(1, -1) };

	readonly record struct Edge(Point To, MoveKind Kind, float Cost);

	public static Point FeetOf(Entity body) => new((int)Math.Round(body.Center.X / 16f) - 1, (int)((body.position.Y + body.height - 1) / 16f));

	public static float CenterXOf(int nodeX) => (nodeX + 1) * 16f;

	public static PathResult Find(Point start, Point goal, int goalRadius, NavigationAbilities abilities, ISet<(Point from, Point to)> forbiddenSteps = null, int searchMultiplier = 1)
	{
		int maxExpanded = MaxExpandedNodes * searchMultiplier;
		using var measurement = Systems.Perf.Measure(Systems.PerfSection.Pathfinding);
		int searchRadius = MaxSearchRadius;
		if (InsideMaze(goal))
		{
			maxExpanded *= MazeSearchMultiplier;
			searchRadius = MazeSearchRadius;
		}
		else if (Heuristic(start, goal) > LongTripTiles)
		{
			goal = WaypointToward(start, goal);
			goalRadius = Math.Max(goalRadius, WaypointRadius);
		}
		canUnlock = abilities.CanUnlock;
		hazardCache.Clear();
		diggableCache.Clear();
		bodyClearCache.Clear();
		supportedCache.Clear();
		var open = new PriorityQueue<Point, float>();
		var cameFrom = new Dictionary<Point, (Point parent, MoveKind kind)>();
		var costSoFar = new Dictionary<Point, float> { [start] = 0f };
		var edges = new List<Edge>(32);
		open.Enqueue(start, HeuristicWeight * Heuristic(start, goal));
		var best = start;
		float bestHeuristic = Heuristic(start, goal);
		int expanded = 0;

		while (open.TryDequeue(out var current, out _) && expanded++ < maxExpanded)
		{
			budgetUsed++;
			Systems.Perf.CountPathNodes(1);
			if (IsGoal(current, goal, goalRadius))
				return new PathResult(Reconstruct(cameFrom, start, current), true);
			float h = Heuristic(current, goal);
			if (h < bestHeuristic)
			{
				bestHeuristic = h;
				best = current;
			}
			edges.Clear();
			Expand(current, abilities, edges);
			foreach (var edge in edges)
			{
				if (Math.Abs(edge.To.X - start.X) > searchRadius || Math.Abs(edge.To.Y - start.Y) > searchRadius)
					continue;
				if (forbiddenSteps != null && forbiddenSteps.Contains((current, edge.To)))
					continue;
				float cost = costSoFar[current] + edge.Cost + (IsHazardousStand(edge.To) ? HazardPenalty : 0f);
				if (costSoFar.TryGetValue(edge.To, out float known) && known <= cost)
					continue;
				costSoFar[edge.To] = cost;
				cameFrom[edge.To] = (current, edge.Kind);
				open.Enqueue(edge.To, cost + HeuristicWeight * Heuristic(edge.To, goal));
			}
		}
		return best == start ? PathResult.None : new PathResult(Reconstruct(cameFrom, start, best), false);
	}

	const int MazeSearchMultiplier = 6;
	const int MazeSearchRadius = 200;

	static bool InsideMaze(Point tile) => WorldGen.InWorld(tile.X, tile.Y) && Main.tile[tile.X, tile.Y].WallType == WallID.LihzahrdBrickUnsafe;

	static bool IsGoal(Point node, Point goal, int radius) =>
		Math.Abs(node.X - goal.X) <= radius && Math.Abs(node.Y - goal.Y) <= radius + 2;

	static float Heuristic(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

	static Point WaypointToward(Point start, Point goal)
	{
		float distance = Heuristic(start, goal);
		float fraction = WaypointTiles / distance;
		return new Point(start.X + (int)((goal.X - start.X) * fraction), start.Y + (int)((goal.Y - start.Y) * fraction));
	}

	static List<PathNode> Reconstruct(Dictionary<Point, (Point parent, MoveKind kind)> cameFrom, Point start, Point end)
	{
		var nodes = new List<PathNode>();
		for (var node = end; node != start; node = cameFrom[node].parent)
			nodes.Add(new PathNode(node, cameFrom[node].kind));
		nodes.Reverse();
		return nodes;
	}

	static void Expand(Point node, NavigationAbilities abilities, List<Edge> edges)
	{
		int x = node.X;
		int y = node.Y;
		bool onRope = RopeColumn(x, y) >= 0 || RopeColumn(x, y - 1) >= 0;
		bool supported = Supported(x, y);

		foreach (int dir in Directions)
		{
			int nx = x + dir;
			if (BodyClear(nx, y))
			{
				if (Supported(nx, y))
					edges.Add(new Edge(new Point(nx, y), MoveKind.Walk, 1f));
				else
				{
					if (DropLanding(nx, y, abilities.MaxFall) is Point landing)
						edges.Add(new Edge(landing, MoveKind.Drop, 1f + (landing.Y - y) * 0.3f));
					else if (abilities.CanRope && abilities.CanBridge && supported && DropLanding(nx, y, MaxRopeDescent) is Point bottom)
						edges.Add(new Edge(bottom, MoveKind.RopeDown, RopeClimbBaseCost + (bottom.Y - y) * RopeClimbCostPerTile));
					if (abilities.CanBridge && supported && !Main.tile[BridgeColumn(x, nx), y + 1].HasTile)
						edges.Add(new Edge(new Point(nx, y), MoveKind.Bridge, BridgeCost));
				}
			}
			else if (BodyClear(nx, y - 1) && RowClear(x, y - 3) && Supported(nx, y - 1))
			{
				edges.Add(new Edge(new Point(nx, y - 1), MoveKind.Walk, 1.5f));
			}
			if (abilities.CanDig && supported)
			{
				TryDig(edges, abilities, node, new Point(nx, y));
				TryDig(edges, abilities, node, new Point(nx, y - 1));
				TryDig(edges, abilities, node, new Point(nx, y + 1));
			}
		}

		if (supported)
			AddJumpEdges(x, y, edges);

		if (InWater(x, y))
		{
			foreach (var offset in SwimOffsets)
			{
				var swimTo = new Point(x + offset.X, y + offset.Y);
				if (BodyClear(swimTo.X, swimTo.Y))
					edges.Add(new Edge(swimTo, MoveKind.Swim, SwimCost));
			}
		}

		if (abilities.CanPillar && supported)
			AddPillarEdges(x, y, edges);

		if (IsPlatformBelow(x, y) && DropLanding(x, y + 1, abilities.MaxFall) is Point belowPlatform)
			edges.Add(new Edge(belowPlatform, MoveKind.Drop, 1f + (belowPlatform.Y - y) * 0.3f));

		if (abilities.CanDig && supported)
			TryDig(edges, abilities, node, new Point(x, y + 1));

		if (abilities.CanRope && (supported || onRope))
			AddRopeClimbEdges(x, y, edges);

		if (onRope || RopeColumn(x, y - 2) >= 0)
		{
			if (BodyClear(x, y - 1))
				edges.Add(new Edge(new Point(x, y - 1), MoveKind.Climb, 1f));
			if (BodyClear(x, y + 1) && (RopeColumn(x, y + 1) >= 0 || Supported(x, y + 1)))
				edges.Add(new Edge(new Point(x, y + 1), MoveKind.Climb, 1f));
		}
	}

	static void AddPillarEdges(int x, int y, List<Edge> edges)
	{
		for (int rise = 1; rise <= MaxPillar && BodyClear(x, y - rise); rise++)
		{
			int top = y - rise;
			bool exit = rise == 1 || (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Pillar, PillarCost + rise * PillarCostPerTile));
		}
	}

	static void AddRopeClimbEdges(int x, int y, List<Edge> edges)
	{
		for (int rise = 1; rise <= MaxRopeClimb && BodyClear(x, y - rise); rise++)
		{
			if (rise <= MaxJumpRise)
				continue;
			int top = y - rise;
			bool exit = (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Climb, RopeClimbBaseCost + rise * RopeClimbCostPerTile));
		}
	}

	public static int BridgeColumn(int fromX, int toX) => toX > fromX ? toX + 1 : toX;

	public static int RopeColumn(int x, int y)
	{
		if (IsRope(x, y))
			return x;
		return IsRope(x + 1, y) ? x + 1 : -1;
	}

	static void AddJumpEdges(int x, int y, List<Edge> edges)
	{
		int headroom = 0;
		while (headroom < MaxJumpRise && RowClear(x, y - 3 - headroom))
			headroom++;
		for (int dx = -MaxJumpReach; dx <= MaxJumpReach; dx++)
		{
			int nx = x + dx;
			for (int rise = dx == 0 ? 2 : 1; rise <= headroom; rise++)
			{
				int ny = y - rise;
				if (!BodyClear(nx, ny) || !Supported(nx, ny) || !SpanClear(x, nx, ny))
					continue;
				edges.Add(new Edge(new Point(nx, ny), MoveKind.Jump, 2f + rise * 0.8f + Math.Abs(dx) * 0.5f));
				break;
			}
		}
	}

	public static IEnumerable<Point> CellsToClear(Point from, Point to)
	{
		int top = Math.Min(from.Y, to.Y) - 2;
		for (int column = to.X; column <= to.X + 1; column++)
		{
			for (int row = top; row <= to.Y; row++)
				yield return new Point(column, row);
		}
		for (int row = to.Y - 2; row <= from.Y - 3; row++)
		{
			yield return new Point(from.X, row);
			yield return new Point(from.X + 1, row);
		}
	}

	static void TryDig(List<Edge> edges, NavigationAbilities abilities, Point from, Point to)
	{
		if (!Supported(to.X, to.Y) && to.Y <= from.Y)
			return;
		int solids = 0;
		foreach (var cell in CellsToClear(from, to))
		{
			if (!CountDiggable(cell.X, cell.Y, abilities, ref solids))
				return;
		}
		if (solids == 0 || Hazardous(to.X, to.Y) || Hazardous(to.X + 1, to.Y))
			return;
		var landing = Supported(to.X, to.Y) ? to : FallAfterDigging(to);
		if (landing is Point destination)
			edges.Add(new Edge(destination, MoveKind.Dig, 1f + solids * DigCostPerTile + (destination.Y - to.Y) * 0.3f));
	}

	static bool CountDiggable(int x, int y, NavigationAbilities abilities, ref int solids)
	{
		if (!TileQuery.IsSolid(x, y) || IsPassableDoor(x, y))
			return true;
		if (!Diggable(x, y, abilities))
			return false;
		solids++;
		return true;
	}

	static Point? DropLanding(int x, int y, int maxFall)
	{
		for (int fall = 0; fall <= maxFall; fall++)
		{
			if (!BodyClear(x, y + fall))
				return null;
			if (Supported(x, y + fall) || RopeColumn(x, y + fall) >= 0 || InWater(x, y + fall))
				return new Point(x, y + fall);
		}
		return null;
	}

	static Point? FallAfterDigging(Point dug)
	{
		for (int fall = 1; fall <= MaxSafeFall; fall++)
		{
			int y = dug.Y + fall;
			if (!RowClear(dug.X, y) || Hazardous(dug.X, y) || Hazardous(dug.X + 1, y))
				return null;
			if (Supported(dug.X, y) || RopeColumn(dug.X, y) >= 0 || InWater(dug.X, y))
				return new Point(dug.X, y);
		}
		return null;
	}

	static bool SpanClear(int fromX, int toX, int feetY)
	{
		int step = Math.Sign(toX - fromX);
		for (int cx = fromX; cx != toX; cx += step)
		{
			if (!BodyClear(cx, feetY))
				return false;
		}
		return true;
	}

	static bool canUnlock;

	static bool IsPassableDoor(int x, int y) => IsClosedDoor(x, y) && (canUnlock || !WorldGen.IsLockedDoor(x, y));

	static bool Clear(int x, int y) => WorldGen.InWorld(x, y, 5) && (!TileQuery.IsSolid(x, y) || IsPassableDoor(x, y));

	static bool RowClear(int x, int y) => Clear(x, y) && Clear(x + 1, y);

	public static bool IsClosedDoor(int x, int y) => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.ClosedDoor;

	static bool BodyClear(int x, int feetY)
	{
		var key = new Point(x, feetY);
		if (!bodyClearCache.TryGetValue(key, out bool clear))
			bodyClearCache[key] = clear = RowClear(x, feetY) && RowClear(x, feetY - 1) && RowClear(x, feetY - 2) && !Hazardous(x, feetY) && !Hazardous(x + 1, feetY);
		return clear;
	}

	public static bool IsSupportedNow(int x, int feetY) => TileQuery.CanStandOn(x, feetY + 1) || TileQuery.CanStandOn(x + 1, feetY + 1);

	static bool Supported(int x, int feetY)
	{
		var key = new Point(x, feetY);
		if (!supportedCache.TryGetValue(key, out bool supported))
			supportedCache[key] = supported = TileQuery.CanStandOn(x, feetY + 1) || TileQuery.CanStandOn(x + 1, feetY + 1);
		return supported;
	}

	static bool IsPlatformBelow(int x, int feetY) =>
		(TileQuery.IsPlatform(x, feetY + 1) || TileQuery.IsPlatform(x + 1, feetY + 1))
		&& !TileQuery.IsSolid(x, feetY + 1) && !TileQuery.IsSolid(x + 1, feetY + 1);

	public static bool InWater(int x, int feetY) => IsSwimmable(x, feetY - 1) || IsSwimmable(x + 1, feetY - 1);

	static bool IsSwimmable(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 5))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount >= SwimmableLiquid && tile.LiquidType != LiquidID.Lava;
	}

	static bool Hazardous(int x, int feetY) =>
		TileQuery.IsDangerousLiquid(x, feetY) || TileQuery.IsDangerousLiquid(x, feetY - 1);

	public static bool NearHurtfulTile(int x, int feetY)
	{
		for (int cx = x - 1; cx <= x + 2; cx++)
		{
			for (int cy = feetY - 3; cy <= feetY + 1; cy++)
			{
				if (WorldGen.InWorld(cx, cy, 5) && Main.tile[cx, cy].HasTile && IsHurtful(Main.tile[cx, cy].TileType))
					return true;
			}
		}
		return false;
	}

	static bool IsHurtful(int tileType) =>
		TileID.Sets.TouchDamageImmediate[tileType] > 0 || TileID.Sets.TouchDamageHot[tileType] || TileID.Sets.TouchDamageBleeding[tileType];

	static bool IsRope(int x, int y) => WorldGen.InWorld(x, y, 5) && Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType];

	static bool IsHazardousStand(Point feet)
	{
		if (!hazardCache.TryGetValue(feet, out bool hazardous))
			hazardCache[feet] = hazardous = NearHurtfulTile(feet.X, feet.Y);
		return hazardous;
	}

	static bool Diggable(int x, int y, NavigationAbilities abilities)
	{
		var cell = new Point(x, y);
		if (!diggableCache.TryGetValue(cell, out bool diggable))
			diggableCache[cell] = diggable = ComputeDiggable(x, y, abilities);
		return diggable;
	}

	static bool ComputeDiggable(int x, int y, NavigationAbilities abilities)
	{
		if (!TileQuery.IsSolid(x, y))
			return false;
		int tileType = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[tileType]
			&& !HouseInspector.IsProtected(x, y)
			&& !TileQuery.SupportsShrine(x, y)
			&& PickRequirement.MinimumPick(tileType, x, y) <= abilities.PickPower
			&& WorldGen.CanKillTile(x, y);
	}
}
