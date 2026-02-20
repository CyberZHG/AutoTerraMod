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
		Detail = "";
		return 45f + Traits.Building * 15f;
	}

	const int ScanIntervalTicks = 120;

	static readonly System.Collections.Generic.Dictionary<int, (uint at, bool any)> campScans = new();

	Point? FindTombstone()
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		if (campScans.TryGetValue(Bot.FactionId, out var scan) && Main.GameUpdateCount - scan.at < ScanIntervalTicks && !scan.any)
			return null;
		var nearest = NearestTombstone(settlement);
		campScans[Bot.FactionId] = (Main.GameUpdateCount, nearest != null);
		return nearest;
	}

	Point? NearestTombstone(Building.Settlement settlement)
	{
		Point? nearest = null;
		long nearestDistance = long.MaxValue;
		var origin = Bot.CenterTile;
		for (int x = settlement.Left - CampMargin; x <= settlement.Right + CampMargin; x++)
		{
			for (int y = settlement.Top - CampRowsAbove; y <= settlement.Floor + CampRowsBelow; y++)
			{
				if (!WorldGen.InWorld(x, y, 5) || !IsClutter(x, y))
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

	static bool IsClutter(int x, int y)
	{
		var tile = Main.tile[x, y];
		if (!tile.HasTile)
			return false;
		if (tile.TileType == TileID.Tombstones)
			return true;
		return Main.tileRope[tile.TileType] && Main.tile[x - 1, y].HasTile && Main.tileRope[Main.tile[x - 1, y].TileType];
	}

	public override void Tick()
	{
		if (tombstone is not Point tile || !IsClutter(tile.X, tile.Y))
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
