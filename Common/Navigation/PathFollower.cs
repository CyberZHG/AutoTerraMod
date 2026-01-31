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
		if (NeedsReplan(feet, goal, grounded) && MayReplan() && PathFinder.HasBudget)
		{
			Replan(feet, goal, arriveRadius, abilities);
			HasNoRoute = path.Nodes.Count == 0;
		}
		if (path.Nodes.Count == 0)
			return default;
		SkipReachedNodes(feet, grounded);
		if (index >= path.Nodes.Count)
			return default;
		stepTicks++;
		lastIntent = Execute(body, feet, grounded, path.Nodes[index]);
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

	void Replan(Point feet, Point goal, float arriveRadius, NavigationAbilities abilities)
	{
		int goalRadius = Math.Max(1, (int)(arriveRadius / 16f));
		int multiplier = ExtendNextSearch ? ExtendedSearchMultiplier : 1;
		ExtendNextSearch = false;
		path = PathFinder.Find(feet, goal, goalRadius, abilities, failedSteps, multiplier);
		index = 0;
		plannedGoal = goal;
		plannedAt = Main.GameUpdateCount;
		stepTicks = 0;
		stepOrigin = feet;
	}

	void SkipReachedNodes(Point feet, bool grounded)
	{
		int window = Math.Min(path.Nodes.Count, index + SkipAheadWindow);
		for (int candidate = window - 1; candidate >= index; candidate--)
		{
			var node = path.Nodes[candidate];
			bool settled = grounded || node.Arrival == MoveKind.Climb;
			if (IsAt(feet, node.Feet) && settled)
			{
				index = candidate + 1;
				stepOrigin = feet;
				stepTicks = 0;
				return;
			}
		}
	}

	static bool IsAt(Point feet, Point node) => feet.X == node.X && Math.Abs(feet.Y - node.Y) <= 1;

	MotorIntent Execute(NPC body, Point feet, bool grounded, PathNode node)
	{
		var intent = new MotorIntent();
		foreach (var cell in PathFinder.CellsToClear(feet, node.Feet))
		{
			if (PathFinder.IsClosedDoor(cell.X, cell.Y))
				intent.DigTile = cell;
		}
		float offset = PathFinder.CenterXOf(node.Feet.X) - body.Center.X;
		intent.MoveDirection = Math.Abs(offset) > CenteringTolerance ? Math.Sign(offset) : 0;
		int rise = feet.Y - node.Feet.Y;
		bool hangingOnRope = !grounded && (PathFinder.RopeColumn(feet.X, feet.Y) >= 0 || PathFinder.RopeColumn(feet.X, feet.Y - 1) >= 0);
		if (hangingOnRope && node.Arrival is MoveKind.Walk or MoveKind.Jump && rise >= 0)
		{
			intent.Jump = true;
			intent.JumpHoldTicks = HoldTicksForRise(rise + 1);
			return intent;
		}
		if (grounded && node.Feet.Y > feet.Y && node.Feet.X == feet.X)
			intent.DropThroughPlatforms = true;
		switch (node.Arrival)
		{
			case MoveKind.Jump:
				intent.Jump = grounded || body.velocity.Y < 0f;
				intent.JumpHoldTicks = HoldTicksForRise(rise);
				break;
			case MoveKind.Drop:
				intent.DropThroughPlatforms = grounded && node.Feet.X == feet.X;
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
				intent.Jump = node.Feet.Y < feet.Y || body.velocity.Y > 1f;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				break;
			case MoveKind.Pillar:
				intent.MoveDirection = 0;
				intent.Jump = grounded || body.velocity.Y < 0f;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				if (!grounded && !PathFinder.IsSupportedNow(feet.X, feet.Y) && PathFinder.IsSupportedNow(feet.X, feet.Y + 1))
					intent.ScaffoldTile = new Point(node.Feet.X + (body.Center.X >= PathFinder.CenterXOf(node.Feet.X) ? 1 : 0), feet.Y + 1);
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

	static bool OvershootsIntoDeadlyDrop(NPC body, Point feet, PathNode node, MotorIntent intent, int maxFall)
	{
		if (intent.Jump || intent.BridgeTile != null || node.Arrival is MoveKind.Jump or MoveKind.Bridge or MoveKind.Pillar || body.wet)
			return false;
		int heading = intent.MoveDirection != 0 ? intent.MoveDirection : Math.Sign(body.velocity.X);
		if (heading == 0)
			return false;
		int aheadX = heading > 0 ? feet.X + 2 : feet.X - 1;
		int depth = DropBelow(aheadX, feet.Y);
		if (depth <= maxFall)
			return false;
		bool insideNodeFootprint = aheadX >= node.Feet.X && aheadX <= node.Feet.X + 1;
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
