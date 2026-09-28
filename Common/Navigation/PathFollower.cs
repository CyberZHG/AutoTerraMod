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
	public bool HoldRope;
	public bool Fly;
	public Vector2? HookAt;
	public Vector2? HangAt;
}

public sealed class PathFollower
{
	const int ReplanIntervalTicks = 300;
	const int StepTimeoutTicks = 120;
	const int MaxLeapTicks = 600;
	const int ForgetFailedStepsTicks = 1800;
	const int ExtendedSearchMultiplier = 3;
	const int OffPathTolerance = 4;
	const int GoalShiftTolerance = 4;
	const int FarGoalShiftDivisor = 8;
	const int MinimumReplanGapTicks = 20;
	const int NoRouteBackoffTicks = 120;
	const int SkipAheadWindow = 4;
	const float CenteringTolerance = 3f;
	const int ShaftAimRise = 1;
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

	public int StepsReached { get; private set; }
	public bool HasPath => path.Nodes.Count > 0 && index < path.Nodes.Count;

	public bool IsSearching => pending != null;

	public string Describe() => HasPath
		? $"path {index}/{path.Nodes.Count} next={path.Nodes[index].Feet}:{path.Nodes[index].Arrival} reachesGoal={path.ReachesGoal} stepTicks={stepTicks} goal={plannedGoal} intent(move={lastIntent.MoveDirection} jump={lastIntent.Jump} dig={lastIntent.DigTile} bridge={lastIntent.BridgeTile} climb={lastIntent.ClimbDirection})"
		: $"no path (noRoute={HasNoRoute}, plannedAgo={Main.GameUpdateCount - plannedAt})";
	public bool ReachesGoal => path.ReachesGoal;
	public bool FinishedShortOfGoal => path.Nodes.Count > 0 && index >= path.Nodes.Count && !path.ReachesGoal;

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
		if (pending == null && (NeedsReplan(feet, goal, grounded) || OutOfMaterial(abilities)) && MayReplan())
			BeginReplan(feet, goal, arriveRadius, abilities);
		if (pending != null && PathFinder.Advance(pending))
			InstallPending(feet);
		if (path.Nodes.Count == 0)
			return default;
		SkipReachedNodes(feet, grounded || body.velocity == Vector2.Zero, body.Center.X);
		if (index >= path.Nodes.Count)
			return default;
		stepTicks++;
		lastIntent = Execute(body, feet, grounded, path.Nodes[index], abilities);
		if (!grounded && index > 0 && path.Nodes[index - 1].Arrival == MoveKind.Fly && feet.Y > path.Nodes[index].Feet.Y)
			lastIntent.Fly = true;
		bool launchesNext = index + 1 < path.Nodes.Count && path.Nodes[index + 1].Arrival is not (MoveKind.Walk or MoveKind.Drop);
		if (grounded && OvershootsIntoDeadlyDrop(body, feet, path.Nodes[index], lastIntent, abilities.MaxFall, launchesNext))
		{
			lastIntent.MoveDirection = 0;
			lastIntent.Brake = true;
		}
		else if (grounded && path.Nodes[index].Arrival == MoveKind.Drop && Math.Abs(PathFinder.CenterXOf(path.Nodes[index].Feet.X) - body.Center.X) < CreepToDropPixels)
			lastIntent.Creep = true;
		return lastIntent;
	}

	static bool SameFloor(Point feet, Point node)
	{
		for (int x = Math.Min(feet.X, node.X); x <= Math.Max(feet.X, node.X); x++)
			if (!PathFinder.IsSupportedNow(x, feet.Y))
				return false;
		return true;
	}

	static bool BodyFitsLevel(int x, int feetY)
	{
		for (int column = x; column <= x + 1; column++)
			for (int row = feetY - 2; row <= feetY; row++)
				if (TileQuery.IsSolid(column, row))
					return false;
		return true;
	}

	bool OutOfMaterial(NavigationAbilities abilities) =>
		index < path.Nodes.Count && (path.Nodes[index].Arrival == MoveKind.Pillar && !abilities.CanPillar || path.Nodes[index].Arrival == MoveKind.Bridge && !abilities.CanBridge);

	bool NeedsReplan(Point feet, Point goal, bool grounded)
	{
		if (path.Nodes.Count == 0 || index >= path.Nodes.Count)
			return true;
		int shiftTolerance = Math.Max(GoalShiftTolerance, (Math.Abs(plannedGoal.X - feet.X) + Math.Abs(plannedGoal.Y - feet.Y)) / FarGoalShiftDivisor);
		if (Math.Abs(goal.X - plannedGoal.X) > shiftTolerance || Math.Abs(goal.Y - plannedGoal.Y) > shiftTolerance)
			return true;
		if (!grounded && path.Nodes[index].Arrival == MoveKind.Leap && feet.Y > path.Nodes[index].Feet.Y + OffPathTolerance)
		{
			failedSteps.Add((index > 0 ? path.Nodes[index - 1].Feet : stepOrigin, path.Nodes[index].Feet));
			return true;
		}
		if (!grounded && stepTicks < MaxLeapTicks && (path.Nodes[index].Arrival is MoveKind.Leap or MoveKind.HookHang || index > 0 && path.Nodes[index - 1].Arrival == MoveKind.HookHang))
			return false;
		if (Main.GameUpdateCount - plannedAt > ReplanIntervalTicks || stepTicks > StepTimeoutTicks)
			return true;
		var next = path.Nodes[index].Feet;
		if (grounded && DugAwayFooting(feet, path.Nodes[index]))
		{
			failedSteps.Add((feet, next));
			return true;
		}
		return grounded && (Math.Abs(next.X - feet.X) > OffPathTolerance || Math.Abs(next.Y - feet.Y) > OffPathTolerance);
	}

	static bool DugAwayFooting(Point feet, PathNode node) =>
		node.Arrival == MoveKind.Dig && node.Feet.Y < feet.Y && !PathFinder.IsSupportedNow(node.Feet.X, node.Feet.Y) && FirstSolidToDig(feet, node.Feet) == null;

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

	public bool StartsHanging { get; set; }

	void BeginReplan(Point feet, Point goal, float arriveRadius, NavigationAbilities abilities)
	{
		int goalRadius = Math.Max(1, (int)(arriveRadius / 16f));
		if (PathFinder.IsGoal(feet, goal, goalRadius))
			goalRadius = 0;
		int multiplier = ExtendNextSearch ? ExtendedSearchMultiplier : 1;
		ExtendNextSearch = false;
		pending = PathFinder.BeginFind(feet, goal, goalRadius, abilities, failedSteps, multiplier, StartsHanging);
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
			if (IsAt(feet, node, centerX) && settled || node.Arrival == MoveKind.Fly && ReachedFlightTop(feet, node))
			{
				StepsReached += candidate + 1 - index;
				index = candidate + 1;
				stepOrigin = feet;
				stepTicks = 0;
				return;
			}
		}
	}

	const int FlightOvershootRows = 3;
	const int RopeTopStallTicks = 20;
	const int SteerAscentRows = 1;
	const float RopeStallSpeed = 1f;

	static bool ReachedFlightTop(Point feet, PathNode node) =>
		Math.Abs(feet.X - node.Feet.X) <= 1 && feet.Y <= node.Feet.Y && feet.Y >= node.Feet.Y - FlightOvershootRows;

	static bool IsAt(Point feet, PathNode node, float centerX)
	{
		bool column = feet.X == node.Feet.X || Math.Abs(node.Feet.X - feet.X) == 1 && Math.Abs(AimX(node) - centerX) <= CenteringTolerance;
		bool onPlatformInNode = node.Arrival == MoveKind.Jump && feet.Y == node.Feet.Y - 1 && (TileQuery.IsPlatform(node.Feet.X, node.Feet.Y) || TileQuery.IsPlatform(node.Feet.X + 1, node.Feet.Y));
		bool sunkBelow = feet.Y == node.Feet.Y + 1 && node.Arrival is MoveKind.Walk or MoveKind.Drop or MoveKind.Swim;
		return column && (feet.Y == node.Feet.Y || onPlatformInNode || sunkBelow);
	}

	static float AimX(PathNode node) => node.Arrival switch
	{
		MoveKind.Drop => PathFinder.CenterXOf(node.Feet.X),
		MoveKind.HookHang => PathFinder.BodyCenterAt(node.Feet).X,
		_ => FootingCenterX(node.Feet)
	};

	static int AirSteer(NPC body, float toLanding)
	{
		float speedToward = body.velocity.X * Math.Sign(toLanding);
		float stoppingDistance = speedToward > 0f ? speedToward * speedToward / (2f * AirSlowdown) : 0f;
		return Math.Abs(toLanding) > CenteringTolerance + stoppingDistance ? Math.Sign(toLanding) : 0;
	}

	const float LeapLandingSlack = 24f;
	const float AirSlowdown = 0.2f;
	const float TurnAroundSpeed = 0.5f;

	static void Leap(NPC body, Point feet, bool grounded, PathNode node, NavigationAbilities abilities, ref MotorIntent intent)
	{
		float toLanding = FootingCenterX(node.Feet) - body.Center.X;
		int heading = Math.Sign(node.Feet.X - feet.X);
		if (grounded)
		{
			intent.MoveDirection = heading;
			bool atEdge = !PathFinder.IsSupportedNow(heading > 0 ? feet.X + 2 : feet.X - 1, feet.Y);
			intent.Jump = atEdge && Math.Abs(node.Feet.X - feet.X) > 1;
			intent.JumpHoldTicks = HoldTicksForRise(abilities.SingleJumpRise);
			return;
		}
		bool farFromLanding = Math.Abs(toLanding) > LeapLandingSlack;
		intent.MoveDirection = AirSteer(body, toLanding);
		intent.Jump = farFromLanding || feet.Y > node.Feet.Y;
		intent.JumpHoldTicks = HoldTicksForRise(abilities.SingleJumpRise);
		intent.Fly = intent.Jump && body.velocity.Y > 0f && abilities.FlyRise > 0 && feet.Y >= node.Feet.Y - LeapHoverRows;
	}

	const int LeapHoverRows = 1;

	const int HookJumpHoldTicks = 15;
	const int RopeLeapCommitTicks = 40;
	const int RopeSideLeapRise = 2;
	uint ropeLeapAt;
	const float HookLaunchClearance = 6f;
	Point? hookedToward;

	MotorIntent Execute(NPC body, Point feet, bool grounded, PathNode node, NavigationAbilities abilities)
	{
		var intent = new MotorIntent();
		if (node.Arrival == MoveKind.HookHang)
		{
			intent.HookAt = PathFinder.HangHold(node.Feet);
			intent.HangAt = PathFinder.BodyCenterAt(node.Feet);
			return intent;
		}
		if (node.Arrival == MoveKind.Hook)
		{
			intent.MoveDirection = Math.Sign(FootingCenterX(node.Feet) - body.Center.X);
			if (grounded || hookedToward != node.Feet)
			{
				hookedToward = null;
				if (PathFinder.HookHold(feet, node.Feet, abilities.HookReach) is Vector2 hold)
				{
					int hang = hold.X > FootingCenterX(node.Feet) ? 1 : -1;
					float launchX = hold.X + hang * (body.width / 2f + HookLaunchClearance);
					if ((body.Center.X - launchX) * hang >= 0f)
					{
						intent.HookAt = hold;
						intent.MoveDirection = 0;
						hookedToward = node.Feet;
						return intent;
					}
					intent.MoveDirection = hang;
					intent.Jump = grounded && !PathFinder.IsSupportedNow(feet.X + hang, feet.Y);
					return intent;
				}
			}
			if (!grounded)
			{
				bool aboveLedge = feet.Y <= node.Feet.Y;
				float toLedge = FootingCenterX(node.Feet) - body.Center.X;
				intent.Jump = body.velocity.Y < 0f && !aboveLedge;
				intent.JumpHoldTicks = HookJumpHoldTicks;
				intent.MoveDirection = Math.Abs(toLedge) > CenteringTolerance ? Math.Sign(toLedge) : 0;
				intent.Creep = aboveLedge;
			}
			return intent;
		}
		foreach (var cell in PathFinder.CellsToClear(feet, node.Feet))
		{
			if (PathFinder.IsClosedDoor(cell.X, cell.Y))
				intent.DigTile = cell;
		}
		int rise = feet.Y - node.Feet.Y;
		float aimX = node.Arrival == MoveKind.Fly && rise > ShaftAimRise ? PathFinder.BodyCenterAt(node.Feet).X : AimX(node);
		float offset = aimX - body.Center.X;
		intent.MoveDirection = Math.Abs(offset) > CenteringTolerance ? Math.Sign(offset) : 0;
		bool hangingOnRope = (PathFinder.RopeColumn(feet.X, feet.Y) >= 0 || PathFinder.RopeColumn(feet.X, feet.Y - 1) >= 0) && !PathFinder.IsSupportedNow(feet.X, feet.Y);
		bool ropeReachesNode = PathFinder.RopeColumn(feet.X, node.Feet.Y) >= 0 || PathFinder.RopeColumn(feet.X, node.Feet.Y - 1) >= 0;
		bool leapingOffRope = Main.GameUpdateCount - ropeLeapAt < RopeLeapCommitTicks && !grounded;
		if (hangingOnRope && !leapingOffRope && (node.Arrival is MoveKind.Walk or MoveKind.Drop && Math.Abs(rise) <= 1 || node.Arrival is MoveKind.Walk or MoveKind.Drop or MoveKind.Jump && ropeReachesNode))
		{
			float standingBottom = (node.Feet.Y + 1) * 16f;
			float bottom = body.position.Y + body.height;
			bool stalledBelowTop = stepTicks > RopeTopStallTicks && Math.Abs(body.velocity.Y) < RopeStallSpeed;
			bool sidewaysLeap = Math.Abs(node.Feet.X - feet.X) > 1 && rise <= RopeSideLeapRise;
			if (bottom > standingBottom && node.Feet.X != feet.X && (stalledBelowTop || sidewaysLeap))
			{
				intent.Jump = true;
				intent.JumpHoldTicks = HoldTicksForRise(Math.Abs(node.Feet.X - feet.X) > 1 ? abilities.SingleJumpRise : 2);
				intent.MoveDirection = Math.Sign(node.Feet.X - feet.X);
				ropeLeapAt = Main.GameUpdateCount;
			}
			else if (bottom > standingBottom || bottom < standingBottom - RopeStepTolerance)
			{
				intent.ClimbDirection = bottom > standingBottom ? -1 : 1;
				intent.ClimbColumn = feet.X;
				intent.MoveDirection = 0;
			}
			else
				intent.HoldRope = true;
			return intent;
		}
		if (hangingOnRope && !leapingOffRope && node.Arrival is MoveKind.Walk or MoveKind.Jump && rise >= 0)
		{
			intent.Jump = true;
			intent.JumpHoldTicks = HoldTicksForRise(rise + 1);
			ropeLeapAt = Main.GameUpdateCount;
			return intent;
		}
		bool overNode = intent.MoveDirection == 0 && Math.Abs(node.Feet.X - feet.X) <= 1;
		if (grounded && node.Feet.Y > feet.Y && overNode)
			intent.DropThroughPlatforms = true;
		switch (node.Arrival)
		{
			case MoveKind.Walk when grounded && body.position.Y + body.height - (node.Feet.Y + 1) * 16f > StepUpPixels:
				intent.Jump = true;
				intent.JumpHoldTicks = HoldTicksForRise(2);
				break;
			case MoveKind.Jump when grounded && rise == 0 && SameFloor(feet, node.Feet):
				intent.Creep = true;
				break;
			case MoveKind.Jump:
				intent.Jump = grounded && body.velocity.X * intent.MoveDirection > -TurnAroundSpeed || body.velocity.Y < 0f || rise > abilities.SingleJumpRise
					|| !grounded && rise > 0 && abilities.JumpRise > abilities.SingleJumpRise;
				intent.JumpHoldTicks = HoldTicksForRise(Math.Min(rise, abilities.SingleJumpRise));
				if (!grounded && (body.velocity.Y > 0f || stepOrigin.Y - node.Feet.Y <= SteerAscentRows))
					intent.MoveDirection = AirSteer(body, offset);
				if (rise > 0 && intent.MoveDirection != 0 && (OverhangBeside(body, intent.MoveDirection) || BelowAdjacentLedge(feet, node)))
					intent.MoveDirection = 0;
				break;
			case MoveKind.Fly:
				bool inFlightColumn = Math.Abs(feet.X - node.Feet.X) <= 1;
				bool belowFlightTop = feet.Y > node.Feet.Y;
				intent.Fly = belowFlightTop && (inFlightColumn || body.velocity.Y > 0f);
				intent.Jump = intent.Fly || grounded && belowFlightTop && inFlightColumn;
				break;
			case MoveKind.Leap:
				Leap(body, feet, grounded, node, abilities, ref intent);
				break;
			case MoveKind.Drop:
				intent.DropThroughPlatforms = grounded && (overNode || node.Feet.Y > feet.Y && Math.Abs(node.Feet.X - feet.X) == 1 && !BodyFitsLevel(node.Feet.X, feet.Y));
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

	static bool BelowAdjacentLedge(Point feet, PathNode node) =>
		Math.Abs(node.Feet.X - feet.X) == 1 && feet.Y > node.Feet.Y && TileQuery.IsSolid(node.Feet.X, node.Feet.Y + 1);

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
	const float StepUpPixels = 16f;

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

	static bool OvershootsIntoDeadlyDrop(NPC body, Point feet, PathNode node, MotorIntent intent, int maxFall, bool launchesNext)
	{
		if (intent.Jump || intent.Fly || intent.HookAt != null || intent.BridgeTile != null || node.Arrival is MoveKind.Jump or MoveKind.Bridge or MoveKind.Pillar or MoveKind.Fly or MoveKind.Hook or MoveKind.Leap || body.wet)
			return false;
		int heading = intent.MoveDirection != 0 ? intent.MoveDirection : Math.Sign(body.velocity.X);
		if (heading == 0)
			return false;
		int aheadX = heading > 0 ? feet.X + 2 : feet.X - 1;
		float aimEdge = AimX(node) + heading * body.width / 2f;
		if (!launchesNext && CoastsPast(body, node, heading) is int beyond && DropBelow(beyond, node.Feet.Y) > maxFall)
			return true;
		bool staysOutOfAhead = heading > 0 ? (int)((aimEdge - 1f) / 16f) < aheadX : (int)(aimEdge / 16f) > aheadX;
		if (intent.MoveDirection == heading && staysOutOfAhead)
			return false;
		int depth = DropBelow(aheadX, feet.Y);
		if (depth <= maxFall)
			return false;
		bool insideNodeFootprint = aheadX >= node.Feet.X && aheadX <= node.Feet.X + 1;
		if (insideNodeFootprint && node.Arrival == MoveKind.Drop)
			return false;
		bool steppingOntoNode = node.Feet.Y == feet.Y && PathFinder.IsSupportedNow(node.Feet.X, node.Feet.Y);
		return !insideNodeFootprint || !steppingOntoNode;
	}

	static int? CoastsPast(NPC body, PathNode node, int heading)
	{
		if (body.velocity.X * heading <= 0f)
			return null;
		int beyond = heading > 0 ? node.Feet.X + 2 : node.Feet.X - 1;
		float coastEdge = body.Center.X + heading * (body.width / 2f + body.velocity.X * body.velocity.X / (2f * AirSlowdown));
		bool crosses = heading > 0 ? coastEdge >= beyond * 16f + body.width / 2f : coastEdge <= (beyond + 1) * 16f - body.width / 2f;
		return crosses ? beyond : null;
	}

	static int DropBelow(int x, int feetY)
	{
		int depth = 0;
		while (depth <= PathFinder.MaxSafeFall * 4 && !TileQuery.CanStandOn(x, feetY + depth + 1) && Main.tile[x, feetY + depth + 1].LiquidAmount == 0)
			depth++;
		return TileQuery.IsDangerousLiquid(x, feetY + depth + 1) ? int.MaxValue : depth;
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

	static int HoldTicksForRise(int rise) => rise <= 1 ? 1 : (rise * 16 - 21) / 5 + 2;

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
