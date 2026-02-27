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
		if (tombstone is not Point held || !IsClutter(held.X, held.Y))
			tombstone = FindTombstone();
		if (tombstone == null || Bot.Hands.BestPickaxe == null)
			return 0f;
		Detail = "";
		return 45f + Traits.Building * 15f;
	}

	const int ScanIntervalTicks = 120;
	const uint RopeScanIntervalTicks = 18000;

	static readonly System.Collections.Generic.Dictionary<int, (uint at, bool any)> campScans = new();
	static readonly System.Collections.Generic.Dictionary<int, uint> ropeScans = new();

	Point? FindTombstone()
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		if (campScans.TryGetValue(Bot.FactionId, out var scan) && Main.GameUpdateCount - scan.at < ScanIntervalTicks && !scan.any)
			return null;
		bool ropes = !ropeScans.TryGetValue(Bot.FactionId, out uint ropeScan) || Main.GameUpdateCount - ropeScan >= RopeScanIntervalTicks;
		if (ropes)
			ropeScans[Bot.FactionId] = Main.GameUpdateCount;
		var nearest = NearestTombstone(settlement, ropes);
		campScans[Bot.FactionId] = (Main.GameUpdateCount, nearest != null);
		return nearest;
	}

	Point? NearestTombstone(Building.Settlement settlement, bool ropes)
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
				if (Main.tileRope[Main.tile[x, y].TileType] && (!ropes || y > settlement.Floor || BotNear(new Point(x, y))))
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

	const float RopeInUseDistance = 16f * 4f;

	static bool BotNear(Point tile)
	{
		var center = tile.ToWorldCoordinates();
		foreach (var bot in Systems.BotWorldSystem.Bots)
		{
			if (bot.IsAlive && Vector2.Distance(bot.Center, center) < RopeInUseDistance)
				return true;
		}
		return false;
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
		if (tombstone is Point cleared && !IsClutter(cleared.X, cleared.Y))
			tombstone = RestOfColumn(cleared);
		if (tombstone is not Point tile)
		{
			IsFinished = true;
			return;
		}
		Bot.Motor.RopesForbidden = true;
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

	static Point? RestOfColumn(Point cleared)
	{
		foreach (int dy in new[] { -1, 1 })
		{
			var next = new Point(cleared.X, cleared.Y + dy);
			if (Main.tileRope[Main.tile[next.X, next.Y].TileType] && IsClutter(next.X, next.Y))
				return next;
		}
		return null;
	}
}
