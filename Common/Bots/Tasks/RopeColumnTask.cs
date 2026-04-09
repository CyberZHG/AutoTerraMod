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
		if (Vector2.Distance(Bot.Center, spot) > WorkReach)
		{
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
