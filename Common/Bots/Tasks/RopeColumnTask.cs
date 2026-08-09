using System.Linq;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class RopeColumnTask : BotTask
{
	HangRopeStep step;

	public RopeColumnTask(Bot bot) : base(bot) { }

	public override string NameKey => "HangRope";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not HangRopeStep hang)
			return 0f;
		step = hang;
		return 65f + Traits.Building * 20f;
	}

	const float SlideSpeed = 6f;

	bool SlideDown(Vector2 spot)
	{
		if (Bot.Npc is not NPC body || System.Math.Abs(Bot.Center.X - spot.X) > 16f * 2.5f || Bot.Center.Y >= spot.Y)
			return false;
		float nudge = System.Math.Clamp(spot.X - Bot.Center.X, -SlideSpeed, SlideSpeed);
		int row = (int)((body.position.Y + body.height + SlideSpeed) / 16f);
		bool blocked = false;
		for (int x = (int)((body.position.X + nudge) / 16f); x <= (int)((body.position.X + nudge + body.width - 1) / 16f); x++)
			blocked |= TileQuery.IsSolid(x, row) && !TileQuery.IsPlatform(x, row);
		if (blocked && System.Math.Abs(nudge) < 0.5f)
			return false;
		Bot.Motor.Stop();
		if (Bot.Grapple.IsAttached)
			Bot.Grapple.Release(body, jump: false);
		body.position.X += nudge;
		if (!blocked)
			body.position.Y += SlideSpeed;
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Watchdog.RecordProgress();
		return true;
	}

	Point? ShaftPlatformAbove(int row)
	{
		if (Bot.Faction.Hellevator is not { } shaft || shaft.RopeX != step.X || Bot.Hands.BestPickaxe == null)
			return null;
		for (int y = row - 1; y >= row - 3 && y > shaft.TopY; y--)
		{
			for (int x = shaft.X; x <= shaft.Right; x++)
			{
				if (shaft.NeedsCleanup(x, y) && Bot.Hands.CanReach(Bot.Hands.BestPickaxe, new Point(x, y)))
					return new Point(x, y);
			}
		}
		return null;
	}

	static bool InShaftRopeColumn(int x, int y) =>
		!World.Highway.ProtectsAnyDeck(x, y) && !Building.HouseInspector.IsProtected(x, y) && Systems.BotWorldSystem.Factions.Any(faction => faction.Hellevator is { } shaft && shaft.RopeX == x && shaft.Contains(x, y));

	public static int? MissingRow(int x, int top, int bottom)
	{
		for (int y = top; y <= bottom; y++)
		{
			if (!Main.tile[x, y].HasTile || !Main.tileRope[Main.tile[x, y].TileType])
			{
				if (TileQuery.IsPlatform(x, y) && !InShaftRopeColumn(x, y))
					continue;
				return y;
			}
		}
		return null;
	}

	public override void Tick()
	{
		if (MissingRow(step.X, step.Top, step.Bottom) is not int row || !Bot.Hands.HasRope)
		{
			IsFinished = true;
			return;
		}
		var cell = new Point(step.X, row);
		bool rightWall = TileQuery.IsSolid(step.X + 1, row) || TileQuery.IsSolid(step.X + 1, row - 1) || TileQuery.IsSolid(step.X + 1, row - 2);
		var spot = new Vector2(PathFinder.CenterXOf(rightWall ? step.X - 1 : step.X), row * 16f + 8f);
		if (!Bot.Hands.CanReach(null, cell))
		{
			if (Main.GameUpdateCount % 600 == 0)
				BotDiagnostics.Log(Bot, $"rope cell {cell} out of reach: distance={Vector2.Distance(Bot.Center, spot):0} reach={Bot.Hands.CanReach(null, cell)} tile={TileQuery.TypeAt(cell)}{TerrainDump.Around(cell, 5, 5)}");
			if (SlideDown(spot))
				return;
			if (!TravelOrTunnel(spot, 24f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		if (ShaftPlatformAbove(row) is Point platform)
		{
			Bot.Hands.Dig(platform);
			return;
		}
		if (TileQuery.HasTile(cell) && !Main.tileRope[Main.tile[cell.X, cell.Y].TileType])
		{
			if (Bot.Hands.Dig(cell) is ToolResult.Unbreakable or ToolResult.NoTool)
				IsFinished = true;
			return;
		}
		if (Bot.Hands.PlaceRope(cell))
			Bot.Watchdog.RecordProgress();
	}
}
