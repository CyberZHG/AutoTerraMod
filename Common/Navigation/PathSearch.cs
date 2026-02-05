using System;
using System.Collections.Generic;
using System.Diagnostics;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Navigation;

public sealed class PathSearch
{
	const float HeuristicWeight = 1.3f;
	const int MaxJumpRise = 5;
	const int MaxRopeClimb = 60;
	const int MaxRopeDescent = 120;
	const float RopeClimbBaseCost = 4f;
	const float RopeClimbCostPerTile = 1.2f;
	const int MaxJumpReach = 3;
	const float DigCostPerTile = 3f;
	const float BridgeCost = 4f;
	const float HazardPenalty = 60f;
	const float LooseSandPenalty = 15f;
	const float PillarCost = 8f;
	const float PillarCostPerTile = 3f;
	const int MaxPillar = 30;
	const float SwimCost = 2f;
	const int ClockCheckInterval = 16;

	static readonly int[] Directions = { -1, 1 };
	static readonly Point[] SwimOffsets = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1, -1), new(1, -1) };

	readonly record struct Edge(Point To, MoveKind Kind, float Cost);

	readonly Point start;
	readonly NavigationAbilities abilities;
	readonly Func<Point, bool> isGoal;
	readonly Func<Point, float> heuristic;
	readonly ISet<(Point from, Point to)> forbiddenSteps;
	readonly int maxExpanded;
	readonly int searchRadius;

	readonly PriorityQueue<Point, float> open = new();
	readonly Dictionary<Point, (Point parent, MoveKind kind)> cameFrom = new();
	readonly Dictionary<Point, float> costSoFar = new();
	readonly List<Edge> edges = new(32);
	readonly Dictionary<Point, bool> hazardCache = new();
	readonly Dictionary<Point, bool> diggableCache = new();
	readonly Dictionary<Point, bool> bodyClearCache = new();
	readonly Dictionary<Point, bool> supportedCache = new();

	Point best;
	float bestHeuristic;
	int expanded;

	public PathSearch(Point start, NavigationAbilities abilities, Func<Point, bool> isGoal, Func<Point, float> heuristic, ISet<(Point from, Point to)> forbiddenSteps, int maxExpanded, int searchRadius)
	{
		this.start = start;
		this.abilities = abilities;
		this.isGoal = isGoal;
		this.heuristic = heuristic;
		this.forbiddenSteps = forbiddenSteps == null ? null : new HashSet<(Point, Point)>(forbiddenSteps);
		this.maxExpanded = maxExpanded;
		this.searchRadius = searchRadius;
		costSoFar[start] = 0f;
		open.Enqueue(start, HeuristicWeight * heuristic(start));
		best = start;
		bestHeuristic = heuristic(start);
	}

	public PathResult Result { get; private set; }
	public bool IsDone => Result != null;
	public Point Start => start;

	public PathResult RunToEnd()
	{
		while (!IsDone)
			Step(long.MaxValue);
		return Result;
	}

	public bool Step(long deadline)
	{
		if (IsDone)
			return true;
		using var measurement = Systems.Perf.Measure(Systems.PerfSection.Pathfinding);
		int sinceClock = 0;
		while (true)
		{
			if (expanded >= maxExpanded || !open.TryDequeue(out var current, out _))
			{
				Result = best == start ? PathResult.None : new PathResult(Reconstruct(best), false);
				return true;
			}
			expanded++;
			Systems.Perf.CountPathNodes(1);
			if (isGoal(current))
			{
				Result = new PathResult(Reconstruct(current), true);
				return true;
			}
			Visit(current);
			if (++sinceClock >= ClockCheckInterval)
			{
				sinceClock = 0;
				if (Stopwatch.GetTimestamp() >= deadline)
					return false;
			}
		}
	}

	void Visit(Point current)
	{
		float h = heuristic(current);
		if (h < bestHeuristic)
		{
			bestHeuristic = h;
			best = current;
		}
		edges.Clear();
		Expand(current, cameFrom.TryGetValue(current, out var arrival) && arrival.kind == MoveKind.Bridge);
		float here = costSoFar[current];
		foreach (var edge in edges)
		{
			if (Math.Abs(edge.To.X - start.X) > searchRadius || Math.Abs(edge.To.Y - start.Y) > searchRadius)
				continue;
			if (forbiddenSteps != null && forbiddenSteps.Contains((current, edge.To)))
				continue;
			float cost = here + edge.Cost + (IsHazardousStand(edge.To) ? HazardPenalty : 0f);
			if (costSoFar.TryGetValue(edge.To, out float known) && known <= cost)
				continue;
			costSoFar[edge.To] = cost;
			cameFrom[edge.To] = (current, edge.Kind);
			open.Enqueue(edge.To, cost + HeuristicWeight * heuristic(edge.To));
		}
	}

	List<PathNode> Reconstruct(Point end)
	{
		var nodes = new List<PathNode>();
		for (var node = end; node != start; node = cameFrom[node].parent)
			nodes.Add(new PathNode(node, cameFrom[node].kind));
		nodes.Reverse();
		return nodes;
	}

	void Expand(Point node, bool standsOnBridge)
	{
		int x = node.X;
		int y = node.Y;
		bool onRope = PathFinder.RopeColumn(x, y) >= 0 || PathFinder.RopeColumn(x, y - 1) >= 0;
		bool supported = standsOnBridge || Supported(x, y);
		bool anchored = supported || onRope;

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
					else if (abilities.CanRope && abilities.CanBridge && anchored && DropLanding(nx, y, MaxRopeDescent) is Point bottom)
						edges.Add(new Edge(bottom, MoveKind.RopeDown, RopeClimbBaseCost + (bottom.Y - y) * RopeClimbCostPerTile));
					if (abilities.CanBridge && anchored && !Main.tile[PathFinder.BridgeColumn(x, nx), y + 1].HasTile)
						edges.Add(new Edge(new Point(nx, y), MoveKind.Bridge, BridgeCost));
				}
			}
			else if (BodyClear(nx, y - 1) && RowClear(x, y - 3) && Supported(nx, y - 1))
			{
				edges.Add(new Edge(new Point(nx, y - 1), MoveKind.Walk, 1.5f));
			}
			if (abilities.CanDig && anchored)
			{
				TryDig(node, new Point(nx, y));
				TryDig(node, new Point(nx, y - 1));
				TryDig(node, new Point(nx, y + 1));
			}
		}

		if (supported)
			AddJumpEdges(x, y);

		if (PathFinder.InWater(x, y))
		{
			foreach (var offset in SwimOffsets)
			{
				var swimTo = new Point(x + offset.X, y + offset.Y);
				if (BodyClear(swimTo.X, swimTo.Y))
					edges.Add(new Edge(swimTo, MoveKind.Swim, SwimCost));
			}
		}

		if (abilities.CanPillar && supported)
			AddPillarEdges(x, y);

		if (IsPlatformBelow(x, y) && DropLanding(x, y + 1, abilities.MaxFall) is Point belowPlatform)
			edges.Add(new Edge(belowPlatform, MoveKind.Drop, 1f + (belowPlatform.Y - y) * 0.3f));

		if (abilities.CanDig && supported)
			TryDig(node, new Point(x, y + 1));

		if (abilities.CanRope && (supported || onRope))
			AddRopeClimbEdges(x, y);

		if (onRope || PathFinder.RopeColumn(x, y - 2) >= 0)
		{
			if (BodyClear(x, y - 1))
				edges.Add(new Edge(new Point(x, y - 1), MoveKind.Climb, 1f));
			if (BodyClear(x, y + 1) && (PathFinder.RopeColumn(x, y + 1) >= 0 || Supported(x, y + 1)))
				edges.Add(new Edge(new Point(x, y + 1), MoveKind.Climb, 1f));
		}
	}

	void AddPillarEdges(int x, int y)
	{
		for (int rise = 1; rise <= MaxPillar && BodyClear(x, y - rise); rise++)
		{
			int top = y - rise;
			bool exit = rise == 1 || (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Pillar, PillarCost + rise * PillarCostPerTile));
		}
	}

	void AddRopeClimbEdges(int x, int y)
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

	void AddJumpEdges(int x, int y)
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

	void TryDig(Point from, Point to)
	{
		if (!Supported(to.X, to.Y) && to.Y <= from.Y)
			return;
		int solids = 0;
		bool loosensSand = false;
		int top = Math.Min(from.Y, to.Y) - 2;
		for (int column = to.X; column <= to.X + 1; column++)
		{
			for (int row = top; row <= to.Y; row++)
			{
				if (!CountDiggable(column, row, ref solids, ref loosensSand))
					return;
			}
		}
		for (int row = to.Y - 2; row <= from.Y - 3; row++)
		{
			if (!CountDiggable(from.X, row, ref solids, ref loosensSand) || !CountDiggable(from.X + 1, row, ref solids, ref loosensSand))
				return;
		}
		if (loosensSand && to.Y < from.Y)
			return;
		if (solids == 0 || Hazardous(to.X, to.Y) || Hazardous(to.X + 1, to.Y))
			return;
		var landing = Supported(to.X, to.Y) ? to : FallAfterDigging(to);
		if (landing is Point destination)
			edges.Add(new Edge(destination, MoveKind.Dig, 1f + solids * DigCostPerTile + (destination.Y - to.Y) * 0.3f + (loosensSand ? LooseSandPenalty : 0f)));
	}

	bool CountDiggable(int x, int y, ref int solids, ref bool loosensSand)
	{
		if (!TileQuery.IsSolid(x, y) || IsPassableDoor(x, y))
			return true;
		if (!Diggable(x, y))
			return false;
		solids++;
		loosensSand |= PathFinder.UnderFallingBlock(x, y);
		return true;
	}

	Point? DropLanding(int x, int y, int maxFall)
	{
		for (int fall = 0; fall <= maxFall; fall++)
		{
			if (!BodyClear(x, y + fall))
				return null;
			if (Supported(x, y + fall) || PathFinder.RopeColumn(x, y + fall) >= 0 || PathFinder.InWater(x, y + fall))
				return new Point(x, y + fall);
		}
		return null;
	}

	Point? FallAfterDigging(Point dug)
	{
		for (int fall = 1; fall <= PathFinder.MaxSafeFall; fall++)
		{
			int y = dug.Y + fall;
			if (!RowClear(dug.X, y) || Hazardous(dug.X, y) || Hazardous(dug.X + 1, y))
				return null;
			if (Supported(dug.X, y) || PathFinder.RopeColumn(dug.X, y) >= 0 || PathFinder.InWater(dug.X, y))
				return new Point(dug.X, y);
		}
		return null;
	}

	bool SpanClear(int fromX, int toX, int feetY)
	{
		int step = Math.Sign(toX - fromX);
		for (int cx = fromX; cx != toX; cx += step)
		{
			if (!BodyClear(cx, feetY))
				return false;
		}
		return true;
	}

	bool IsPassableDoor(int x, int y) => PathFinder.IsClosedDoor(x, y) && (abilities.CanUnlock || !WorldGen.IsLockedDoor(x, y));

	bool Clear(int x, int y) => WorldGen.InWorld(x, y, 5) && (!TileQuery.IsSolid(x, y) || IsPassableDoor(x, y));

	bool RowClear(int x, int y) => Clear(x, y) && Clear(x + 1, y);

	bool BodyClear(int x, int feetY)
	{
		var key = new Point(x, feetY);
		if (!bodyClearCache.TryGetValue(key, out bool clear))
			bodyClearCache[key] = clear = RowClear(x, feetY) && RowClear(x, feetY - 1) && RowClear(x, feetY - 2) && !Hazardous(x, feetY) && !Hazardous(x + 1, feetY);
		return clear;
	}

	bool Supported(int x, int feetY)
	{
		var key = new Point(x, feetY);
		if (!supportedCache.TryGetValue(key, out bool supported))
			supportedCache[key] = supported = PathFinder.IsSupportedNow(x, feetY);
		return supported;
	}

	static bool IsPlatformBelow(int x, int feetY) =>
		(TileQuery.IsPlatform(x, feetY + 1) || TileQuery.IsPlatform(x + 1, feetY + 1))
		&& !TileQuery.IsSolid(x, feetY + 1) && !TileQuery.IsSolid(x + 1, feetY + 1);

	static bool Hazardous(int x, int feetY) =>
		TileQuery.IsDangerousLiquid(x, feetY) || TileQuery.IsDangerousLiquid(x, feetY - 1);

	bool IsHazardousStand(Point feet)
	{
		if (!hazardCache.TryGetValue(feet, out bool hazardous))
			hazardCache[feet] = hazardous = PathFinder.NearHurtfulTile(feet.X, feet.Y) || PathFinder.OnPressurePlate(feet.X, feet.Y);
		return hazardous;
	}

	bool Diggable(int x, int y)
	{
		var cell = new Point(x, y);
		if (!diggableCache.TryGetValue(cell, out bool diggable))
			diggableCache[cell] = diggable = ComputeDiggable(x, y);
		return diggable;
	}

	bool ComputeDiggable(int x, int y)
	{
		int tileType = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[tileType]
			&& !HouseInspector.IsProtected(x, y)
			&& !TileQuery.SupportsShrine(x, y)
			&& PickRequirement.MinimumPick(tileType, x, y) <= abilities.PickPower
			&& WorldGen.CanKillTile(x, y);
	}
}
