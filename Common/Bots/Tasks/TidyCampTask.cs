using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class TidyCampTask : BotTask
{
	const int CampMargin = 40;
	const int CampRowsAbove = 25;
	const int CampRowsBelow = 15;

	Point? tombstone;

	public TidyCampTask(Bot bot) : base(bot) { }

	public override string NameKey => "TidyCamp";

	public override float Evaluate()
	{
		tombstone = FindTombstone();
		if (tombstone == null || Bot.Hands.BestPickaxe == null)
			return 0f;
		Detail = Lang.GetMapObjectName(TileID.Tombstones) ?? "";
		return 45f + Traits.Building * 15f;
	}

	Point? FindTombstone()
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		Point? nearest = null;
		long nearestDistance = long.MaxValue;
		var origin = Bot.CenterTile;
		for (int x = settlement.Left - CampMargin; x <= settlement.Right + CampMargin; x++)
		{
			for (int y = settlement.Top - CampRowsAbove; y <= settlement.Floor + CampRowsBelow; y++)
			{
				if (!WorldGen.InWorld(x, y, 5) || !Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.Tombstones)
					continue;
				long distance = WorldScan.DistanceSq(origin, new Point(x, y));
				if (distance < nearestDistance)
				{
					nearest = new Point(x, y);
					nearestDistance = distance;
				}
			}
		}
		return nearest;
	}

	public override void Tick()
	{
		if (tombstone is not Point tile || TileQuery.TypeAt(tile) != TileID.Tombstones)
		{
			IsFinished = true;
			return;
		}
		var pickaxe = Bot.Hands.BestPickaxe;
		if (!Bot.Hands.CanReach(pickaxe, tile))
		{
			Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		if (Bot.Hands.Dig(tile) is ToolResult.Unbreakable or ToolResult.NoTool)
			IsFinished = true;
	}
}
