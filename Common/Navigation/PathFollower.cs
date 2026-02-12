using System;
using System.Collections.Generic;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Navigation;

public struct MotorIntent
{
	public int MoveDirection;
	public bool Jump;
	public int JumpHoldTicks;
	public Point? DigTile;
	public Point? BridgeTile;
	public bool DropThroughPlatforms;
	public int ClimbDirection;
	public int ClimbColumn;
	public Point? ScaffoldTile;
	public bool Brake;
	public bool Creep;
	public bool Fly;
	public Vector2? HookAt;
}

public sealed class PathFollower
{
	const int ReplanIntervalTicks = 180;
	const int StepTimeoutTicks = 120;
	const int ForgetFailedStepsTicks = 1800;
	const int ExtendedSearchMultiplier = 3;
	const int OffPathTolerance = 4;
	const int GoalShiftTolerance = 4;
	const int MinimumReplanGapTicks = 20;
	const int NoRouteBackoffTicks = 120;
	const int SkipAheadWindow = 4;
	const float CenteringTolerance = 3f;
	const float CreepToDropPixels = 40f;

	PathResult path = PathResult.None;
	int index;
	Point plannedGoal;
	Point stepOrigin;
	uint plannedAt;
	int stepTicks;
	MotorIntent lastIntent;
	readonly HashSet<(Point from, Point to)> failedSteps = new();
	uint failedStepsSince;

	public bool HasNoRoute { get; private set; }
	public bool HasPath => path.Nodes.Count > 0 && index < path.Nodes.Count;

	public string Describe() => HasPath
		? $"path {index}/{path.Nodes.Count} next={path.Nodes[index].Feet}:{path.Nodes[index].Arrival} reachesGoal={path.ReachesGoal} stepTicks={stepTicks} goal={plannedGoal} intent(move={lastIntent.MoveDirection} jump={lastIntent.Jump} dig={lastIntent.DigTile} bridge={lastIntent.BridgeTile} climb={lastIntent.ClimbDirection})"
		: $"no path (noRoute={HasNoRoute}, plannedAgo={Main.GameUpdateCount - plannedAt})";
	public bool ReachesGoal => path.ReachesGoal;

	public void Invalidate()
	{
		pending?.Cancel();
		pending = null;
		path = PathResult.None;
		index = 0;
		HasNoRoute = false;
		plannedAt = 0;
	}

	public MotorIntent Follow(NPC body, Vector2 destination, float arriveRadius, NavigationAbilities abilities)
	{
		var feet = PathFinder.FeetOf(body);
		var goal = new Point((int)(destination.X / 16f), (int)((destination.Y + 21f) / 16f));
		bool grounded = body.velocity.Y == 0f || body.wet;
		RememberFailedStep(feet);
		if (pending == null && NeedsReplan(feet, goal, grounded) && MayReplan())
			BeginReplan(feet, goal, arriveRadius, abilities);
		if (pending != null && PathFinder.Advance(pending))
			InstallPending(feet);
		if (path.Nodes.Count == 0)
			return default;
		SkipReachedNodes(feet, grounded, body.Center.X);
		if (index >= path.Nodes.Count)
			return default;
		stepTicks++;
		lastIntent = Execute(body, feet, grounded, path.Nodes[index], abilities);
		if (!grounded && index > 0 && path.Nodes[index - 1].Arrival == MoveKind.Fly)
			lastIntent.Fly = true;
		if (grounded && OvershootsIntoDeadlyDrop(body, feet, path.Nodes[index], lastIntent, abilities.MaxFall))
		{
			lastIntent.MoveDirection = 0;
			lastIntent.Brake = true;
		}
		else if (grounded && path.Nodes[index].Arrival == MoveKind.Drop && Math.Abs(PathFinder.CenterXOf(path.Nodes[index].Feet.X) - body.Center.X) < CreepToDropPixels)
			lastIntent.Creep = true;
		return lastIntent;
	}

	bool NeedsReplan(Point feet, Point goal, bool grounded)
	{
		if (path.Nodes.Count == 0 || index >= path.Nodes.Count)
			return true;
		if (Math.Abs(goal.X - plannedGoal.X) > GoalShiftTolerance || Math.Abs(goal.Y - plannedGoal.Y) > GoalShiftTolerance)
			return true;
		if (Main.GameUpdateCount - plannedAt > ReplanIntervalTicks || stepTicks > StepTimeoutTicks)
			return true;
		var next = path.Nodes[index].Feet;
		return grounded && (Math.Abs(next.X - feet.X) > OffPathTolerance || Math.Abs(next.Y - feet.Y) > OffPathTolerance);
	}

	void RememberFailedStep(Point feet)
	{
		if (Main.GameUpdateCount - failedStepsSince > ForgetFailedStepsTicks)
		{
			failedSteps.Clear();
			failedStepsSince = Main.GameUpdateCount;
		}
		if (stepTicks <= StepTimeoutTicks || index >= path.Nodes.Count || feet != stepOrigin)
			return;
		failedSteps.Add((stepOrigin, path.Nodes[index].Feet));
		plannedAt = 0;
	}

	bool MayReplan()
	{
		uint sinceLast = Main.GameUpdateCount - plannedAt;
		return sinceLast >= (HasNoRoute ? NoRouteBackoffTicks : MinimumReplanGapTicks);
	}

	public bool ExtendNextSearch { get; set; }

	PathSearch pending;
	Point pendingGoal;

	void BeginReplan(Point feet, Point goal, float arriveRadius, NavigationAbilities abilities)
	{
		int goalRadius = Math.Max(1, (int)(arriveRadius / 16f));
		int multiplier = ExtendNextSearch ? ExtendedSearchMultiplier : 1;
		ExtendNextSearch = false;
		pending = PathFinder.BeginFind(feet, goal, goalRadius, abilities, failedSteps, multiplier);
		pendingGoal = goal;
	}

	void InstallPending(Point feet)
	{
		path = pending.Result;
		var origin = pending.Start;
		pending = null;
		index = 0;
		plannedGoal = pendingGoal;
		plannedAt = Main.GameUpdateCount;
		stepTicks = 0;
		stepOrigin = origin;
		HasNoRoute = path.Nodes.Count == 0;
		if (feet != origin)
			SkipReachedNodes(feet, true, float.NaN);
	}

	void SkipReachedNodes(Point feet, bool grounded, float centerX)
	{
		int window = Math.Min(path.Nodes.Count, index + SkipAheadWindow);
		for (int candidate = window - 1; candidate >= index; candidate--)
		{
			var node = path.Nodes[candidate];
			bool settled = grounded || node.Arrival == MoveKind.Climb;
			if (IsAt(feet, node, centerX) && settled)
			{
				index = candidate + 1;
				stepOrigin = feet;
				stepTicks = 0;
				return;
			}
		}
	}

	static bool IsAt(Point feet, PathNode node, float centerX)
	{
		bool column = feet.X == node.Feet.X || Math.Abs(node.Feet.X - feet.X) == 1 && Math.Abs(AimX(node) - centerX) <= CenteringTolerance;
		return column && (feet.Y == node.Feet.Y || feet.Y == node.Feet.Y + 1);
	}

	static float AimX(PathNode node) => node.Arrival == MoveKind.Drop ? PathFinder.CenterXOf(node.Feet.X) : FootingCenterX(node.Feet);

	const int HookJumpHoldTicks = 15;

	MotorIntent Execute(NPC body, Point feet, bool grounded, PathNode node, NavigationAbilities abilities)
	{
		var intent = new MotorIntent();
		if (node.Arrival == MoveKind.Hook)
		{
			intent.MoveDirection = Math.Sign(FootingCenterX(node.Feet) - body.Center.X);
			if (grounded && PathFinder.HookHold(feet, node.Feet, abilities.HookReach) is Vector2 hold)
			{
				intent.HookAt = hold;
				intent.MoveDirection = 0;
			}
			else if (!grounded)
			{
				intent.Jump = body.velocity.Y < 0f;
				intent.JumpHoldTicks = HookJumpHoldTicks;
			}
			return intent;
		}
		foreach (var cell in PathFinder.CellsToClear(feet, node.Feet))
		{
			if (PathFinder.IsClosedDoor(cell.X, cell.Y))
				intent.DigTile = cell;
		}
		float offset = AimX(node) - body.Center.X;
		intent.MoveDirection = Math.Abs(offset) > CenteringTolerance ? Math.Sign(offset) : 0;
		int rise = feet.Y - node.Feet.Y;
		bool hangingOnRope = (PathFinder.RopeColumn(feet.X, feet.Y) >= 0 || PathFinder.RopeColumn(feet.X, feet.Y - 1) >= 0) && !PathFinder.IsSupportedNow(feet.X, feet.Y);
		if (hangingOnRope && node.Arrival is MoveKind.Walk or MoveKind.Drop && Math.Abs(rise) <= 1)
		{
			float standingBottom = (node.Feet.Y + 1) * 16f;
			float bottom = body.position.Y + body.height;
			if (bottom > standingBottom || bottom < standingBottom - RopeStepTolerance)
			{
				intent.ClimbDirection = bottom > standingBottom ? -1 : 1;
				intent.ClimbColumn = -1;
				intent.MoveDirection = 0;
			}
			return intent;
		}
		if (hangingOnRope && node.Arrival is MoveKind.Walk or MoveKind.Jump && rise >= 0)
		{
			intent.Jump = true;
			intent.JumpHoldTicks = HoldTicksForRise(rise + 1);
			return intent;
		}
		bool overNode = intent.MoveDirection == 0 && Math.Abs(node.Feet.X - feet.X) <= 1;
		if (grounded && node.Feet.Y > feet.Y && overNode)
			intent.DropThroughPlatforms = true;
		switch (node.Arrival)
		{
			case MoveKind.Jump:
				intent.Jump = grounded || body.velocity.Y < 0f || rise > PathFinder.BaseJumpRise;
				intent.JumpHoldTicks = HoldTicksForRise(Math.Min(rise, PathFinder.BaseJumpRise));
				if (rise > 0 && intent.MoveDirection != 0 && OverhangBeside(body, intent.MoveDirection))
					intent.MoveDirection = 0;
				break;
			case MoveKind.Fly:
				intent.Fly = true;
				intent.Jump = true;
				if (rise > 0)
					intent.MoveDirection = 0;
				break;
			case MoveKind.Drop:
				intent.DropThroughPlatforms = grounded && overNode;
				break;
			case MoveKind.Dig:
				intent.DigTile ??= FirstSolidToDig(feet, node.Feet);
				break;
			case MoveKind.RopeDown:
				intent.ClimbDirection = 1;
				intent.ClimbColumn = node.Feet.X;
				break;
			case MoveKind.Climb:
				intent.ClimbDirection = Math.Sign(node.Feet.Y - feet.Y);
				intent.ClimbColumn = node.Feet.X;
				break;
			case MoveKind.Swim:
				intent.Jump = node.Feet.Y < feet.Y || body.velocity.Y > 1f && feet.Y >= node.Feet.Y;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				break;
			case MoveKind.Pillar:
				intent.MoveDirection = 0;
				if (grounded && !TileQuery.FreeForPlacing(node.Feet.X, feet.Y) && !TileQuery.FreeForPlacing(node.Feet.X + 1, feet.Y))
				{
					intent.ScaffoldTile = ScaffoldSpot(node.Feet.X, feet.Y, body.Center.X >= PathFinder.CenterXOf(node.Feet.X));
					break;
				}
				intent.Jump = grounded || body.velocity.Y < 0f;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				if (!grounded && !PathFinder.IsSupportedNow(feet.X, feet.Y) && PathFinder.IsSupportedNow(feet.X, feet.Y + 1))
					intent.ScaffoldTile = ScaffoldSpot(node.Feet.X, feet.Y + 1, body.Center.X >= PathFinder.CenterXOf(node.Feet.X));
				break;
			case MoveKind.Bridge:
				if (MissingBridgeTile(feet, node.Feet) is Point gap)
				{
					intent.BridgeTile = gap;
					intent.MoveDirection = 0;
				}
				break;
		}
		return intent;
	}

	static Point ScaffoldSpot(int x, int y, bool preferRight)
	{
		int first = preferRight ? x + 1 : x;
		int second = preferRight ? x : x + 1;
		bool firstFree = TileQuery.FreeForPlacing(first, y);
		bool secondFree = TileQuery.FreeForPlacing(second, y);
		if (!firstFree && !secondFree && TileQuery.IsChoppableTree(Main.tile[second, y].TileType) && !TileQuery.IsChoppableTree(Main.tile[first, y].TileType))
			return new Point(second, y);
		return new Point(firstFree || !secondFree ? first : second, y);
	}

	static bool OverhangBeside(NPC body, int direction)
	{
		int column = (int)((direction > 0 ? body.position.X + body.width + 1f : body.position.X - 1f) / 16f);
		int top = (int)(body.position.Y / 16f);
		int bottom = (int)((body.position.Y + body.height - 1f) / 16f);
		bool solidAbove = false;
		for (int y = top; y <= bottom; y++)
		{
			bool solid = TileQuery.IsSolid(column, y);
			if (solidAbove && !solid)
				return true;
			solidAbove |= solid;
		}
		return false;
	}

	const float RopeStepTolerance = 6f;

	const float FootingShift = 8f;

	static float FootingCenterX(Point feet)
	{
		bool left = TileQuery.CanStandOn(feet.X, feet.Y + 1);
		bool right = TileQuery.CanStandOn(feet.X + 1, feet.Y + 1);
		float center = PathFinder.CenterXOf(feet.X);
		return left == right ? center : right ? center + FootingShift : center - FootingShift;
	}

	public bool FlatAhead(int nodes)
	{
		if (!HasPath)
			return false;
		int y = path.Nodes[index].Feet.Y;
		for (int i = index; i < Math.Min(path.Nodes.Count, index + nodes); i++)
		{
			if (path.Nodes[i].Arrival != MoveKind.Walk || path.Nodes[i].Feet.Y != y)
				return false;
		}
		return index + nodes <= path.Nodes.Count;
	}

	static bool OvershootsIntoDeadlyDrop(NPC body, Point feet, PathNode node, MotorIntent intent, int maxFall)
	{
		if (intent.Jump || intent.Fly || intent.HookAt != null || intent.BridgeTile != null || node.Arrival is MoveKind.Jump or MoveKind.Bridge or MoveKind.Pillar or MoveKind.Fly or MoveKind.Hook || body.wet)
			return false;
		int heading = intent.MoveDirection != 0 ? intent.MoveDirection : Math.Sign(body.velocity.X);
		if (heading == 0)
			return false;
		int aheadX = heading > 0 ? feet.X + 2 : feet.X - 1;
		int depth = DropBelow(aheadX, feet.Y);
		if (depth <= maxFall)
			return false;
		bool insideNodeFootprint = aheadX >= node.Feet.X && aheadX <= node.Feet.X + 1;
		if (insideNodeFootprint && node.Arrival == MoveKind.Drop)
			return false;
		bool steppingOntoNode = node.Feet.Y == feet.Y && PathFinder.IsSupportedNow(node.Feet.X, node.Feet.Y);
		return !insideNodeFootprint || !steppingOntoNode;
	}

	static int DropBelow(int x, int feetY)
	{
		int depth = 0;
		while (depth <= PathFinder.MaxSafeFall * 4 && !TileQuery.CanStandOn(x, feetY + depth + 1) && Main.tile[x, feetY + depth + 1].LiquidAmount == 0)
			depth++;
		return depth;
	}

	static Point? MissingBridgeTile(Point feet, Point node)
	{
		int near = node.X > feet.X ? node.X : node.X + 1;
		int far = node.X > feet.X ? node.X + 1 : node.X;
		foreach (int x in new[] { near, far })
		{
			if (!TileQuery.CanStandOn(x, node.Y + 1) && !Main.tile[x, node.Y + 1].HasTile)
				return new Point(x, node.Y + 1);
		}
		return null;
	}

	static int HoldTicksForRise(int rise) => rise <= 1 ? 1 : Math.Min(15, (rise * 16 - 21) / 5 + 2);

	static Point? FirstSolidToDig(Point feet, Point next)
	{
		foreach (var cell in PathFinder.CellsToClear(feet, next))
		{
			if (TileQuery.IsSolid(cell.X, cell.Y) && !PathFinder.IsClosedDoor(cell.X, cell.Y))
				return cell;
		}
		return null;
	}
}
