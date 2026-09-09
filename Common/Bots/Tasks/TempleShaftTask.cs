using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class TempleShaftTask : BotTask
{
	const float DigPriority = 72f;
	const int StuckTicks = 600;

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
		if (TempleShaft.Current is not { } shaft || shaft.IsComplete)
		{
			IsFinished = true;
			return;
		}
		if (shaft.NextSolid() is Point cell)
		{
			Dig(shaft, cell);
			return;
		}
		if (shaft.NextCap() is Point cap)
			Cap(shaft, cap);
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
			GiveUp("cannot reach the shaft top");
		if (++idleTicks > StuckTicks)
			GiveUp($"stuck digging toward {cell}, at {here}");
	}

	void Cap(TempleShaftPlan shaft, Point cap)
	{
		if (Bot.Hands.CanReachPlatform(cap))
		{
			Bot.Motor.Stop();
			if (Bot.Hands.PlacePlatform(cap))
			{
				Bot.Watchdog.RecordProgress();
				if (shaft.Capped)
					BotDiagnostics.Log(Bot, $"finished the temple shaft at {shaft.X},{shaft.TopY}..{shaft.BottomY}");
			}
			return;
		}
		if (!Bot.Hands.HasPlatform || !TravelOrTunnel(shaft.Top, 16f) || ++idleTicks > StuckTicks)
			GiveUp("cannot cap the shaft");
	}

	void GiveUp(string why)
	{
		BotDiagnostics.Log(Bot, $"temple shaft paused: {why}");
		Bot.Brain.SuppressCurrentGoal();
		IsFinished = true;
	}
}
