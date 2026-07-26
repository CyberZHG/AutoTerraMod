using System;
using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class BotMotor
{
	const int JumpHoldTicks = 99;
	const int MaxJumpableWall = 4;
	const float PlayerGravity = 0.4f;
	const float NpcBaseGravity = 0.3f;
	const int ProgressCheckInterval = 60;
	const int StalledChecksBeforeStuck = 3;
	const float MeaningfulProgress = 12f;
	const float RopeClimbSpeed = 3f;
	const float RopeClimbMax = 8f;
	const float RopeDescendMax = 10f;
	const uint RopeLeapCooldown = 30;
	uint ropeLeapAt;

	static float RopeVelocity(float current, int direction)
	{
		if (direction < 0)
		{
			if (current > 0f)
				current *= 0.7f;
			current -= current > -RopeClimbSpeed ? 0.2f : 0.02f;
			return Math.Max(current, -RopeClimbMax);
		}
		if (current < 0f)
			current *= 0.7f;
		current += current < RopeClimbSpeed ? 0.2f : 0.1f;
		return Math.Min(current, RopeDescendMax);
	}

	const uint RopeSpeedMemoryTicks = 20;
	float ropeSpeed;
	uint ropeSpeedAt;

	float NextRopeSpeed(int direction)
	{
		if (Main.GameUpdateCount - ropeSpeedAt > RopeSpeedMemoryTicks)
			ropeSpeed = 0f;
		ropeSpeedAt = Main.GameUpdateCount;
		return ropeSpeed = RopeVelocity(ropeSpeed, direction);
	}

	static bool RopeAtHead(NPC npc, int column)
	{
		int row = (int)((npc.position.Y - 16f) / 16f);
		return IsRope(column, row) || IsRope(column + 1, row);
	}
	const float CreepSpeed = 1.2f;
	const int FlightIntentTicks = 2;
	const int SprintLookahead = 6;
	const float RopeLeapSpeed = 5f;
	const float JumpReachPixels = 16f * 5f;
	const float SurfaceForAirBelow = 0.35f;

	const int HazardEscapeSearch = 4;

	readonly Bot bot;
	readonly PathFollower follower = new();
	readonly MacroRoute longRoute = new();
	bool needsWholeRoute;
	const int StalledChecksBeforeWholeRoute = 2;
	const float LegRadius = 32f;
	Vector2? destination;
	float arriveRadius;
	int jumpHold;
	int progressTimer;
	int stalledChecks;
	float closestDistance = float.MaxValue;
	Vector2? progressAim;

	public BotMotor(Bot bot) => this.bot = bot;

	public bool HasArrived { get; private set; }
	public bool IsStalled => stalledChecks >= StalledChecksBeforeStuck;
	public bool HasNoRoute => follower.HasNoRoute;
	public bool IsFlying => flightTicks > 0;
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

	bool dropRequested;

	public void DropThroughPlatform() => dropRequested = true;
	public Point? TileToDig { get; private set; }

	int stickyBreak;

	const float IceBreakingFallSpeed = 7f;

	static void BreakThinIce(NPC body)
	{
		if (body.velocity.Y <= IceBreakingFallSpeed)
			return;
		var next = body.position + body.velocity;
		int left = (int)(next.X / 16f), right = (int)((next.X + body.width) / 16f);
		int top = (int)((body.position.Y + body.height + 1f) / 16f);
		for (int x = left; x <= right; x++)
		{
			for (int y = top; y <= top + 1; y++)
			{
				if (WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && !Main.tile[x, y].IsActuated && Main.tile[x, y].TileType == TileID.BreakableIce && !WorldGen.SolidTile(x, y - 1))
					WorldGen.KillTile(x, y);
			}
		}
	}

	public void ApplyTerrainHazards(NPC body)
	{
		BreakThinIce(body);
		Collision.SwitchTiles(bot.Avatar, body.position, body.width, body.height, body.oldPosition, 1);
		var sticky = Collision.StickyTiles(body.position, body.velocity, body.width, body.height);
		if (sticky.X == -1f || sticky.Y == -1f)
			return;
		int x = (int)sticky.X, y = (int)sticky.Y;
		if (Main.tile[x, y].TileType == TileID.Cobweb && bot.Hands.Ready)
			bot.Hands.Cut(new Point(x, y));
		else if (Main.tile[x, y].TileType == TileID.Cobweb && body.velocity != Vector2.Zero && ++stickyBreak > Main.rand.Next(20, 100))
		{
			stickyBreak = 0;
			WorldGen.KillTile(x, y);
		}
		body.velocity.X = MathHelper.Clamp(body.velocity.X, -1f, 1f) * (Math.Abs(body.velocity.X) > 0.75f ? 0.85f : 0.6f);
		body.velocity.Y = MathHelper.Clamp(body.velocity.Y, -5f, 1f) * (body.velocity.Y < 0f ? 0.96f : 0.3f);
	}

	const int CeilingSearchRows = 3;

	Point? CeilingToBreakForAir(NPC npc)
	{
		int head = (int)(npc.position.Y - 1f) / 16;
		int left = (int)npc.position.X / 16;
		int right = (int)(npc.position.X + npc.width - 1f) / 16;
		for (int y = head; y > head - CeilingSearchRows; y--)
		{
			bool open = false;
			for (int x = left; x <= right; x++)
			{
				var tile = Main.tile[x, y];
				if (!TileQuery.IsSolid(x, y))
				{
					open |= tile.LiquidAmount < byte.MaxValue;
					continue;
				}
				if (!InAnySettlement(x, y) && CanDigThrough(new Point(x, y)))
					return new Point(x, y);
			}
			if (open)
				return null;
		}
		return null;
	}

	static bool InAnySettlement(int x, int y)
	{
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (faction.Settlement is { } settlement && settlement.Contains(x, y))
				return true;
		}
		return false;
	}
	public Point? ScaffoldTile { get; private set; }
	public Point? RopeTile { get; private set; }
	public Point? BridgeTile { get; private set; }
	public bool HasRoute => destination != null && follower.HasPath;
	public Vector2? Destination => destination;
	public bool ScaffoldingAllowed { get; set; }
	public bool PlacementForbidden { get; set; }
	bool MayPlaceRope => bot.Hands.HasRope && !PlacementForbidden && bot.Npc?.wet != true;
	public bool ClimbingStraightUp { get; set; }
	public bool Tunneling { get; private set; }

	bool jumpRequested;

	public void RequestJump() => jumpRequested = true;

	public void TunnelTo(Vector2 target, float radius)
	{
		if (Tunneling && destination is Vector2 current && Vector2.DistanceSquared(current, target) <= 32f * 32f)
		{
			arriveRadius = radius;
			return;
		}
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
			needsWholeRoute = retryingSameTarget;
			Tunneling = false;
			pylonHop = PlanPylonHop(target);
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
		pylonHop = null;
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
		progressAim = null;
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
		sprinting = false;
		ScaffoldTile = null;
		RopeTile = null;
		BridgeTile = null;
		WantsToDropThroughPlatforms = false;

		bot.Grapple.Tick(npc, destination);
		if (bot.Grapple.ConsumeClimbRelease())
			jumpHold = bot.Vitals.JumpHeight;
		if (bot.Grapple.IsAttached && !bot.Grapple.HangingInPlace)
		{
			TrackProgress(npc);
			ScaffoldingAllowed = false;
			PlacementForbidden = false;
			return;
		}

		int moveDirection = 0;
		bool wantsJump = false;
		int jumpHoldTicks = JumpHoldTicks;
		if (ClimbingStraightUp)
			ClimbUpStep(npc, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
		else if (destination is Vector2 tunnelGoal && Tunneling)
			TunnelStep(npc, tunnelGoal, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
		else if (destination is Vector2 target)
			Steer(npc, target, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
		else
			moveDirection = EscapeHazard(npc);
		if (StandingInHazard() && EscapeHazard(npc) is int away && away != 0)
		{
			moveDirection = away;
			wantsJump = npc.velocity.Y == 0f;
		}
		if (jumpRequested)
		{
			wantsJump = true;
			jumpHoldTicks = JumpHoldTicks;
			jumpRequested = false;
		}
		RefuelWings(npc);
		ArrestFall(npc, ref wantsJump);
		if (flightTicks > 0)
			wantsJump = true;
		if (npc.wet && bot.Vitals.BreathFraction < SurfaceForAirBelow)
		{
			wantsJump = true;
			jumpHoldTicks = JumpHoldTicks;
			if (CeilingToBreakForAir(npc) is Point ceiling)
				TileToDig = ceiling;
		}

		Run(npc, moveDirection);
		Jump(npc, wantsJump, jumpHoldTicks);
		if (flightTicks > 0)
			flightTicks--;
		HoldRopeOverDrop(npc, wantsJump);
		if (dropRequested)
		{
			WantsToDropThroughPlatforms = true;
			dropRequested = false;
		}
		if (!WantsToDropThroughPlatforms)
			Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
		npc.spriteDirection = npc.direction;
		TrackProgress(npc);

		if (TileToDig is Point dig)
			PassThrough(dig, npc.direction);
		else if (!PlacementForbidden)
			PlacePlannedSupport();
		ScaffoldingAllowed = false;
		PlacementForbidden = false;
	}

	void PlacePlannedSupport()
	{
		if ((BridgeTile ?? RopeTile ?? ScaffoldTile) is Point support && World.Highway.InAnyClearance(support.X, support.Y))
			return;
		if (BridgeTile is Point bridge)
			bot.Hands.PlaceBridge(bridge);
		else if (RopeTile is Point rope && bot.Hands.PlaceRope(rope))
			World.RopeLedger.Note(rope);
		else if (ScaffoldTile is Point scaffold)
			PlaceFoothold(scaffold);
	}

	void TunnelStep(NPC npc, Vector2 target, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		var delta = target - npc.Center;
		if (Math.Abs(delta.X) <= arriveRadius && Math.Abs(delta.Y) <= arriveRadius + 24f)
		{
			HasArrived = true;
			return;
		}
		HasArrived = false;
		if (bot.Hands.Explosives.BlastCenter is Vector2 blast)
		{
			if (Vector2.Distance(npc.Center, blast) < Explosives.SafeDistance)
				moveDirection = npc.Center.X >= blast.X ? 1 : -1;
			return;
		}
		if (Math.Abs(delta.X) < 16f && delta.Y < 0f)
		{
			ClimbUpStep(npc, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
			return;
		}
		int pickPower = bot.Hands.BestPickaxe?.pick ?? 0;
		var body = BodyTiles.Of(npc);
		bool grounded = npc.velocity.Y == 0f || npc.wet;
		bool mostlyDown = delta.Y > Math.Abs(delta.X) * 0.5f;
		if (mostlyDown && grounded && TunnelTile(body.Left, body.Right, body.Bottom + 1, body.Bottom + 1, pickPower) is Point floor && DropDepth(floor.X, floor.Y) <= AffordableFall)
		{
			TileToDig = floor;
			return;
		}
		if (mostlyDown && !grounded)
			return;
		int direction = delta.X >= 0f ? 1 : -1;
		int column = direction > 0 ? body.Right + 1 : body.Left - 1;
		int top = delta.Y < -16f && !Unbreakable(column, body.Top - 1, pickPower) ? body.Top - 1 : body.Top;
		int bottom = delta.Y > 16f ? body.Bottom + 1 : body.Bottom;
		if (TunnelTile(column, column, top, bottom, pickPower) is Point wall)
		{
			TileToDig = wall;
			return;
		}
		if (FirstSolid(column, top, bottom) is Point blocker && Unbreakable(blocker.X, blocker.Y, pickPower) && bot.Hands.Explosives.Light(blocker))
			return;
		if (grounded && DropDepth(column, body.Bottom) > AffordableFall)
		{
			if (bot.Hands.HasBridgeMaterial && !TileQuery.CanStandOn(column, body.Bottom + 1))
				BridgeTile = new Point(column, body.Bottom + 1);
			return;
		}
		moveDirection = direction;
		wantsJump = grounded && delta.Y < -16f;
	}

	Point? TunnelTile(int left, int right, int top, int bottom, int pickPower)
	{
		for (int y = top; y <= bottom; y++)
		{
			for (int x = left; x <= right; x++)
			{
				if (!TileQuery.IsSolid(x, y))
					continue;
				if (!CanBreak(x, y, pickPower) || LavaNear(x, y) || TileQuery.SupportsShrine(x, y))
					return null;
				return new Point(x, y);
			}
		}
		return null;
	}

	bool Unbreakable(int x, int y, int pickPower) => TileQuery.IsSolid(x, y) && !CanBreak(x, y, pickPower);

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
		{
			TileToDig = DiggableWallBeside(feet.X, feet.Y, pickPower);
			return;
		}
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
		if (FirstOccupiedAbove(column, feet.Y) is Point trunk)
		{
			bot.Hands.Chop(trunk);
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
		for (int column = feet.X - ClimbColumnReach; column <= feet.X + ClimbColumnReach; column++)
		{
			int cost = Math.Abs(column - feet.X);
			if (cost > 0 && (!PathFinder.IsSupportedNow(column, feet.Y) || !WalkableBetween(feet, column)))
				continue;
			bool blocked = false;
			if (FirstOccupiedAbove(column, feet.Y) is Point occupied)
			{
				blocked = !Abilities().CanChop || !TileQuery.IsChoppableTree(TileQuery.TypeAt(occupied));
				cost += 3;
			}
			for (int x = column; x <= column + 1 && !blocked; x++)
			{
				blocked = SandAbove(x, feet.Y);
				for (int y = feet.Y - 4; y <= feet.Y && !blocked; y++)
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

	const int ClimbColumnReach = 3;

	static bool WalkableBetween(Point feet, int column)
	{
		int left = Math.Min(feet.X, column);
		int right = Math.Max(feet.X, column) + 1;
		for (int x = left; x <= right; x++)
		{
			for (int y = feet.Y - 2; y <= feet.Y; y++)
			{
				if (TileQuery.IsSolid(x, y))
					return false;
			}
		}
		return true;
	}

	const int SandLookUpTiles = 10;

	static bool SandAbove(int x, int feetY)
	{
		for (int y = feetY - 1; y >= feetY - SandLookUpTiles; y--)
		{
			var tile = Main.tile[x, y];
			if (tile.HasTile && TileID.Sets.Falling[tile.TileType])
				return true;
		}
		return false;
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

	static Point? FirstOccupiedAbove(int column, int feetY)
	{
		for (int y = feetY; y >= feetY - 4; y--)
		{
			if (!TileQuery.IsSolid(column, y) && !TileQuery.FreeForPlacing(column, y))
				return new Point(column, y);
		}
		return null;
	}

	Point? DiggableWallBeside(int column, int feetY, int pickPower)
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

	bool CanBreak(int x, int y, int pickPower) =>
		!TileQuery.InOcean(x, y) && CanDigThrough(new Point(x, y)) && PickRequirement.MinimumPick(Main.tile[x, y].TileType, x, y) <= pickPower && !bot.Hands.IsReclaimable(new Point(x, y));

	void PlaceFoothold(Point tile)
	{
		if (!TileQuery.FreeForPlacing(tile.X, tile.Y))
		{
			if (TileQuery.IsChoppableTree(TileQuery.TypeAt(tile)))
				bot.Hands.Chop(tile);
			return;
		}
		bool insideSettlement = bot.Faction.Settlement?.Contains(tile.X, tile.Y) == true;
		if (insideSettlement)
			bot.Hands.PlaceScaffold(tile);
		else
			bot.Hands.PlaceBridge(tile);
	}

	bool HasTempleKey => NPC.downedPlantBoss && Inventory.Count(bot.Avatar, ItemID.TempleKey) > 0;

	void UnlockWithKey(Point tile)
	{
		var key = Inventory.Items(bot.Avatar).FirstOrDefault(item => item.type == ItemID.TempleKey);
		if (key == null || !NPC.downedPlantBoss)
			return;
		Inventory.ConsumeOne(bot.Avatar, key);
		WorldGen.UnlockDoor(tile.X, tile.Y);
		BotDiagnostics.Log(bot, $"unlocked the temple door at {tile}");
	}

	void PassThrough(Point tile, int direction)
	{
		int tileType = TileQuery.TypeAt(tile);
		if (tileType == TileID.ClosedDoor)
		{
			if (WorldGen.IsLockedDoor(tile.X, tile.Y))
				UnlockWithKey(tile);
			if (!WorldGen.OpenDoor(tile.X, tile.Y, direction))
				WorldGen.OpenDoor(tile.X, tile.Y, -direction);
			return;
		}
		if (!CanDigThrough(tile))
			return;
		if (PathFinder.RemovableBlockerOnTop(tile.X, tile.Y, Abilities().CanChop) is not Point blocker)
		{
			bot.Hands.Dig(tile);
			return;
		}
		bool chop = Main.tileAxe[TileQuery.TypeAt(blocker)];
		var result = chop ? bot.Hands.Chop(blocker) : bot.Hands.Dig(blocker);
		if (result == ToolResult.OutOfReach && bot.Npc?.velocity.Y == 0f)
			jumpRequested = true;
	}

	PylonHop? pylonHop;
	uint pylonRefusedAt;
	const int PylonRetryTicks = 3600;
	const float PylonWorthyTrip = 16f * 150f;

	PylonHop? PlanPylonHop(Vector2 target)
	{
		bool recentlyRefused = pylonRefusedAt != 0 && Main.GameUpdateCount - pylonRefusedAt < PylonRetryTicks;
		if (recentlyRefused || Vector2.Distance(bot.Center, target) < PylonWorthyTrip)
			return null;
		return PylonTravel.Plan(bot.Center, target);
	}

	bool HopThroughPylon(ref Vector2 target)
	{
		if (pylonHop is not PylonHop hop)
			return false;
		if (!PylonTravel.InRange(bot, hop))
		{
			target = hop.StandingSpot;
			return true;
		}
		pylonHop = null;
		if (!PylonTravel.TryTeleport(bot, hop))
			pylonRefusedAt = Main.GameUpdateCount;
		follower.Invalidate();
		ResetProgress();
		return false;
	}

	void Steer(NPC npc, Vector2 target, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		bool hopping = HopThroughPylon(ref target);
		var delta = target - npc.Center;
		if (!hopping && Math.Abs(delta.X) <= arriveRadius && Math.Abs(delta.Y) <= arriveRadius + 24f)
		{
			HasArrived = true;
			moveDirection = EscapeHazard(npc);
			return;
		}
		HasArrived = false;

		var abilities = Abilities();
		var goalTile = new Point((int)(target.X / 16f), (int)((target.Y + 21f) / 16f));
		var leg = longRoute.LegToward(PathFinder.FeetOf(npc), goalTile, Math.Max(1, (int)(arriveRadius / 16f)), abilities, needsWholeRoute);
		if (longRoute.Planned != null)
		{
			BotDiagnostics.Log(bot, longRoute.Planned);
			longRoute.ClearPlanned();
		}
		var intent = follower.Follow(npc, leg ?? target, leg == null ? arriveRadius : LegRadius, abilities);
		if (leg == null && follower.HasNoRoute)
			needsWholeRoute = true;
		AimProgressAt(leg ?? target);
		if (follower.HasPath && !(Main.GameUpdateCount < recyclingUntil && !follower.ReachesGoal))
		{
			sprinting = npc.velocity.Y == 0f && follower.FlatAhead(SprintLookahead);
			ApplyIntent(npc, intent, ref moveDirection, ref wantsJump, ref jumpHoldTicks);
			return;
		}
		if (npc.velocity.Y == 0f && CoastsOffDeadlyEdge(npc))
		{
			npc.velocity.X = 0f;
			return;
		}
		if ((follower.HasNoRoute || follower.FinishedShortOfGoal || Main.GameUpdateCount < recyclingUntil) && !PlacementForbidden && RecycleBlocks(npc, delta, ref moveDirection, ref wantsJump, ref jumpHoldTicks))
		{
			recyclingUntil = Main.GameUpdateCount + RecyclingStickTicks;
			return;
		}
		SteerGreedy(npc, target, delta, ref moveDirection, ref wantsJump);
	}

	bool CoastsOffDeadlyEdge(NPC npc)
	{
		float speed = npc.velocity.X;
		if (speed == 0f)
			return false;
		float coast = Math.Sign(speed) * speed * speed / (2f * bot.Avatar.runSlowdown);
		int left = (int)((npc.position.X + coast) / 16f);
		int right = (int)((npc.position.X + npc.width - 1f + coast) / 16f);
		int row = BodyTiles.Of(npc).Bottom;
		for (int x = left; x <= right; x++)
		{
			if (TileQuery.CanStandOn(x, row + 1))
				return false;
		}
		return DropDepth(speed > 0f ? left : right, row) > AffordableFall;
	}

	const int RecycleFrontierLimit = 40;
	const uint RecyclingStickTicks = 60;
	uint recyclingUntil;
	const float TargetFeetOffset = 21f;
	const float AlignedPixels = 12f;
	const float FrontierTolerance = 4f;
	const float FrontierCreepSpeed = 1f;

	bool RecycleBlocks(NPC npc, Vector2 delta, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		if (bot.Hands.BestPickaxe == null)
			return false;
		var body = BodyTiles.Of(npc);
		bool grounded = npc.velocity.Y == 0f;
		int column = (int)(npc.Center.X / 16f);
		int targetFeet = (int)((npc.Center.Y + delta.Y + TargetFeetOffset) / 16f);
		if (body.Bottom - targetFeet >= Abilities().JumpRise)
			return RecycleUpward(npc, body, column, grounded, ref wantsJump, ref jumpHoldTicks);
		return Math.Abs(delta.X) >= AlignedPixels && Math.Abs(delta.X) >= delta.Y && RecycleAcross(npc, body, column, grounded, Math.Sign(delta.X), ref moveDirection);
	}

	bool RecycleUpward(NPC npc, BodyTiles body, int column, bool grounded, ref bool wantsJump, ref int jumpHoldTicks)
	{
		if (TileQuery.IsSolid(column, body.Top - 1))
			return false;
		if (bot.Hands.HasBridgeMaterial)
		{
			wantsJump = grounded || jumpHold > 0;
			jumpHoldTicks = ClimbJumpHoldTicks;
			bool risenOneTileAboveSupport = !grounded && !PathFinder.IsSupportedNow(column, body.Bottom) && PathFinder.IsSupportedNow(column, body.Bottom + 1);
			if (risenOneTileAboveSupport)
				BridgeTile = new Point(column, body.Bottom + 1);
			return true;
		}
		if (!grounded)
			return true;
		var pickaxe = bot.Hands.BestPickaxe;
		Point? spare = null;
		foreach (var placed in bot.Hands.PlacedBlocks)
		{
			bool supportsBody = placed.Y == body.Bottom + 1 && placed.X >= body.Left && placed.X <= body.Right;
			if (placed.Y > body.Bottom && !supportsBody && bot.Hands.IsReclaimable(placed) && bot.Hands.CanReach(pickaxe, placed) && (spare is not Point best || placed.Y < best.Y))
				spare = placed;
		}
		if (spare is not Point reclaim)
			return false;
		bot.Hands.Reclaim(reclaim);
		return true;
	}

	bool RecycleAcross(NPC npc, BodyTiles body, int column, bool grounded, int direction, ref int moveDirection)
	{
		if (!grounded)
			return true;
		int row = body.Bottom + 1;
		int frontier = column;
		while (TileQuery.CanStandOn(frontier + direction, row) && Math.Abs(frontier - column) < RecycleFrontierLimit)
			frontier += direction;
		if (DropDepth(frontier + direction, body.Bottom) <= AffordableFall)
			return false;
		float offset = frontier * 16f + 8f - npc.Center.X;
		if (Math.Abs(offset) > FrontierTolerance)
		{
			moveDirection = Math.Sign(offset);
			npc.velocity.X = Math.Clamp(npc.velocity.X, -FrontierCreepSpeed, FrontierCreepSpeed);
			return true;
		}
		npc.velocity.X = 0f;
		var ahead = new Point(frontier + direction, row);
		if (bot.Hands.HasBridgeMaterial)
		{
			BridgeTile = ahead;
			return true;
		}
		var behind = new Point(frontier - direction, row);
		if (!bot.Hands.IsReclaimable(behind))
			return false;
		bot.Hands.Reclaim(behind);
		return true;
	}

	const float AffordableFallDamageShare = 0.4f;
	const int FallDamagePerTile = 10;
	const int MaxMeasuredDrop = 200;

	int AffordableFall => bot.Avatar.noFallDmg ? WingedSafeFall : bot.Vitals.SafeFall - 1 + (int)(bot.Avatar.statLife * AffordableFallDamageShare / FallDamagePerTile);

	const int GearRefreshTicks = 15;
	const int ScarceBridgeMaterial = 5;

	NavigationAbilities gear;
	uint gearCheckedAt;

	public void RefreshGear() => gearCheckedAt = 0;

	const uint CorridorMemoryTicks = 30;
	uint alongCorridorAt;

	public void NoteAlongCorridor() => alongCorridorAt = Math.Max(1u, Main.GameUpdateCount);

	bool AlongCorridor => alongCorridorAt != 0 && Main.GameUpdateCount - alongCorridorAt < CorridorMemoryTicks;

	public NavigationAbilities Abilities()
	{
		if (gearCheckedAt == 0 || Main.GameUpdateCount - gearCheckedAt >= GearRefreshTicks)
		{
			gearCheckedAt = Math.Max(1u, Main.GameUpdateCount);
			int pickPower = bot.Hands.BestPickaxe?.pick ?? 0;
			int flyRise = FlightRise;
			bool extraJump = HasExtraJump;
			int singleRise = SingleJumpRise;
			gear = new NavigationAbilities(pickPower, pickPower > 0, bot.Hands.BridgeMaterialCount() >= ScarceBridgeMaterial, false, bot.Hands.HasRope,
				flyRise > 0 ? WingedSafeFall : 0, HasTempleKey, singleRise + (extraJump ? ExtraJumpRise : 0), flyRise, bot.Grapple.HookReachTiles, bot.Hands.HasAxe, singleRise, LeapReach(extraJump, flyRise));
		}
		bool noPlacing = PlacementForbidden || World.SkyArena.FightingOn(bot);
		bool canRope = gear.CanRope && !noPlacing;
		return gear with
		{
			CanRope = canRope,
			HookReach = AlongCorridor ? 0 : gear.HookReach,
			CanBridge = gear.CanBridge && !noPlacing,
			CanPillar = gear.CanBridge && !noPlacing && (ScaffoldingAllowed || !canRope),
			MaxFall = gear.FlyRise > 0 ? WingedSafeFall : AffordableFall
		};
	}

	const int WingedSafeFall = 120;
	const int ExtraJumpRise = 4;
	const float ExtraJumpFallSpeed = 0.5f;
	const int MaxFlightRise = 40;
	const float FlightRiseEfficiency = 0.9f;

	const int RocketBootTicks = 70;

	int FlightTicks
	{
		get
		{
			int ticks = bot.Avatar.rocketBoots > 0 ? RocketBootTicks : 0;
			for (int slot = Equipment.FirstAccessorySlot; slot <= Equipment.LastAccessorySlot; slot++)
			{
				var worn = bot.Avatar.armor[slot];
				int wingSlot = worn.wingSlot;
				if (wingSlot > 0 && !worn.IsAir && bot.Avatar.IsItemSlotUnlockedAndUsable(slot))
					return ticks + ArmorIDs.Wing.Sets.Stats[wingSlot].FlyTime;
			}
			return ticks;
		}
	}

	int FlightRise => FlightTicks <= 0 ? 0 : Math.Min(MaxFlightRise, (int)(FlightTicks * WingRiseSpeed * FlightRiseEfficiency / 16f));

	static readonly System.Collections.Generic.HashSet<int> DoubleJumpAccessories = new()
	{
		ItemID.CloudinaBottle, ItemID.BlizzardinaBottle, ItemID.SandstorminaBottle, ItemID.FartinaJar, ItemID.TsunamiInABottle,
		ItemID.CloudinaBalloon, ItemID.BlizzardinaBalloon, ItemID.SandstorminaBalloon, ItemID.FartInABalloon, ItemID.SharkronBalloon,
		ItemID.BundleofBalloons, ItemID.BalloonHorseshoeFart, ItemID.BalloonHorseshoeSharkron, ItemID.HorseshoeBundle
	};

	bool HasExtraJump
	{
		get
		{
			for (int slot = Equipment.FirstAccessorySlot; slot <= Equipment.LastAccessorySlot; slot++)
			{
				if (DoubleJumpAccessories.Contains(bot.Avatar.armor[slot].type) && bot.Avatar.IsItemSlotUnlockedAndUsable(slot))
					return true;
			}
			return false;
		}
	}

	void ApplyIntent(NPC npc, MotorIntent intent, ref int moveDirection, ref bool wantsJump, ref int jumpHoldTicks)
	{
		moveDirection = intent.MoveDirection;
		if (moveDirection != 0)
			npc.direction = moveDirection;
		wantsJump = intent.Jump;
		if (intent.JumpHoldTicks > 0)
			jumpHoldTicks = intent.JumpHoldTicks;
		if (intent.Fly && CanFly)
		{
			RequestFlight(FlightIntentTicks);
			wantsJump = true;
		}
		if (intent.HangAt is Vector2 hangCenter && intent.HookAt is Vector2 hangHold)
		{
			if (bot.Grapple.Anchor != hangHold)
				bot.Grapple.LatchToHang(npc, hangHold, hangCenter);
		}
		else
		{
			if (bot.Grapple.HangingInPlace)
				bot.Grapple.Release(npc, jump: false);
			if (intent.HookAt is Vector2 hold && !bot.Grapple.IsAttached)
				bot.Grapple.LatchForClimb(npc, hold);
		}
		if (intent.HoldRope)
		{
			ropeHeldAt = Main.GameUpdateCount;
			npc.velocity.Y = 0f;
			npc.GravityMultiplier = MultipliableFloat.One * 0f;
		}
		if (intent.Brake)
			npc.velocity.X = 0f;
		else if (intent.Creep)
			npc.velocity.X = Math.Clamp(npc.velocity.X, -CreepSpeed, CreepSpeed);
		TileToDig = intent.DigTile;
		BridgeTile = intent.BridgeTile;
		if (BridgeTile != null && npc.velocity.Y == 0f)
			npc.velocity.X = 0f;
		ScaffoldTile = intent.ScaffoldTile;
		WantsToDropThroughPlatforms = intent.DropThroughPlatforms;
		if (intent.ClimbDirection != 0)
		{
			if (intent.ClimbColumn >= 0)
				intent.ClimbColumn = ExistingRopeColumn(npc, intent.ClimbColumn);
			climbingRopeAt = Main.GameUpdateCount;
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
			{
				WantsToDropThroughPlatforms = true;
				bot.Vitals.Teleported(npc);
			}
			npc.GravityMultiplier = MultipliableFloat.One * 0f;
			if (intent.ClimbDirection < 0 && intent.ClimbColumn >= 0 && !RopeAtHead(npc, intent.ClimbColumn))
			{
				if (++ropeTopTicks >= RopeTopJumpTicks)
				{
					ropeTopTicks = 0;
					npc.GravityMultiplier = MultipliableFloat.One;
					npc.velocity.Y = -bot.Vitals.JumpSpeed;
					jumpHold = HoldFor(RopeTopJumpHold);
					return;
				}
				npc.velocity = Vector2.Zero;
				return;
			}
			ropeTopTicks = 0;
			npc.velocity = new Vector2(0f, NextRopeSpeed(intent.ClimbDirection));
		}
	}

	int ropeTopTicks;
	const int RopeTopJumpTicks = 15;
	const int RopeTopJumpHold = 6;

	static int ExistingRopeColumn(NPC npc, int column)
	{
		var body = BodyTiles.Of(npc);
		if (RopeInColumn(column, body) || RopeInColumn(column + 1, body))
			return column;
		if (RopeInColumn(column - 1, body))
			return column - 1;
		return RopeInColumn(column + 2, body) ? column + 1 : column;
	}

	static bool RopeInColumn(int x, BodyTiles body)
	{
		for (int y = body.Top - 1; y <= body.Bottom + 1; y++)
		{
			if (IsRope(x, y))
				return true;
		}
		return false;
	}

	bool PrepareRopeBelow(NPC npc, int column)
	{
		var body = BodyTiles.Of(npc);
		int y = body.Bottom + 1;
		if (Main.tile[column, y].HasTile || !MayPlaceRope)
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
			if (!IsRope(column, y) && !IsRope(column + 1, y))
			{
				bool ropeOnRight = IsRope(column + 1, y + 1) && !IsRope(column, y + 1);
				int place = ropeOnRight || World.Hellevator.OffRopeColumn(column, y) ? column + 1 : column;
				return MayPlaceRope && !Main.tile[place, y].HasTile && !World.Hellevator.OffRopeColumn(place, y) ? new Point(place, y) : null;
			}
		}
		return null;
	}

	const int HazardContactTicks = 30;

	bool StandingInHazard() =>
		stalledChecks >= 1 && Main.GameUpdateCount - bot.Vitals.HurtByTileAt < HazardContactTicks;

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
		bool alignedHorizontally = Math.Abs(delta.X) < AlignedPixels;

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

	bool CanDigThrough(Point tile)
	{
		int tileType = TileQuery.TypeAt(tile);
		if (tileType < 0 || tileType == TileID.ClosedDoor)
			return true;
		return !Main.tileFrameImportant[tileType] && !HouseInspector.IsProtected(tile.X, tile.Y)
			&& (WorldGen.CanKillTile(tile.X, tile.Y) || PathFinder.RemovableBlockerOnTop(tile.X, tile.Y, Abilities().CanChop) != null);
	}

	static int DropDepth(int x, int feetY)
	{
		int depth = 0;
		while (depth <= MaxMeasuredDrop && !TileQuery.CanStandOn(x, feetY + depth + 1) && Main.tile[x, feetY + depth + 1].LiquidAmount == 0)
			depth++;
		return TileQuery.IsDangerousLiquid(x, feetY + depth + 1) ? int.MaxValue : depth;
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
		if (rise > JumpReachPixels && HasNoRoute && bot.Grapple.TryLatch(npc, target))
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
			climbingRopeAt = Main.GameUpdateCount;
			bool ropeEndsHere = !IsRope(centerX, body.Top - 1) && !MayPlaceRope;
			if (ropeEndsHere)
			{
				if (Main.GameUpdateCount - ropeLeapAt > RopeLeapCooldown)
				{
					ropeLeapAt = Main.GameUpdateCount;
					npc.velocity.Y = -RopeLeapSpeed;
				}
				return false;
			}
			npc.GravityMultiplier = MultipliableFloat.One * 0f;
			npc.velocity = new Vector2(0f, NextRopeSpeed(-1));
			if (MayPlaceRope && !Main.tile[centerX, body.Top - 1].HasTile)
				RopeTile = new Point(centerX, body.Top - 1);
			return true;
		}
		if (!grounded || !MayPlaceRope || Main.tile[centerX, body.Bottom].HasTile)
			return false;
		RopeTile = new Point(centerX, body.Bottom);
		return true;
	}

	uint climbingRopeAt;
	uint ropeHeldAt;

	public bool HoldsRope => (climbingRopeAt == Main.GameUpdateCount || ropeHeldAt == Main.GameUpdateCount) && bot.Npc is NPC body && OnRope(body);

	void HoldRopeOverDrop(NPC npc, bool wantsJump)
	{
		if (climbingRopeAt == Main.GameUpdateCount || wantsJump || npc.velocity.Y < 0f || !OnRope(npc))
			return;
		var body = BodyTiles.Of(npc);
		if (Math.Min(DropDepth(body.Left, body.Bottom), DropDepth(body.Right, body.Bottom)) <= AffordableFall)
			return;
		ropeHeldAt = Main.GameUpdateCount;
		npc.velocity.Y = 0f;
		npc.GravityMultiplier = MultipliableFloat.One * 0f;
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
		if (sprinting)
			maxSpeed = Math.Max(maxSpeed, avatar.accRunSpeed);
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
		float jumpSpeed = bot.Vitals.JumpSpeed;
		if (jumpHold == 0 && (npc.velocity.Y == 0f || npc.wet || OnRope(npc)))
		{
			npc.velocity.Y = -jumpSpeed;
			jumpHold = HoldFor(holdTicks);
		}
		else if (jumpHold > 0)
		{
			npc.velocity.Y = -jumpSpeed;
			jumpHold--;
		}
		else if (extraJumpReady && flightTicks == 0 && npc.velocity.Y > ExtraJumpFallSpeed)
		{
			npc.velocity.Y = -jumpSpeed;
			jumpHold = HoldFor(holdTicks);
			extraJumpReady = false;
		}
		else if (flightTicks > 0 && wingTimeLeft > 0)
		{
			npc.velocity.Y = Math.Max(npc.velocity.Y - WingLift, -WingRiseSpeed);
			wingTimeLeft--;
		}
	}

	int HoldFor(int baseTicks)
	{
		var vitals = bot.Vitals;
		if (baseTicks >= JumpHoldTicks)
			return vitals.JumpHeight;
		float speed = vitals.JumpSpeed;
		float wanted = BotVitals.BaseJumpSpeed * baseTicks + (BotVitals.BaseJumpSpeed * BotVitals.BaseJumpSpeed - speed * speed) / (2f * PlayerGravity);
		return Math.Clamp((int)MathF.Ceiling(wanted / speed), 0, vitals.JumpHeight);
	}

	static float JumpApex(float speed, int height) => speed * (height + 1) + speed * speed / (2f * PlayerGravity);

	static float JumpAirTicks(float speed, int height) => height + 1 + speed / PlayerGravity + MathF.Sqrt(2f * JumpApex(speed, height) / PlayerGravity);

	static readonly float BaseApex = JumpApex(BotVitals.BaseJumpSpeed, BotVitals.BaseJumpHeight);
	static readonly float BaseLeapPixels = JumpAirTicks(BotVitals.BaseJumpSpeed, BotVitals.BaseJumpHeight) * 3f;

	int SingleJumpRise => Math.Max(1, PathFinder.BaseJumpRise + (int)MathF.Floor((JumpApex(bot.Vitals.JumpSpeed, bot.Vitals.JumpHeight) - BaseApex) / 16f));

	int LeapReach(bool extraJump, int flyRise)
	{
		float run = bot.Avatar.maxRunSpeed * bot.Avatar.moveSpeed;
		float pixels = JumpAirTicks(bot.Vitals.JumpSpeed, bot.Vitals.JumpHeight) * run;
		return (int)(BaseLeapTiles * pixels / BaseLeapPixels) + (extraJump ? ExtraJumpRise * 3 / 2 : 0) + flyRise * 2 / 3;
	}

	const int BaseLeapTiles = 6;

	const float WingLift = 0.6f;
	const float WingRiseSpeed = 6f;
	int wingTimeLeft;
	int flightTicks;

	public bool CanFly => FlightTicks > 0;

	public void RequestFlight(int ticks) => flightTicks = Math.Max(flightTicks, ticks);

	bool extraJumpReady;
	bool sprinting;

	const int ArrestMarginTiles = 4;
	const float ArrestFallSpeed = 4f;

	void ArrestFall(NPC npc, ref bool wantsJump)
	{
		if (npc.velocity.Y < ArrestFallSpeed || npc.wet || CanFly || bot.Grapple.IsAttached)
			return;
		if (bot.Vitals.FallenTiles(npc) < bot.Vitals.SafeFall - ArrestMarginTiles)
			return;
		if (extraJumpReady)
		{
			wantsJump = true;
			jumpHold = 0;
			BotDiagnostics.Log(bot, "double-jumped to break a fall");
			return;
		}
		bot.Grapple.TryArrestFall(npc);
	}

	void RefuelWings(NPC npc)
	{
		if (npc.velocity.Y != 0f && !npc.wet)
			return;
		wingTimeLeft = FlightTicks;
		extraJumpReady = HasExtraJump;
	}

	void AimProgressAt(Vector2 aim)
	{
		if (progressAim is Vector2 previous && Vector2.DistanceSquared(previous, aim) <= LegRadius * LegRadius)
			return;
		progressAim = aim;
		closestDistance = float.MaxValue;
	}

	int stepsAtLastCheck;

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
		float distance = Vector2.Distance(npc.Center, progressAim ?? target);
		bool followingRoute = !follower.HasNoRoute && follower.StepsReached != stepsAtLastCheck;
		stepsAtLastCheck = follower.StepsReached;
		if (distance < closestDistance - MeaningfulProgress || bot.Hands.IsWorking || followingRoute)
		{
			closestDistance = Math.Min(distance, closestDistance);
			stalledChecks = 0;
			return;
		}
		stalledChecks++;
		needsWholeRoute |= stalledChecks >= StalledChecksBeforeWholeRoute;
		if (stalledChecks == StalledChecksBeforeHook && target.Y < npc.Center.Y - HookWorthyRise && !bot.Grapple.IsAttached && !OnRope(npc) && bot.Grapple.TryLatch(npc, target))
			BotDiagnostics.Log(bot, $"stuck below {target.ToTileCoordinates()}, hooking up");
	}

	const int StalledChecksBeforeHook = 2;
	const float HookWorthyRise = 16f * 3f;
}

public readonly record struct BodyTiles(int Left, int Right, int Top, int Bottom)
{
	public static BodyTiles Of(Entity entity) => new(
		(int)(entity.position.X / 16f),
		(int)((entity.position.X + entity.width - 1) / 16f),
		(int)(entity.position.Y / 16f),
		(int)((entity.position.Y + entity.height - 1) / 16f));
}
