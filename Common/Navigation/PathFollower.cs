using System;
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
}

public sealed class PathFollower
{
	const int ReplanIntervalTicks = 180;
	const int StepTimeoutTicks = 180;
	const int OffPathTolerance = 4;
	const int GoalShiftTolerance = 4;
	const int MinimumReplanGapTicks = 20;
	const int NoRouteBackoffTicks = 120;
	const int SkipAheadWindow = 4;
	const float CenteringTolerance = 3f;

	PathResult path = PathResult.None;
	int index;
	Point plannedGoal;
	Point stepOrigin;
	uint plannedAt;
	int stepTicks;

	public bool HasNoRoute { get; private set; }
	public bool HasPath => path.Nodes.Count > 0 && index < path.Nodes.Count;

	public string Describe() => HasPath
		? $"path {index}/{path.Nodes.Count} next={path.Nodes[index].Feet}:{path.Nodes[index].Arrival} reachesGoal={path.ReachesGoal} stepTicks={stepTicks} goal={plannedGoal}"
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
		return Execute(body, feet, grounded, path.Nodes[index]);
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

	bool MayReplan()
	{
		uint sinceLast = Main.GameUpdateCount - plannedAt;
		return sinceLast >= (HasNoRoute ? NoRouteBackoffTicks : MinimumReplanGapTicks);
	}

	void Replan(Point feet, Point goal, float arriveRadius, NavigationAbilities abilities)
	{
		int goalRadius = Math.Max(1, (int)(arriveRadius / 16f));
		path = PathFinder.Find(feet, goal, goalRadius, abilities);
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
		switch (node.Arrival)
		{
			case MoveKind.Jump:
				intent.Jump = grounded || body.velocity.Y < 0f;
				intent.JumpHoldTicks = HoldTicksForRise(rise);
				break;
			case MoveKind.Drop:
				intent.DropThroughPlatforms = node.Feet.X == feet.X;
				break;
			case MoveKind.Dig:
				intent.DigTile ??= FirstSolidToDig(feet, node.Feet);
				break;
			case MoveKind.Climb:
				intent.ClimbDirection = Math.Sign(node.Feet.Y - feet.Y);
				intent.ClimbColumn = Math.Max(PathFinder.RopeColumn(node.Feet.X, node.Feet.Y), PathFinder.RopeColumn(feet.X, feet.Y));
				break;
			case MoveKind.Swim:
				intent.Jump = node.Feet.Y < feet.Y || body.velocity.Y > 1f;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				break;
			case MoveKind.Pillar:
				intent.MoveDirection = 0;
				intent.Jump = grounded || body.velocity.Y < 0f;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				if (!grounded && feet.Y < stepOrigin.Y && !PathFinder.Supported(stepOrigin.X, stepOrigin.Y - 1))
					intent.ScaffoldTile = new Point(stepOrigin.X + (body.Center.X >= PathFinder.CenterXOf(stepOrigin.X) ? 1 : 0), stepOrigin.Y);
				break;
			case MoveKind.Bridge:
				if (!PathFinder.Supported(node.Feet.X, node.Feet.Y))
				{
					intent.BridgeTile = new Point(PathFinder.BridgeColumn(feet.X, node.Feet.X), node.Feet.Y + 1);
					intent.MoveDirection = 0;
				}
				break;
		}
		return intent;
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
