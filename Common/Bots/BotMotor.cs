using System;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class BotMotor
{
	const float JumpSpeed = 5.01f;
	const int JumpHoldTicks = 15;
	const int MaxJumpableWall = 4;
	const float PlayerGravity = 0.4f;
	const float NpcBaseGravity = 0.3f;
	const int ProgressCheckInterval = 60;
	const int StalledChecksBeforeStuck = 3;
	const float MeaningfulProgress = 12f;
	const float RopeClimbSpeed = 3f;
	const float RopeLeapSpeed = 5f;
	const float JumpReachPixels = 16f * 5f;
	const float SurfaceForAirBelow = 0.6f;

	const int HazardEscapeSearch = 4;

	readonly Bot bot;
	readonly PathFollower follower = new();
	Vector2? destination;
	float arriveRadius;
	int jumpHold;
	int progressTimer;
	int stalledChecks;
	float closestDistance = float.MaxValue;

	public BotMotor(Bot bot) => this.bot = bot;

	public bool HasArrived { get; private set; }
	public bool IsStuck
	{
		get
		{
			bool stuck = stalledChecks >= StalledChecksBeforeStuck;
			if (stuck && !stuckReported)
			{
				stuckReported = true;
				bot.Watchdog.ReportNavigationFailure();
			}
			return stuck;
		}
	}

	bool stuckReported;
	public bool WantsToDropThroughPlatforms { get; private set; }
	public Point? TileToDig { get; private set; }
	public Point? ScaffoldTile { get; private set; }
	public Point? RopeTile { get; private set; }
	public Point? BridgeTile { get; private set; }
	public bool HasRoute => destination != null && follower.HasPath;
	public Vector2? Destination => destination;
	public bool ScaffoldingAllowed { get; set; }
	public bool ClimbingStraightUp { get; set; }
	public bool Tunneling { get; private set; }

	bool jumpRequested;

	public void RequestJump() => jumpRequested = true;

	public void TunnelTo(Vector2 target, float radius)
	{
		MoveTo(target, radius);
		Tunneling = true;
		ResetProgress();
	}

	public void MoveTo(Vector2 target, float radius = 16f)
	{
		if (destination is not Vector2 current || Vector2.DistanceSquared(current, target) > 32f * 32f)
		{
			bool retryingSameTarget = lastFailedDestination is Vector2 failed && Vector2.DistanceSquared(failed, target) < 64f * 64f;
			ResetProgress();
			follower.Invalidate();
			follower.ExtendNextSearch = retryingSameTarget;
			Tunneling = false;
		}
		destination = target;
		arriveRadius = radius;
	}

	Vector2? lastFailedDestination;

	public void Stop()
	{
		if (stalledChecks >= StalledChecksBeforeStuck)
			lastFailedDestination = destination;
		destination = null;
		HasArrived = false;
		Tunneling = false;
		ResetProgress();
		follower.Invalidate();
	}

	void ResetProgress()
	{
		stuckReported = false;
		stalledChecks = 0;
		progressTimer = 0;
		closestDistance = float.MaxValue;
	}

	public string Describe()
	{
		var npc = bot.Npc;
		string body = npc == null ? "no body" : $"feet={PathFinder.FeetOf(npc)} vel={npc.velocity} wet={npc.wet} grapple={bot.Grapple.IsAttached}";
		string target = destination is Vector2 d ? d.ToTileCoordinates().ToString() : "none";
		return $"dest={target} arrived={HasArrived} stalled={stalledChecks} {body} {follower.Describe()}";
	}

	public void Face(Vector2 target)
	{
		var npc = bot.Npc;
		if (npc != null)
			npc.direction = target.X >= npc.Center.X ? 1 : -1;
	}

	public void Tick(NPC npc)
	{
		npc.GravityMultiplier = MultipliableFloat.One * (PlayerGravity / NpcBaseGravity);
		TileToDig = null;
		ScaffoldTile = null;
		RopeTile = null;
		BridgeTile = null;
		WantsToDropThroughPlatforms = false;

		bot.Grapple.Tick(npc, destination);
		if (bot.Grapple.IsAttached)
		{
			TrackProgress(npc);
			ScaffoldingAllowed = false;
			return;
		}

		int moveDirection = 0;
		bool wantsJump = false;
		int jumpHoldTicks = JumpHoldTicks;
		if (ClimbingStraightUp)
			ClimbUpStep(npc, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
		else if (destination is Vector2 tunnelGoal && Tunneling)
			TunnelStep(npc, tunnelGoal, ref moveDirection, ref wantsJump);
		else if (destination is Vector2 target)
			Steer(npc, target, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
		else
			moveDirection = EscapeHazard(npc);
		if (jumpRequested)
		{
			wantsJump = true;
			jumpHoldTicks = JumpHoldTicks;
			jumpRequested = false;
		}
		if (npc.wet && bot.Vitals.BreathFraction < SurfaceForAirBelow)
		{
			wantsJump = true;
			jumpHoldTicks = JumpHoldTicks;
		}

		Run(npc, moveDirection);
		Jump(npc, wantsJump, jumpHoldTicks);
		Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
		npc.spriteDirection = npc.direction;
		TrackProgress(npc);

		if (TileToDig is Point dig)
			PassThrough(dig, npc.direction);
		else if (BridgeTile is Point bridge)
			bot.Hands.PlaceBridge(bridge);
		else if (RopeTile is Point rope)
			bot.Hands.PlaceRope(rope);
		else if (ScaffoldTile is Point scaffold)
			PlaceFoothold(scaffold);
		ScaffoldingAllowed = false;
	}

	void TunnelStep(NPC npc, Vector2 target, ref int moveDirection, ref bool wantsJump)
	{
		var delta = target - npc.Center;
		if (Math.Abs(delta.X) <= arriveRadius && Math.Abs(delta.Y) <= arriveRadius + 24f)
		{
			HasArrived = true;
			return;
		}
		HasArrived = false;
		int pickPower = bot.Hands.BestPickaxe?.pick ?? 0;
		var body = BodyTiles.Of(npc);
		bool grounded = npc.velocity.Y == 0f || npc.wet;
		bool mostlyDown = delta.Y > Math.Abs(delta.X) * 0.5f;
		if (mostlyDown && grounded && TunnelTile(body.Left, body.Right, body.Bottom + 1, body.Bottom + 1, pickPower) is Point floor)
		{
			TileToDig = floor;
			return;
		}
		if (mostlyDown && !grounded)
			return;
		int direction = delta.X >= 0f ? 1 : -1;
		int column = direction > 0 ? body.Right + 1 : body.Left - 1;
		int top = delta.Y < -16f ? body.Top - 1 : body.Top;
		int bottom = delta.Y > 16f ? body.Bottom + 1 : body.Bottom;
		if (TunnelTile(column, column, top, bottom, pickPower) is Point wall)
		{
			TileToDig = wall;
			return;
		}
		moveDirection = direction;
		wantsJump = grounded && delta.Y < -16f;
	}

	static Point? TunnelTile(int left, int right, int top, int bottom, int pickPower)
	{
		for (int y = top; y <= bottom; y++)
		{
			for (int x = left; x <= right; x++)
			{
				if (!TileQuery.IsSolid(x, y))
					continue;
				if (!CanBreak(x, y, pickPower) || LavaNear(x, y))
					return null;
				return new Point(x, y);
			}
		}
		return null;
	}

	static bool LavaNear(int x, int y)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -1; dy <= 1; dy++)
			{
				var tile = Main.tile[x + dx, y + dy];
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava)
					return true;
			}
		}
		return false;
	}

	const int ClimbJumpHoldTicks = 4;

	const float ClimbCenteringTolerance = 3f;

	void ClimbUpStep(NPC npc, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		var feet = PathFinder.FeetOf(npc);
		int pickPower = bot.Hands.BestPickaxe?.pick ?? 0;
		if (BestClimbColumn(feet, pickPower) is not int column)
			return;
		bool grounded = npc.velocity.Y == 0f;
		float offset = PathFinder.CenterXOf(column) - npc.Center.X;
		if (grounded && Math.Abs(offset) > ClimbCenteringTolerance)
		{
			moveDirection = Math.Sign(offset);
			return;
		}
		if (FirstSolidAbove(column, feet.Y) is Point ceiling)
		{
			TileToDig = ceiling;
			return;
		}
		if (!bot.Hands.HasBridgeMaterial)
		{
			TileToDig = DiggableWallBeside(column, feet.Y, pickPower);
			return;
		}
		wantsJump = grounded || jumpHold > 0;
		jumpHoldTicks = ClimbJumpHoldTicks;
		var body = BodyTiles.Of(npc);
		bool risenOneTileAboveSupport = !grounded && !PathFinder.IsSupportedNow(column, body.Bottom) && PathFinder.IsSupportedNow(column, body.Bottom + 1);
		if (risenOneTileAboveSupport)
			ScaffoldTile = new Point(column, body.Bottom + 1);
	}

	int? BestClimbColumn(Point feet, int pickPower)
	{
		int? best = null;
		int bestCost = int.MaxValue;
		for (int column = feet.X - 1; column <= feet.X + 1; column++)
		{
			int cost = Math.Abs(column - feet.X);
			bool blocked = false;
			for (int x = column; x <= column + 1 && !blocked; x++)
			{
				for (int y = feet.Y - 4; y <= feet.Y; y++)
				{
					if (!TileQuery.IsSolid(x, y))
						continue;
					if (!CanBreak(x, y, pickPower))
						blocked = true;
					cost += 3;
				}
			}
			if (!blocked && cost < bestCost)
			{
				best = column;
				bestCost = cost;
			}
		}
		return best;
	}

	static Point? FirstSolidAbove(int column, int feetY)
	{
		for (int y = feetY; y >= feetY - 4; y--)
		{
			for (int x = column; x <= column + 1; x++)
			{
				if (TileQuery.IsSolid(x, y))
					return new Point(x, y);
			}
		}
		return null;
	}

	static Point? DiggableWallBeside(int column, int feetY, int pickPower)
	{
		foreach (int x in new[] { column - 1, column + 2 })
		{
			for (int y = feetY; y >= feetY - 2; y--)
			{
				if (TileQuery.IsSolid(x, y) && CanBreak(x, y, pickPower))
					return new Point(x, y);
			}
		}
		return null;
	}

	static bool CanBreak(int x, int y, int pickPower) =>
		CanDigThrough(new Point(x, y)) && PickRequirement.MinimumPick(Main.tile[x, y].TileType, x, y) <= pickPower;

	void PlaceFoothold(Point tile)
	{
		bool insideSettlement = bot.Faction.Settlement?.Contains(tile.X, tile.Y) == true;
		if (insideSettlement)
			bot.Hands.PlaceScaffold(tile);
		else
			bot.Hands.PlaceBridge(tile);
	}

	void PassThrough(Point tile, int direction)
	{
		int tileType = TileQuery.TypeAt(tile);
		if (tileType == TileID.ClosedDoor)
		{
			WorldGen.OpenDoor(tile.X, tile.Y, direction);
			return;
		}
		if (CanDigThrough(tile))
			bot.Hands.Dig(tile);
	}

	void Steer(NPC npc, Vector2 target, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		var delta = target - npc.Center;
		if (Math.Abs(delta.X) <= arriveRadius && Math.Abs(delta.Y) <= arriveRadius + 24f)
		{
			HasArrived = true;
			moveDirection = EscapeHazard(npc);
			return;
		}
		HasArrived = false;

		var intent = follower.Follow(npc, target, arriveRadius, Abilities());
		if (follower.HasPath)
		{
			ApplyIntent(npc, intent, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
			return;
		}
		SteerGreedy(npc, target, delta, ref moveDirection, ref wantsJump);
	}

	const float AffordableFallDamageShare = 0.4f;
	const int FallDamagePerTile = 10;
	const int MaxMeasuredDrop = 200;

	int AffordableFall => PathFinder.MaxSafeFall + (int)(bot.Avatar.statLife * AffordableFallDamageShare / FallDamagePerTile);

	public NavigationAbilities Abilities()
	{
		int pickPower = bot.Hands.BestPickaxe?.pick ?? 0;
		bool hasBridgeMaterial = bot.Hands.HasBridgeMaterial;
		return new NavigationAbilities(pickPower, pickPower > 0, hasBridgeMaterial, ScaffoldingAllowed && hasBridgeMaterial, bot.Hands.HasRope, AffordableFall);
	}

	void ApplyIntent(NPC npc, MotorIntent intent, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		moveDirection = intent.MoveDirection;
		if (moveDirection != 0)
			npc.direction = moveDirection;
		wantsJump = intent.Jump;
		if (intent.JumpHoldTicks > 0)
			jumpHoldTicks = intent.JumpHoldTicks;
		TileToDig = intent.DigTile;
		BridgeTile = intent.BridgeTile;
		ScaffoldTile = intent.ScaffoldTile;
		WantsToDropThroughPlatforms = intent.DropThroughPlatforms;
		if (intent.ClimbDirection != 0)
		{
			if (intent.ClimbColumn >= 0)
				npc.position.X = PathFinder.CenterXOf(intent.ClimbColumn) - npc.width / 2f;
			if (intent.ClimbDirection < 0 && intent.ClimbColumn >= 0 && NextRopeTile(npc, intent.ClimbColumn) is Point rope)
			{
				RopeTile = rope;
				npc.velocity = Vector2.Zero;
				return;
			}
			if (intent.ClimbDirection > 0 && intent.ClimbColumn >= 0 && !PrepareRopeBelow(npc, intent.ClimbColumn))
			{
				npc.velocity = Vector2.Zero;
				return;
			}
			if (intent.ClimbDirection > 0)
				WantsToDropThroughPlatforms = true;
			npc.velocity = new Vector2(0f, intent.ClimbDirection * RopeClimbSpeed);
		}
	}

	bool PrepareRopeBelow(NPC npc, int column)
	{
		var body = BodyTiles.Of(npc);
		int y = body.Bottom + 1;
		if (Main.tile[column, y].HasTile || !bot.Hands.HasRope)
			return true;
		bool anchored = IsRope(column, y - 1) || Main.tile[column, y - 1].HasTile;
		if (anchored)
			RopeTile = new Point(column, y);
		else
			BridgeTile = new Point(column, y);
		return false;
	}

	Point? NextRopeTile(NPC npc, int column)
	{
		var body = BodyTiles.Of(npc);
		for (int y = body.Bottom; y >= body.Top - 1; y--)
		{
			if (!IsRope(column, y))
				return bot.Hands.HasRope && !Main.tile[column, y].HasTile ? new Point(column, y) : null;
		}
		return null;
	}

	int EscapeHazard(NPC npc)
	{
		var feet = PathFinder.FeetOf(npc);
		if (!PathFinder.NearHurtfulTile(feet.X, feet.Y))
			return 0;
		for (int distance = 1; distance <= HazardEscapeSearch; distance++)
		{
			foreach (int dir in new[] { -1, 1 })
			{
				int x = feet.X + dir * distance;
				if (TileQuery.CanStandOn(x, feet.Y + 1) && !TileQuery.IsSolid(x, feet.Y) && !PathFinder.NearHurtfulTile(x, feet.Y))
					return dir;
			}
		}
		return 0;
	}

	void SteerGreedy(NPC npc, Vector2 target, Vector2 delta, ref int moveDirection, ref bool wantsJump)
	{

		var body = BodyTiles.Of(npc);
		bool grounded = npc.velocity.Y == 0f;
		bool targetBelow = delta.Y > 40f;
		bool targetAbove = delta.Y < -40f;
		bool alignedHorizontally = Math.Abs(delta.X) < 12f;

		if (alignedHorizontally && targetBelow)
		{
			Descend(body);
			return;
		}
		if (alignedHorizontally && targetAbove)
		{
			Ascend(npc, body, grounded, -delta.Y, target, ref wantsJump);
			return;
		}

		moveDirection = Math.Sign(delta.X);
		npc.direction = moveDirection;
		int aheadX = moveDirection > 0 ? body.Right + 1 : body.Left - 1;
		int wallHeight = WallHeight(aheadX, body.Bottom);

		if (wallHeight == 0)
		{
			if (DropDepth(aheadX, body.Bottom) > AffordableFall)
			{
				moveDirection = 0;
				return;
			}
			if (targetBelow && grounded && delta.Y > Math.Abs(delta.X) * 0.5f && TileQuery.IsSolid(aheadX, body.Bottom + 1))
				TileToDig = new Point(aheadX, body.Bottom + 1);
			return;
		}
		if (wallHeight <= MaxJumpableWall && HasHeadroom(body, wallHeight) && !targetBelow)
		{
			wantsJump = grounded || jumpHold > 0;
			return;
		}
		var obstacle = targetAbove ? StaircaseUpTile(body, aheadX)
			: targetBelow ? FirstSolid(aheadX, body.Top + 1, body.Bottom + 1)
			: FirstSolid(aheadX, body.Top, body.Bottom);
		if (obstacle is Point blocked && targetAbove && ScaffoldingAllowed && !CanDigThrough(blocked))
		{
			moveDirection = 0;
			Ascend(npc, body, grounded, -delta.Y, target, ref wantsJump);
			return;
		}
		TileToDig = obstacle;
	}

	static bool CanDigThrough(Point tile)
	{
		int tileType = TileQuery.TypeAt(tile);
		return tileType < 0 || tileType == TileID.ClosedDoor || (!Main.tileFrameImportant[tileType] && !HouseInspector.IsProtected(tile.X, tile.Y));
	}

	static int DropDepth(int x, int feetY)
	{
		int depth = 0;
		while (depth <= MaxMeasuredDrop && !TileQuery.CanStandOn(x, feetY + depth + 1) && Main.tile[x, feetY + depth + 1].LiquidAmount == 0)
			depth++;
		return depth;
	}

	void Descend(BodyTiles body)
	{
		for (int x = body.Left; x <= body.Right; x++)
		{
			if (TileQuery.IsPlatform(x, body.Bottom + 1))
				WantsToDropThroughPlatforms = true;
			else if (TileQuery.IsSolid(x, body.Bottom + 1))
			{
				TileToDig = new Point(x, body.Bottom + 1);
				return;
			}
		}
	}

	void Ascend(NPC npc, BodyTiles body, bool grounded, float rise, Vector2 target, ref bool wantsJump)
	{
		for (int x = body.Left; x <= body.Right; x++)
		{
			if (TileQuery.IsSolid(x, body.Top - 1))
			{
				TileToDig = new Point(x, body.Top - 1);
				return;
			}
		}
		if (rise > JumpReachPixels && bot.Grapple.TryLatch(npc, target))
			return;
		if (ClimbRope(npc, body, grounded))
			return;
		wantsJump = grounded || jumpHold > 0;
		int centerX = (int)(npc.Center.X / 16f);
		bool risenOneTileAboveSupport = !grounded && !TileQuery.CanStandOn(centerX, body.Bottom + 1) && TileQuery.CanStandOn(centerX, body.Bottom + 2);
		if (ScaffoldingAllowed && risenOneTileAboveSupport)
			ScaffoldTile = new Point(centerX, body.Bottom + 1);
	}

	bool ClimbRope(NPC npc, BodyTiles body, bool grounded)
	{
		int centerX = (int)(npc.Center.X / 16f);
		bool onRope = false;
		for (int y = body.Top; y <= body.Bottom; y++)
			onRope |= IsRope(centerX, y);
		if (onRope)
		{
			bool ropeEndsHere = !IsRope(centerX, body.Top - 1) && !bot.Hands.HasRope;
			if (ropeEndsHere)
			{
				npc.velocity.Y = -RopeLeapSpeed;
				return false;
			}
			npc.velocity = new Vector2(0f, -RopeClimbSpeed);
			if (!Main.tile[centerX, body.Top - 1].HasTile)
				RopeTile = new Point(centerX, body.Top - 1);
			return true;
		}
		if (!grounded || !bot.Hands.HasRope || Main.tile[centerX, body.Bottom].HasTile)
			return false;
		RopeTile = new Point(centerX, body.Bottom);
		return true;
	}

	static bool OnRope(NPC npc)
	{
		var body = BodyTiles.Of(npc);
		for (int x = body.Left; x <= body.Right; x++)
		{
			for (int y = body.Top; y <= body.Bottom; y++)
			{
				if (IsRope(x, y))
					return true;
			}
		}
		return false;
	}

	static bool IsRope(int x, int y) => WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType];

	static Point? StaircaseUpTile(BodyTiles body, int aheadX)
	{
		for (int x = body.Left; x <= body.Right; x++)
		{
			if (TileQuery.IsSolid(x, body.Top - 1))
				return new Point(x, body.Top - 1);
		}
		return FirstSolid(aheadX, body.Top - 1, body.Bottom - 1);
	}

	static Point? FirstSolid(int x, int fromY, int toY)
	{
		for (int y = fromY; y <= toY; y++)
		{
			if (TileQuery.IsSolid(x, y))
				return new Point(x, y);
		}
		return null;
	}

	static int WallHeight(int x, int feetY)
	{
		int height = 0;
		while (height <= MaxJumpableWall + 1 && TileQuery.IsSolid(x, feetY - height))
			height++;
		return height;
	}

	static bool HasHeadroom(BodyTiles body, int rise)
	{
		for (int x = body.Left; x <= body.Right; x++)
		{
			for (int y = body.Top - rise; y < body.Top; y++)
			{
				if (TileQuery.IsSolid(x, y))
					return false;
			}
		}
		return true;
	}

	void Run(NPC npc, int direction)
	{
		var avatar = bot.Avatar;
		float maxSpeed = avatar.maxRunSpeed * avatar.moveSpeed;
		float acceleration = avatar.runAcceleration * avatar.moveSpeed;
		if (direction != 0)
		{
			if (npc.velocity.X * direction < maxSpeed)
				npc.velocity.X += acceleration * direction;
			else if (npc.velocity.Y == 0f)
				npc.velocity.X = maxSpeed * direction;
			return;
		}
		if (Math.Abs(npc.velocity.X) <= avatar.runSlowdown)
			npc.velocity.X = 0f;
		else
			npc.velocity.X -= avatar.runSlowdown * Math.Sign(npc.velocity.X);
	}

	void Jump(NPC npc, bool wantsJump, int holdTicks)
	{
		if (!wantsJump)
		{
			jumpHold = 0;
			return;
		}
		if (jumpHold == 0 && (npc.velocity.Y == 0f || npc.wet || OnRope(npc)))
		{
			npc.velocity.Y = -JumpSpeed;
			jumpHold = holdTicks;
		}
		else if (jumpHold > 0)
		{
			npc.velocity.Y = -JumpSpeed;
			jumpHold--;
		}
	}

	void TrackProgress(NPC npc)
	{
		if (destination is not Vector2 target || HasArrived)
		{
			ResetProgress();
			return;
		}
		if (++progressTimer < ProgressCheckInterval)
			return;
		progressTimer = 0;
		float distance = Vector2.Distance(npc.Center, target);
		if (distance < closestDistance - MeaningfulProgress || bot.Hands.IsWorking)
		{
			closestDistance = Math.Min(distance, closestDistance);
			stalledChecks = 0;
			return;
		}
		stalledChecks++;
	}
}

public readonly record struct BodyTiles(int Left, int Right, int Top, int Bottom)
{
	public static BodyTiles Of(Entity entity) => new(
		(int)(entity.position.X / 16f),
		(int)((entity.position.X + entity.width - 1) / 16f),
		(int)(entity.position.Y / 16f),
		(int)((entity.position.Y + entity.height - 1) / 16f));
}
