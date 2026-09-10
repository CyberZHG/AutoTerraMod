using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class TempleShaftTask : BotTask
{
	const float DigPriority = 72f;
	const int StuckTicks = 1200;
	const int TravelFailurePenalty = 30;

	int idleTicks;
	Point lastCell;

	public TempleShaftTask(Bot bot) : base(bot) { }

	public override string NameKey => "DigShaft";

	public override float Evaluate() => Bot.Brain.Plan is DigTempleShaftStep ? DigPriority : 0f;

	public override void Start()
	{
		base.Start();
		idleTicks = 0;
	}

	public override void Tick()
	{
		var shaft = TempleShaft.Current;
		if (shaft == null || shaft.IsComplete)
		{
			if (shaft != null)
				BotDiagnostics.Log(Bot, $"finished the temple shaft at {shaft.X},{shaft.TopY}..{shaft.BottomY}");
			IsFinished = true;
			return;
		}
		if (shaft.NextCap() is Point cap)
		{
			Cap(shaft, cap);
			return;
		}
		if (shaft.NextSolid() is Point cell)
			Dig(shaft, cell);
	}

	void Dig(TempleShaftPlan shaft, Point cell)
	{
		if (cell != lastCell)
		{
			lastCell = cell;
			idleTicks = 0;
		}
		var pickaxe = Bot.Hands.BestPickaxe;
		if (pickaxe != null && Bot.Hands.CanReach(pickaxe, cell))
		{
			Bot.Motor.Stop();
			var result = Bot.Hands.Dig(cell);
			if (result is ToolResult.Unbreakable or ToolResult.NoTool)
				GiveUp($"cannot break {TileQuery.TypeAt(cell)} at {cell}");
			else
				Bot.Watchdog.RecordProgress();
			return;
		}
		var stand = new Point(shaft.RopeX, cell.Y - 1);
		var here = Bot.CenterTile;
		if (shaft.Contains(here) && here.Y < cell.Y)
			Bot.Motor.MoveTo(stand.ToWorldCoordinates(8f, 0f), 8f);
		else if (!TravelOrTunnel(shaft.Top, 16f))
			idleTicks += TravelFailurePenalty;
		if (++idleTicks > StuckTicks)
			GiveUp($"stuck digging toward {cell}, at {here}");
	}

	void Cap(TempleShaftPlan shaft, Point cap)
	{
		var pickaxe = Bot.Hands.BestPickaxe;
		bool solid = TileQuery.HasTile(cap);
		bool inReach = solid ? pickaxe != null && Bot.Hands.CanReach(pickaxe, cap) : Bot.Hands.CanReachPlatform(cap);
		if (!solid && !Bot.Hands.HasPlatform)
		{
			GiveUp("no platforms to cap the shaft");
			return;
		}
		if (!inReach)
		{
			if (!TravelOrTunnel(shaft.SideStand.ToWorldCoordinates(8f, 0f), 16f))
				idleTicks += TravelFailurePenalty;
			if (++idleTicks > StuckTicks)
				GiveUp($"cannot reach the shaft top from {Bot.CenterTile}");
			return;
		}
		Bot.Motor.Stop();
		Bot.Watchdog.RecordProgress();
		if (solid)
		{
			if (Bot.Hands.Dig(cap) is ToolResult.Unbreakable or ToolResult.NoTool)
				GiveUp($"cannot break {TileQuery.TypeAt(cap)} at {cap}");
			return;
		}
		if (Bot.Hands.PlacePlatform(cap) && shaft.Capped)
			BotDiagnostics.Log(Bot, $"capped the temple shaft at {shaft.X},{shaft.TopY}, digging down to {shaft.BottomY}");
	}

	void GiveUp(string why)
	{
		BotDiagnostics.Log(Bot, $"temple shaft paused: {why}");
		Bot.Brain.SuppressCurrentGoal();
		IsFinished = true;
	}
}
