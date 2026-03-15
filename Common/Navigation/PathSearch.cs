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

	sealed class Frontier
	{
		public readonly PriorityQueue<Point, float> Open = new();
		public readonly Dictionary<Point, (Point parent, MoveKind kind)> CameFrom = new();
		public readonly Dictionary<Point, float> CostSoFar = new();

		public void Clear()
		{
			Open.Clear();
			CameFrom.Clear();
			CostSoFar.Clear();
		}
	}

	static readonly Stack<Frontier> frontierPool = new();

	Frontier frontier = frontierPool.Count > 0 ? frontierPool.Pop() : new Frontier();
	PriorityQueue<Point, float> open => frontier.Open;
	Dictionary<Point, (Point parent, MoveKind kind)> cameFrom => frontier.CameFrom;
	Dictionary<Point, float> costSoFar => frontier.CostSoFar;
	readonly List<Edge> edges = new(32);
	const int ScanMargin = 130;
	const ushort BodyKnown = 1, BodyClearBit = 2, SupportKnown = 4, SupportBit = 8, HazardKnown = 16, HazardBit = 32, DigKnown = 64, DigBit = 128, HangKnown = 256, HangBit = 512;

	ushort[] cells;
	readonly int originX;
	readonly int originY;
	readonly int span;

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
		int reach = searchRadius + ScanMargin;
		span = reach * 2 + 1;
		originX = start.X - reach;
		originY = start.Y - reach;
		cells = System.Buffers.ArrayPool<ushort>.Shared.Rent(span * span);
		Array.Clear(cells, 0, span * span);
		costSoFar[start] = 0f;
		open.Enqueue(start, HeuristicWeight * heuristic(start));
		best = start;
		bestHeuristic = heuristic(start);
	}

	PathResult result;

	public PathResult Result
	{
		get => result;
		private set
		{
			result = value;
			ReleaseBuffers();
		}
	}

	public void Cancel()
	{
		if (cells == null)
			return;
		ReleaseBuffers();
		result ??= PathResult.None;
	}

	const int MaxPooledFrontiers = 8;

	void ReleaseBuffers()
	{
		if (cells != null)
			System.Buffers.ArrayPool<ushort>.Shared.Return(cells);
		cells = null;
		if (frontier == null)
			return;
		frontier.Clear();
		if (frontierPool.Count < MaxPooledFrontiers)
			frontierPool.Push(frontier);
		frontier = null;
	}

	int Cell(int x, int y)
	{
		int cx = x - originX;
		int cy = y - originY;
		return (uint)cx < (uint)span && (uint)cy < (uint)span ? cx + cy * span : -1;
	}

	bool Flag(int x, int y, ushort known, ushort bit, out int index)
	{
		index = Cell(x, y);
		return index >= 0 && (cells[index] & known) != 0;
	}

	bool Remember(int index, ushort known, ushort bit, bool value)
	{
		if (index >= 0)
			cells[index] |= (ushort)(known | (value ? bit : 0));
		return value;
	}
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
		bool arrived = cameFrom.TryGetValue(current, out var arrival);
		Expand(current, arrived && arrival.kind == MoveKind.Bridge, arrived && arrival.kind == MoveKind.HookHang);
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

	void Expand(Point node, bool standsOnBridge, bool hanging)
	{
		int x = node.X;
		int y = node.Y;
		bool onRope = PathFinder.RopeColumn(x, y) >= 0 || PathFinder.RopeColumn(x, y - 1) >= 0;
		bool supported = standsOnBridge || Supported(x, y);
		bool anchored = supported || onRope;
		if (hanging && !supported)
		{
			ExpandHang(x, y);
			return;
		}

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

		bool swimming = PathFinder.InWater(x, y);
		if (supported || onRope)
			AddJumpEdges(x, y, abilities.JumpRise);
		else if (swimming && !PathFinder.InWater(x, y - 1))
			AddJumpEdges(x, y, Math.Min(abilities.JumpRise, WaterJumpRise));

		if (swimming)
		{
			foreach (var offset in SwimOffsets)
			{
				var swimTo = new Point(x + offset.X, y + offset.Y);
				if (BodyClear(swimTo.X, swimTo.Y))
					edges.Add(new Edge(swimTo, MoveKind.Swim, SwimCost));
			}
		}

		if (abilities.CanPillar && supported && !swimming)
			AddPillarEdges(x, y);

		if (abilities.FlyRise > 0 && supported)
			AddFlyEdges(x, y);

		if (supported && !swimming)
			AddLeapEdges(x, y);

		if (abilities.HookReach > abilities.JumpRise && supported)
		{
			AddHookEdges(x, y);
			if (HangEdges)
				AddHangEdgesFromGround(x, y);
		}

		if (IsPlatformBelow(x, y) && DropLanding(x, y + 1, abilities.MaxFall) is Point belowPlatform)
			edges.Add(new Edge(belowPlatform, MoveKind.Drop, 1f + (belowPlatform.Y - y) * 0.3f));

		if (abilities.CanDig && supported)
			TryDig(node, new Point(x, y + 1));

		if (abilities.CanRope && !swimming && (supported || onRope))
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
		for (int rise = 1; rise <= MaxPillar && BodyClear(x, y - rise) && (Placeable(x, y - rise + 1) || Placeable(x + 1, y - rise + 1)); rise++)
		{
			int top = y - rise;
			bool exit = rise == 1 || (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Pillar, PillarCost + rise * PillarCostPerTile));
		}
	}

	const float FlyCost = 3f;
	const float FlyCostPerTile = 0.4f;

	void AddFlyEdges(int x, int y)
	{
		for (int rise = 1; rise <= abilities.JumpRise + abilities.FlyRise && BodyClear(x, y - rise); rise++)
		{
			if (rise <= abilities.JumpRise)
				continue;
			int top = y - rise;
			bool exit = (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Fly, FlyCost + rise * FlyCostPerTile));
		}
	}

	public static bool HangEdges = true;
	const int LeapDropTiles = 8;
	const float LeapCost = 4f;

	void AddLeapEdges(int x, int y)
	{
		int reach = abilities.LeapReach;
		int arc = y - 2;
		for (int direction = -1; direction <= 1; direction += 2)
		{
			if (Supported(x + direction, y) || !BodyClear(x + direction, y) || !BodyClear(x, arc))
				continue;
			for (int dx = 2; dx <= reach; dx++)
			{
				int nx = x + direction * dx;
				if (!BodyClear(nx, arc))
					break;
				for (int ny = y - 1; ny <= y + LeapDropTiles && BodyClear(nx, ny); ny++)
				{
					if (!Supported(nx, ny))
						continue;
					edges.Add(new Edge(new Point(nx, ny), MoveKind.Leap, LeapCost + dx * 0.6f + Math.Abs(ny - y) * 0.3f));
					break;
				}
			}
		}
	}

	const int HookColumnSpan = 10;
	const float HookCost = 5f;
	const float HookCostPerTile = 0.4f;

	void AddHookEdges(int x, int y)
	{
		var from = new Point(x, y);
		for (int column = x - HookColumnSpan; column <= x + HookColumnSpan; column++)
		{
			for (int row = y - abilities.JumpRise - 1; row >= y - abilities.HookReach; row--)
			{
				if (!BodyClear(column, row) || !Supported(column, row))
					continue;
				var ledge = new Point(column, row);
				if (PathFinder.HookHold(from, ledge, abilities.HookReach) != null)
					edges.Add(new Edge(ledge, MoveKind.Hook, HookCost + (y - row) * HookCostPerTile));
				break;
			}
		}
	}

	const float HangCost = 12f;
	const int MaxHangSightChecks = 1;
	const float HangCostPerTile = 0.5f;

	bool HangSpot(int x, int y)
	{
		if (Flag(x, y, HangKnown, HangBit, out int index))
			return (cells[index] & HangBit) != 0;
		return Remember(index, HangKnown, HangBit, BodyClear(x, y) && !Supported(x, y) && PathFinder.HangHold(new Point(x, y)) != null);
	}

	static bool ClearLine(Vector2 from, Vector2 to)
	{
		var delta = to - from;
		int steps = (int)(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) / 8f) + 1;
		var step = delta / steps;
		for (int i = 1; i < steps; i++)
		{
			var point = from + step * i;
			if (TileQuery.IsSolid((int)(point.X / 16f), (int)(point.Y / 16f)))
				return false;
		}
		return true;
	}

	bool DeepDropBeside(int column, int y) =>
		!Supported(column, y) && BodyClear(column, y) && (DropLanding(column, y, abilities.MaxFall) is not Point landing || landing.Y - y > abilities.JumpRise);

	void AddHangEdgesFromGround(int x, int y)
	{
		if (DeepDropBeside(x - 1, y))
			AddHangEdges(x, y, x - HookColumnSpan, x, y + 2);
		if (DeepDropBeside(x + 1, y))
			AddHangEdges(x, y, x, x + HookColumnSpan, y + 2);
		if (TileQuery.IsSolid(x - 1, y - 1) || TileQuery.IsSolid(x + 2, y - 1))
			AddHangEdges(x, y, x - 1, x + 1, y - abilities.JumpRise - 1);
	}

	void ExpandHang(int x, int y)
	{
		AddHookEdges(x, y);
		AddHangEdges(x, y, x - HookColumnSpan, x + HookColumnSpan, y + 2);
		AddJumpEdges(x, y, PathFinder.BaseJumpRise);
		if (DropLanding(x, y + 1, abilities.MaxFall) is Point landing)
			edges.Add(new Edge(landing, MoveKind.Drop, 1f + (landing.Y - y) * 0.3f));
	}

	void AddHangEdges(int x, int y, int firstColumn, int lastColumn, int lowestRow)
	{
		var origin = PathFinder.BodyCenterAt(new Point(x, y));
		float reach = abilities.HookReach * 16f;
		for (int column = firstColumn; column <= lastColumn; column++)
		{
			int sightChecks = 0;
			for (int row = y - abilities.HookReach; row <= lowestRow && sightChecks < MaxHangSightChecks; row++)
			{
				if (column == x && row >= y - 1 || !HangSpot(column, row) || PathFinder.HangHold(new Point(column, row)) is not Vector2 hold)
					continue;
				if (Vector2.Distance(origin, hold) > reach)
					continue;
				sightChecks++;
				if (!ClearLine(origin, hold + Vector2.Normalize(origin - hold) * 4f))
					continue;
				edges.Add(new Edge(new Point(column, row), MoveKind.HookHang, HangCost + (Math.Abs(column - x) + Math.Abs(row - y)) * HangCostPerTile));
				break;
			}
		}
	}

	void AddRopeClimbEdges(int x, int y)
	{
		if (!RopeFits(x, y) || !RopeFits(x, y - 1) || !RopeFits(x, y - 2))
			return;
		for (int rise = 1; rise <= MaxRopeClimb && BodyClear(x, y - rise) && RopeFits(x, y - rise - 2); rise++)
		{
			if (rise <= MaxJumpRise)
				continue;
			int top = y - rise;
			bool exit = (BodyClear(x - 1, top) && Supported(x - 1, top)) || (BodyClear(x + 1, top) && Supported(x + 1, top));
			if (exit)
				edges.Add(new Edge(new Point(x, top), MoveKind.Climb, RopeClimbBaseCost + rise * RopeClimbCostPerTile));
		}
	}

	bool Placeable(int x, int y) =>
		TileQuery.FreeForPlacing(x, y) || abilities.CanChop && TileQuery.IsChoppableTree(Main.tile[x, y].TileType);

	static bool RopeFits(int x, int y) => PathFinder.IsRope(x, y) || TileQuery.FreeForPlacing(x, y);

	const int WaterJumpRise = 3;

	void AddJumpEdges(int x, int y, int maxRise)
	{
		if (!RowClear(x, y - 1) || !RowClear(x, y - 2))
			return;
		int headroom = 0;
		while (headroom < maxRise && RowClear(x, y - 3 - headroom))
			headroom++;
		int reach = Math.Clamp(abilities.LeapReach / 2, 1, MaxJumpReach);
		for (int dx = -reach; dx <= reach; dx++)
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
				if (!CountDiggable(column, row, from.Y, ref solids, ref loosensSand))
					return;
			}
		}
		for (int row = to.Y - 2; row <= from.Y - 3; row++)
		{
			if (!CountDiggable(from.X, row, from.Y, ref solids, ref loosensSand) || !CountDiggable(from.X + 1, row, from.Y, ref solids, ref loosensSand))
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

	bool CountDiggable(int x, int y, int feetY, ref int solids, ref bool loosensSand)
	{
		if (!TileQuery.IsSolid(x, y) || IsPassableDoor(x, y))
			return true;
		if (!Diggable(x, y) || y <= feetY && TileQuery.HoldsBackLava(x, y))
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
			if (PathFinder.RopeColumn(x, y + fall) >= 0 || PathFinder.InWater(x, y + fall))
				return new Point(x, y + fall);
			if (Supported(x, y + fall))
				return fall >= PrecariousDropTiles && OnLipOverChasm(x, y + fall) ? null : new Point(x, y + fall);
		}
		return null;
	}

	const int PrecariousDropTiles = 4;

	static bool OnLipOverChasm(int x, int feetY)
	{
		bool left = TileQuery.CanStandOn(x, feetY + 1);
		if (left == TileQuery.CanStandOn(x + 1, feetY + 1))
			return false;
		int open = left ? x + 1 : x;
		for (int depth = 2; depth <= PathFinder.MaxSafeFall; depth++)
		{
			var tile = Main.tile[open, feetY + depth];
			if (TileQuery.CanStandOn(open, feetY + depth) || tile.LiquidAmount > 0)
				return false;
		}
		return true;
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
		if (Flag(x, feetY, BodyKnown, BodyClearBit, out int index))
			return (cells[index] & BodyClearBit) != 0;
		return Remember(index, BodyKnown, BodyClearBit, RowClear(x, feetY) && RowClear(x, feetY - 1) && RowClear(x, feetY - 2) && !Hazardous(x, feetY) && !Hazardous(x + 1, feetY));
	}

	bool Supported(int x, int feetY)
	{
		if (Flag(x, feetY, SupportKnown, SupportBit, out int index))
			return (cells[index] & SupportBit) != 0;
		return Remember(index, SupportKnown, SupportBit, PathFinder.IsSupportedNow(x, feetY));
	}

	static bool IsPlatformBelow(int x, int feetY) =>
		(TileQuery.IsPlatform(x, feetY + 1) || TileQuery.IsPlatform(x + 1, feetY + 1))
		&& !TileQuery.IsSolid(x, feetY + 1) && !TileQuery.IsSolid(x + 1, feetY + 1);

	static bool Hazardous(int x, int feetY) =>
		TileQuery.IsDangerousLiquid(x, feetY) || TileQuery.IsDangerousLiquid(x, feetY - 1);

	bool IsHazardousStand(Point feet)
	{
		if (Flag(feet.X, feet.Y, HazardKnown, HazardBit, out int index))
			return (cells[index] & HazardBit) != 0;
		return Remember(index, HazardKnown, HazardBit, PathFinder.NearHurtfulTile(feet.X, feet.Y) || PathFinder.OnPressurePlate(feet.X, feet.Y) || DangerSpots.IsMarked(feet.X, feet.Y));
	}

	bool Diggable(int x, int y)
	{
		if (Flag(x, y, DigKnown, DigBit, out int index))
			return (cells[index] & DigBit) != 0;
		return Remember(index, DigKnown, DigBit, ComputeDiggable(x, y));
	}

	bool ComputeDiggable(int x, int y)
	{
		int tileType = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[tileType]
			&& !HouseInspector.IsProtected(x, y)
			&& !TileQuery.SupportsShrine(x, y)
			&& PickRequirement.MinimumPick(tileType, x, y) <= abilities.PickPower
			&& (WorldGen.CanKillTile(x, y) || PathFinder.RemovableBlockerOnTop(x, y, abilities.CanChop) != null);
	}
}
