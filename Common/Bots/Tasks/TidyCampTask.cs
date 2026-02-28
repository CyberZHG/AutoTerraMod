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
	const int RoadLength = 24;
	const int RoadHeadroom = 4;

	Point? tombstone;
	const uint FailedTileRetryTicks = 3600;
	static readonly System.Collections.Generic.Dictionary<Point, uint> failedTiles = new();

	static bool RecentlyFailed(Point tile) => failedTiles.TryGetValue(tile, out uint at) && Main.GameUpdateCount - at < FailedTileRetryTicks;

	void GiveUp(Point tile)
	{
		failedTiles[tile] = Main.GameUpdateCount;
		if (failedTiles.Count > 256)
			failedTiles.Clear();
		IsFinished = true;
	}

	public TidyCampTask(Bot bot) : base(bot) { }

	public override string NameKey => "TidyCamp";

	public override float Evaluate()
	{
		if (tombstone is not Point held || !NeedsWork(held))
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
		var road = NearestRoadWork(settlement);
		if (road is Point work && (nearest is not Point grave || WorldScan.DistanceSq(Bot.CenterTile, work) < WorldScan.DistanceSq(Bot.CenterTile, grave)))
			nearest = road;
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
				if (!WorldGen.InWorld(x, y, 5) || !IsClutter(x, y) || RecentlyFailed(new Point(x, y)))
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

	Point? NearestRoadWork(Building.Settlement settlement)
	{
		Point? nearest = null;
		long nearestDistance = long.MaxValue;
		bool canPave = PavingBlock() != null;
		for (int x = settlement.Left - RoadLength; x <= settlement.Right + RoadLength; x++)
		{
			for (int y = settlement.Floor - RoadHeadroom; y <= settlement.Floor; y++)
			{
				if (!IsRoadObstacle(settlement, x, y) && !(canPave && IsRoadGap(settlement, x, y)) || RecentlyFailed(new Point(x, y)))
					continue;
				long distance = WorldScan.DistanceSq(Bot.CenterTile, new Point(x, y));
				if (distance < nearestDistance)
				{
					nearest = new Point(x, y);
					nearestDistance = distance;
				}
			}
		}
		return nearest;
	}

	static bool OnRoad(Building.Settlement settlement, int x) => x >= settlement.Left - RoadLength && x <= settlement.Right + RoadLength;

	static bool IsRoadObstacle(Building.Settlement settlement, int x, int y)
	{
		if (!OnRoad(settlement, x) || y >= settlement.Floor || y < settlement.Floor - RoadHeadroom || !WorldGen.InWorld(x, y, 10) || !TileQuery.IsSolid(x, y))
			return false;
		int type = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[type] && !Building.HouseInspector.IsProtected(x, y) && WorldGen.CanKillTile(x, y);
	}

	static bool IsRoadGap(Building.Settlement settlement, int x, int y)
	{
		if (!OnRoad(settlement, x) || y != settlement.Floor || !WorldGen.InWorld(x, y, 10))
			return false;
		var tile = Main.tile[x, y];
		return !tile.HasTile && tile.LiquidAmount == 0 && !Building.HouseInspector.IsProtected(x, y);
	}

	Item PavingBlock()
	{
		Item fallback = null;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (item.type == ItemID.DirtBlock)
				return item;
			if (fallback == null && item.type != ItemID.StoneBlock && Building.Outpost.IsBlock(item))
				fallback = item;
		}
		return fallback;
	}

	bool NeedsWork(Point tile)
	{
		if (IsClutter(tile.X, tile.Y))
			return true;
		var settlement = Bot.Faction.Settlement;
		return settlement != null && (IsRoadObstacle(settlement, tile.X, tile.Y) || IsRoadGap(settlement, tile.X, tile.Y) && PavingBlock() != null);
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
		if (tombstone is Point cleared && !NeedsWork(cleared))
			tombstone = RestOfColumn(cleared);
		if (tombstone is not Point tile)
		{
			IsFinished = true;
			return;
		}
		Bot.Motor.RopesForbidden = true;
		bool paving = Bot.Faction.Settlement is { } camp && IsRoadGap(camp, tile.X, tile.Y);
		var tool = paving ? PavingBlock() : Bot.Hands.BestPickaxe;
		if (!Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
			if (Bot.Motor.IsStuck)
				GiveUp(tile);
			return;
		}
		Bot.Motor.Stop();
		if (paving)
		{
			if (Bot.Hands.Ready && !Bot.Hands.Place(tool, tile))
				GiveUp(tile);
			else
				Accomplished = true;
		}
		else if (Bot.Hands.Dig(tile) is ToolResult.Unbreakable or ToolResult.NoTool)
			GiveUp(tile);
		else
			Accomplished = true;
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
