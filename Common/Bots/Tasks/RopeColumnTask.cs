using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class RopeColumnTask : BotTask
{
	const float WorkReach = 16f * 4f;

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

	const float SlideSpeed = 2f;

	bool SlideDown(Vector2 spot)
	{
		if (Bot.Npc is not NPC body || System.Math.Abs(Bot.Center.X - spot.X) > 16f * 1.5f || Bot.Center.Y >= spot.Y)
			return false;
		var below = new Point(Bot.CenterTile.X, (int)((body.position.Y + body.height + SlideSpeed) / 16f));
		if (TileQuery.IsSolid(below.X, below.Y) && !TileQuery.IsPlatform(below.X, below.Y) || TileQuery.IsSolid(below.X + 1, below.Y) && !TileQuery.IsPlatform(below.X + 1, below.Y))
			return false;
		Bot.Motor.Stop();
		if (Bot.Grapple.IsAttached)
			Bot.Grapple.Release(body, jump: false);
		body.position.Y += SlideSpeed;
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Watchdog.RecordProgress();
		return true;
	}

	public static int? MissingRow(int x, int top, int bottom)
	{
		for (int y = top; y <= bottom; y++)
		{
			if (!Main.tile[x, y].HasTile || !Main.tileRope[Main.tile[x, y].TileType])
			{
				if (TileQuery.IsPlatform(x, y))
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
		var spot = new Vector2(PathFinder.CenterXOf(step.X), row * 16f + 8f);
		if (Vector2.Distance(Bot.Center, spot) > WorkReach || !Bot.Hands.CanReach(null, cell))
		{
			if (SlideDown(spot))
				return;
			if (!TravelOrTunnel(spot, 24f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
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
