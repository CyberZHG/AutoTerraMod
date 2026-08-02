using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	RopeDown,
	Fly,
	Hook,
	Leap,
	HookHang
}

public readonly record struct PathNode(Point Feet, MoveKind Arrival);

public sealed record PathResult(List<PathNode> Nodes, bool ReachesGoal)
{
	public static readonly PathResult None = new(new List<PathNode>(), false);
}

public readonly record struct NavigationAbilities(int PickPower, bool CanDig, bool CanBridge, bool CanPillar, bool CanRope = false, int MaxFall = PathFinder.MaxSafeFall, bool CanUnlock = false, int JumpRise = PathFinder.BaseJumpRise, int FlyRise = 0, int HookReach = 0, bool CanChop = false, int SingleJumpRise = PathFinder.BaseJumpRise, int LeapReach = PathFinder.BaseLeapReach);

public static class PathFinder
{
	const int MaxExpandedNodes = 2000;
	const int MaxSearchRadius = 90;
	public const int LongTripTiles = 60;
	const int WaypointTiles = 50;
	const int WaypointRadius = 4;
	const int ExitSearchRadius = 200;
	const int MazeSearchMultiplier = 6;
	const int MazeSearchRadius = 200;
	const int LongRouteRadius = 250;
	public const int MaxSafeFall = 24;
	public const int BaseJumpRise = 5;
	public const int BaseLeapReach = 6;
	const int SwimmableLiquid = 64;
	const double TickBudgetMilliseconds = 3.0;
	const double SliceMilliseconds = 1.0;
	const uint ForgetWaitingTicks = 60;

	static readonly List<PathSearch> waiting = new();

	public static bool Prioritized { get; set; }

	static uint budgetTick;
	static long tickDeadline;

	static long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);

	static void StartBudgetTick()
	{
		if (budgetTick == Main.GameUpdateCount)
			return;
		budgetTick = Main.GameUpdateCount;
		tickDeadline = Stopwatch.GetTimestamp() + Ticks(TickBudgetMilliseconds);
	}

	public static bool HasBudget
	{
		get
		{
			StartBudgetTick();
			return Stopwatch.GetTimestamp() < tickDeadline;
		}
	}

	public static bool Advance(PathSearch search)
	{
		if (search.IsDone)
			return true;
		StartBudgetTick();
		if (Prioritized)
		{
			waiting.Remove(search);
			return search.Step(Stopwatch.GetTimestamp() + Ticks(SliceMilliseconds));
		}
		waiting.RemoveAll(other => other.IsDone || Main.GameUpdateCount - other.PolledAt > ForgetWaitingTicks);
		int ahead = waiting.IndexOf(search);
		long reserved = (ahead < 0 ? waiting.Count : ahead) * Ticks(SliceMilliseconds);
		long now = Stopwatch.GetTimestamp();
		search.PolledAt = Main.GameUpdateCount;
		if (now + reserved >= tickDeadline)
		{
			if (ahead < 0)
				waiting.Add(search);
			return false;
		}
		waiting.Remove(search);
		return search.Step(Math.Min(tickDeadline - reserved, now + Ticks(SliceMilliseconds)));
	}

	public static Point FeetOf(Entity body)
	{
		int x = (int)Math.Round(body.Center.X / 16f) - 1;
		int y = (int)((body.position.Y + body.height - 1) / 16f);
		bool sunkIntoFloor = TileQuery.IsSolid(x, y) || TileQuery.IsSolid(x + 1, y);
		return sunkIntoFloor && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x + 1, y - 1) ? new Point(x, y - 1) : new Point(x, y);
	}

	public static float CenterXOf(int nodeX) => (nodeX + 1) * 16f;

	public static PathSearch BeginFind(Point start, Point goal, int goalRadius, NavigationAbilities abilities, ISet<(Point from, Point to)> forbiddenSteps = null, int searchMultiplier = 1)
	{
		int maxExpanded = MaxExpandedNodes * searchMultiplier;
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
		return new PathSearch(start, abilities, node => IsGoal(node, goal, goalRadius), node => Heuristic(node, goal), forbiddenSteps, maxExpanded, searchRadius);
	}

	public static PathSearch BeginExit(Point start, NavigationAbilities abilities, Func<Point, float> distanceToExit, int searchMultiplier) =>
		new(start, abilities, node => distanceToExit(node) <= 0f, distanceToExit, null, MaxExpandedNodes * searchMultiplier, ExitSearchRadius);

	public static PathSearch BeginLongRoute(Point start, Point goal, int goalRadius, NavigationAbilities abilities, int maxNodes) =>
		new(start, abilities, node => IsGoal(node, goal, goalRadius), node => Heuristic(node, goal), null, maxNodes, LongRouteRadius);

	public static PathResult Find(Point start, Point goal, int goalRadius, NavigationAbilities abilities) =>
		BeginFind(start, goal, goalRadius, abilities).RunToEnd();

	public static PathResult FindLongRoute(Point start, Point goal, int goalRadius, NavigationAbilities abilities, int maxNodes) =>
		BeginLongRoute(start, goal, goalRadius, abilities, maxNodes).RunToEnd();

	public static bool InsideMaze(Point tile) => WorldGen.InWorld(tile.X, tile.Y) && Main.tile[tile.X, tile.Y].WallType == WallID.LihzahrdBrickUnsafe;

	public static bool IsGoal(Point node, Point goal, int radius) =>
		Math.Abs(node.X - goal.X) <= radius && Math.Abs(node.Y - goal.Y) <= radius + 2 && !SealedBelow(node, goal);

	static bool SealedBelow(Point node, Point goal)
	{
		int targetRow = goal.Y - 1;
		int bodyTop = node.Y - 2;
		if (bodyTop - 1 <= targetRow || TileQuery.IsSolid(goal.X, targetRow))
			return false;
		for (int y = targetRow + 1; y < bodyTop; y++)
		{
			if (TileQuery.IsSolid(node.X, y) || TileQuery.IsSolid(node.X + 1, y))
				return true;
		}
		return false;
	}

	static float Heuristic(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

	static Point WaypointToward(Point start, Point goal)
	{
		float distance = Heuristic(start, goal);
		float fraction = WaypointTiles / distance;
		return new Point(start.X + (int)((goal.X - start.X) * fraction), start.Y + (int)((goal.Y - start.Y) * fraction));
	}

	public static int BridgeColumn(int fromX, int toX) => toX > fromX ? toX + 1 : toX;

	public static int RopeColumn(int x, int y)
	{
		if (IsRope(x, y))
			return x;
		return IsRope(x + 1, y) ? x + 1 : -1;
	}

	public static Vector2 BodyCenterAt(Point feet) => new((feet.X + 1) * 16f, (feet.Y + 1) * 16f - 21f);

	public static Vector2? HangHold(Point feet)
	{
		int headRow = feet.Y - 3;
		if (TileQuery.IsSolid(feet.X, headRow) || TileQuery.IsSolid(feet.X + 1, headRow))
			return new Vector2((feet.X + 1) * 16f, (headRow + 1) * 16f);
		int chestRow = feet.Y - 1;
		if (TileQuery.IsSolid(feet.X - 1, chestRow))
			return new Vector2(feet.X * 16f, chestRow * 16f + 8f);
		if (TileQuery.IsSolid(feet.X + 2, chestRow))
			return new Vector2((feet.X + 2) * 16f, chestRow * 16f + 8f);
		return null;
	}

	public static Vector2? HookHold(Point from, Point ledge, int reach)
	{
		int side = from.X < ledge.X ? -1 : from.X > ledge.X + 1 ? 1 : 0;
		var origin = BodyCenterAt(from);
		int supportRow = ledge.Y + 1;
		for (int pick = 0; pick < 2; pick++)
		{
			int tileX = side > 0 ? ledge.X + 1 - pick : ledge.X + pick;
			if (!TileQuery.CanStandOn(tileX, supportRow))
				continue;
			int hang = side != 0 ? side : tileX == ledge.X ? -1 : 1;
			if (!HangRoom(tileX + hang, supportRow, hang))
				continue;
			var hold = new Vector2(tileX * 16f + (hang > 0 ? 16f : 0f), supportRow * 16f + 8f);
			if (Vector2.Distance(origin, hold) > reach * 16f)
				continue;
			if (Collision.CanHitLine(origin, 1, 1, hold + new Vector2(hang * 4f, 0f), 1, 1))
				return hold;
		}
		return null;
	}

	static bool HangRoom(int column, int supportRow, int hang)
	{
		for (int row = supportRow - 3; row <= supportRow + 1; row++)
		{
			if (TileQuery.IsSolid(column, row) || TileQuery.IsSolid(column + hang, row))
				return false;
		}
		return true;
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

	public static Point? RemovableBlockerOnTop(int x, int y, bool canChop)
	{
		if (!WorldGen.InWorld(x, y - 1, 2) || WorldGen.CanKillTile(x, y))
			return null;
		var above = Main.tile[x, y - 1];
		if (!above.HasTile || above.TileType == Main.tile[x, y].TileType || Building.HouseInspector.IsProtected(x, y - 1))
			return null;
		int type = above.TileType;
		if (Main.tileAxe[type])
			return canChop ? new Point(x, y - 1) : null;
		bool container = TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type] || type == TileID.Hellforge;
		return container && WorldGen.CanKillTile(x, y - 1) ? new Point(x, y - 1) : null;
	}

	public static bool IsClosedDoor(int x, int y) => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.ClosedDoor;

	public static bool IsSupportedNow(int x, int feetY) => TileQuery.CanStandOn(x, feetY + 1) || TileQuery.CanStandOn(x + 1, feetY + 1);

	public static bool InWater(int x, int feetY) => IsSwimmable(x, feetY - 1) || IsSwimmable(x + 1, feetY - 1);

	static bool IsSwimmable(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 5))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount >= SwimmableLiquid && tile.LiquidType != LiquidID.Lava;
	}

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

	public static bool OnPressurePlate(int x, int feetY)
	{
		for (int cx = x; cx <= x + 1; cx++)
		{
			for (int cy = feetY - 2; cy <= feetY; cy++)
			{
				if (WorldGen.InWorld(cx, cy, 5) && Main.tile[cx, cy].HasTile && Main.tile[cx, cy].TileType == TileID.PressurePlates)
					return true;
			}
		}
		return false;
	}

	static bool IsHurtful(int tileType) =>
		TileID.Sets.TouchDamageImmediate[tileType] > 0 || TileID.Sets.TouchDamageHot[tileType] || TileID.Sets.TouchDamageBleeding[tileType];

	public static bool IsRope(int x, int y) => WorldGen.InWorld(x, y, 5) && Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType];

	public static bool UnderFallingBlock(int x, int y)
	{
		var above = Main.tile[x, y - 1];
		return above.HasTile && TileID.Sets.Falling[above.TileType];
	}
}
