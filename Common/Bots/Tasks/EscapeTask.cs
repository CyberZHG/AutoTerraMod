using System;
using System.Linq;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class EscapeTask : BotTask
{
	const int MaxEscapeTicks = 3600;
	const int NavigationGraceTicks = 600;
	const int StraightClimbTicks = 1800;
	const int StillTrappedTiles = 10;
	const int SurfaceMarginTiles = 5;
	const int SurfaceStepTiles = 8;
	const int OpenGroundTrip = 30;
	const int ExitSearchMultiplier = 6;
	const int ExitLegTiles = 45;
	const int ExitReplanTicks = 300;
	const int ExitMinReplanTicks = 60;
	const int TrivialLegTiles = 2;

	Vector2 destination;
	Vector2 start;
	bool headingForSurface;
	uint climbStartedAt;
	uint startedAt;
	uint exitPlannedAt;
	bool followingExit;
	bool legReached;
	bool legIsSurfaceExit;
	uint routedAt;
	const uint NoRouteGraceTicks = 300;
	int surfaceMargin;
	const int PitSurfaceMargin = -2;
	int highestY;
	uint climbedAt;
	bool startedForAir;
	bool climbAllTheWay;
	Vector2? campExit;
	Vector2 lastEscapeStart;
	uint lastEscapeAt;
	const int LocalEscapeTiles = 20;
	const int RepeatEscapeTiles = 40;
	const uint RepeatEscapeTicks = 7200;

	public EscapeTask(Bot bot) : base(bot) { }

	public override string NameKey => "Escape";
	public override bool IsUrgent => true;

	public override float Evaluate() => Bot.Watchdog.EscapeRequested ? 100f : 0f;

	public override void Start()
	{
		base.Start();
		startedAt = Main.GameUpdateCount;
		start = Bot.Center;
		campExit = CampExitAbove(Bot.Faction.BaseCenter);
		climbAllTheWay = campExit != null || lastEscapeAt != 0 && Main.GameUpdateCount - lastEscapeAt < RepeatEscapeTicks && Vector2.Distance(Bot.Center, lastEscapeStart) < RepeatEscapeTiles * 16f;
		lastEscapeStart = Bot.Center;
		lastEscapeAt = Main.GameUpdateCount;
		startedForAir = Bot.Npc is NPC swimmer && (swimmer.wet || Bot.Vitals.BreathFraction < 1f);
		climbStartedAt = 0;
		routedAt = Main.GameUpdateCount;
		var here = Bot.CenterTile;
		int x = Utils.Clamp(here.X + Bot.Watchdog.EscapeDirection * SurfaceStepTiles, 50, Main.maxTilesX - 50);
		int surfaceY = TileQuery.SurfaceY(x);
		if (PathFinder.InsideMaze(here))
		{
			Finish();
			return;
		}
		if (Bot.Brain.OnExpedition)
		{
			headingForSurface = false;
			destination = Bot.Center + new Vector2(Bot.Watchdog.EscapeDirection * OpenGroundTrip * 16f, 0f);
			return;
		}
		surfaceMargin = BelowOwnCamp() ? PitSurfaceMargin : SurfaceMarginTiles;
		headingForSurface = here.Y > surfaceY + surfaceMargin;
		exitPlannedAt = 0;
		exitSearch?.Cancel();
		exitSearch = null;
		followingExit = false;
		legReached = false;
		legIsSurfaceExit = false;
		highestY = here.Y;
		climbedAt = startedAt;
		destination = headingForSurface
			? new Vector2(x * 16f, (surfaceY - 2) * 16f)
			: Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) > 16f * 40f
				? Bot.Faction.BaseCenter
				: Bot.Center + new Vector2(Bot.Watchdog.EscapeDirection * OpenGroundTrip * 16f, 0f);
	}

	public override void Tick()
	{
		Bot.Motor.ScaffoldingAllowed = true;
		if (campExit is Vector2 exit)
		{
			TunnelOutFromUnderCamp(exit);
			return;
		}
		if (climbStartedAt == 0 && ShouldClimbStraightUp() && !Bot.Brain.OnExpedition && TryRecallHome())
		{
			Finish();
			return;
		}
		if (climbStartedAt == 0 && ShouldClimbStraightUp())
		{
			climbStartedAt = Main.GameUpdateCount;
			Bot.Motor.ClimbingStraightUp = true;
			BotDiagnostics.Log(Bot, $"navigation failed to free me, digging straight up from {Bot.CenterTile}");
		}
		if (climbStartedAt != 0 && Main.GameUpdateCount - climbStartedAt > StraightClimbTicks)
		{
			Finish();
			return;
		}
		if (!headingForSurface && !Bot.Brain.OnExpedition && Bot.Motor.HasNoRoute)
		{
			headingForSurface = true;
			surfaceMargin = PitSurfaceMargin;
			startedAt = Main.GameUpdateCount;
			destination = Bot.Center;
		}
		if (headingForSurface && climbStartedAt == 0)
			PlanExitLeg();
		Bot.Motor.MoveTo(destination, 32f);
		var here = Bot.CenterTile;
		bool reachedSurface = headingForSurface && (here.Y <= TileQuery.SurfaceY(here.X) + surfaceMargin || legIsSurfaceExit && Bot.Motor.HasArrived);
		bool standing = Bot.Npc is NPC body && body.velocity.Y == 0f;
		if (headingForSurface && !climbAllTheWay && standing && Vector2.Distance(Bot.Center, start) > LocalEscapeTiles * 16f)
		{
			BotDiagnostics.Log(Bot, $"got clear of {start.ToTileCoordinates()} at {here}, ending the escape");
			Finish();
			return;
		}
		if (startedForAir && standing && !Bot.Npc.wet && Bot.Vitals.BreathFraction >= 1f)
		{
			BotDiagnostics.Log(Bot, $"out of the water at {here}, ending the escape");
			Finish();
			return;
		}
		if (standing && here.Y < highestY)
		{
			highestY = here.Y;
			climbedAt = Main.GameUpdateCount;
		}
		if (!Bot.Motor.HasNoRoute)
			routedAt = Main.GameUpdateCount;
		uint sinceProgress = Main.GameUpdateCount - (followingExit ? climbedAt : startedAt);
		if (reachedSurface || sinceProgress > MaxEscapeTicks || !followingExit && Bot.Motor.HasArrived)
			Finish();
		else if (Bot.Motor.HasArrived)
			legReached = true;
	}

	PathSearch exitSearch;

	void PlanExitLeg()
	{
		if (Bot.Npc is not NPC body)
			return;
		var feet = PathFinder.FeetOf(body);
		if (exitSearch == null)
		{
			uint sincePlanned = Main.GameUpdateCount - exitPlannedAt;
			bool due = exitPlannedAt == 0 || sincePlanned > ExitReplanTicks || legReached && sincePlanned > ExitMinReplanTicks;
			if (!due)
				return;
			exitPlannedAt = Main.GameUpdateCount;
			legReached = false;
			var surface = new System.Collections.Generic.Dictionary<int, int>();
			float BelowSurface(Point node)
			{
				if (!surface.TryGetValue(node.X, out int y))
					surface[node.X] = y = TileQuery.SurfaceY(node.X) + surfaceMargin + 1;
				return Math.Max(0, node.Y - y);
			}
			exitSearch = PathFinder.BeginExit(feet, Bot.Motor.Abilities(), BelowSurface, ExitSearchMultiplier);
		}
		if (!PathFinder.Advance(exitSearch))
			return;
		var route = exitSearch.Result;
		exitSearch = null;
		if (route.Nodes.Count == 0 && route.ReachesGoal)
		{
			Finish();
			return;
		}
		if (route.Nodes.Count == 0 || route.Nodes[^1].Feet.Y >= feet.Y)
			return;
		var leg = route.Nodes.Last(node => Math.Max(Math.Abs(node.Feet.X - feet.X), Math.Abs(node.Feet.Y - feet.Y)) <= ExitLegTiles).Feet;
		bool surfaceExit = route.ReachesGoal && leg == route.Nodes[^1].Feet;
		if (Math.Max(Math.Abs(leg.X - feet.X), Math.Abs(leg.Y - feet.Y)) <= TrivialLegTiles)
		{
			if (surfaceExit)
				Finish();
			return;
		}
		legIsSurfaceExit = surfaceExit;
		destination = new Vector2(leg.X * 16f + 8f, leg.Y * 16f - 13f);
		if (!followingExit)
			BotDiagnostics.Log(Bot, $"escaping toward exit {route.Nodes[^1].Feet} (reaches surface={route.ReachesGoal}) via {leg}");
		followingExit = true;
	}


	void TunnelOutFromUnderCamp(Vector2 exit)
	{
		var camp = Bot.Faction.Settlement;
		var here = Bot.CenterTile;
		if (camp == null || here.Y < camp.Floor - 1 || Main.GameUpdateCount - startedAt > MaxEscapeTicks)
		{
			Finish();
			return;
		}
		Bot.Motor.TunnelTo(exit, 32f);
		if (Bot.Motor.HasArrived)
			Finish();
	}

	bool ShouldClimbStraightUp() =>
		headingForSurface
		&& (Main.GameUpdateCount - (followingExit ? climbedAt : startedAt) > NavigationGraceTicks && Vector2.Distance(Bot.Center, start) < StillTrappedTiles * 16f
			|| Main.GameUpdateCount - routedAt > NoRouteGraceTicks);

	public override void Stop()
	{
		exitSearch?.Cancel();
		exitSearch = null;
		Bot.Motor.ClimbingStraightUp = false;
		base.Stop();
	}

	void Finish()
	{
		Bot.Motor.ClimbingStraightUp = false;
		IsFinished = true;
		Bot.Watchdog.EscapeFinished();
	}
}
