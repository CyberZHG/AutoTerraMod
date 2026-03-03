using System.Linq;
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
				if (!WorldGen.InWorld(x, y, 5) || RecentlyFailed(new Point(x, y)))
					continue;
				if (!IsClutter(x, y) && !IsCampObstacle(settlement, x, y))
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
		bool canBuildEntrance = EntrancePlatform() != null;
		int y = settlement.Floor;
		for (int x = settlement.Left - CampMargin; x <= settlement.Right + CampMargin; x++)
		{
			bool work = canPave && IsRoadGap(settlement, x, y) || IsEntranceWork(settlement, x, y) && (TileQuery.HasTile(new Point(x, y)) || canBuildEntrance);
			if (!work || RecentlyFailed(new Point(x, y)))
				continue;
			long distance = WorldScan.DistanceSq(Bot.CenterTile, new Point(x, y));
			if (distance < nearestDistance)
			{
				nearest = new Point(x, y);
				nearestDistance = distance;
			}
		}
		return nearest;
	}

	const int EntranceWidth = 3;
	static readonly int[] EntranceOffsets = { 5, 25 };

	static bool IsEntrance(Building.Settlement settlement, int x)
	{
		foreach (int offset in EntranceOffsets)
		{
			int leftStart = settlement.Left - offset - EntranceWidth + 1;
			int rightStart = settlement.Right + offset;
			if (x >= leftStart && x < leftStart + EntranceWidth || x >= rightStart && x < rightStart + EntranceWidth)
				return true;
		}
		return false;
	}

	static bool IsEntranceWork(Building.Settlement settlement, int x, int y)
	{
		if (y != settlement.Floor || !IsEntrance(settlement, x) || TileQuery.IsPlatform(x, y) || !WorldGen.InWorld(x, y, 10)
			|| Main.tile[x, y].LiquidAmount > 0 || Building.HouseInspector.IsProtected(x, y) || InAnotherCamp(settlement, x, y))
			return false;
		if (!TileQuery.HasTile(new Point(x, y)))
			return true;
		return !Main.tileFrameImportant[Main.tile[x, y].TileType] && WorldGen.CanKillTile(x, y);
	}

	Item EntrancePlatform() => Inventory.Items(Avatar).FirstOrDefault(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]);

	static bool InCamp(Building.Settlement settlement, int x) => x >= settlement.Left - CampMargin && x <= settlement.Right + CampMargin;

	static bool IsCampObstacle(Building.Settlement settlement, int x, int y)
	{
		if (!InCamp(settlement, x) || y >= settlement.Floor || y < settlement.Top - CampRowsAbove || !WorldGen.InWorld(x, y, 10) || !TileQuery.IsSolid(x, y))
			return false;
		int type = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[type] && !Building.HouseInspector.IsProtected(x, y) && !InAnotherCamp(settlement, x, y) && WorldGen.CanKillTile(x, y);
	}

	static bool InAnotherCamp(Building.Settlement own, int x, int y)
	{
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (faction.Settlement is { } other && other != own && other.Contains(x, y))
				return true;
		}
		return false;
	}

	static bool IsRoadGap(Building.Settlement settlement, int x, int y)
	{
		if (!InCamp(settlement, x) || IsEntrance(settlement, x) || y != settlement.Floor || !WorldGen.InWorld(x, y, 10))
			return false;
		var tile = Main.tile[x, y];
		return !tile.HasTile && tile.LiquidAmount == 0 && !Building.HouseInspector.IsProtected(x, y) && !InAnotherCamp(settlement, x, y);
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
		return settlement != null && (IsCampObstacle(settlement, tile.X, tile.Y) || IsEntranceWork(settlement, tile.X, tile.Y) || IsRoadGap(settlement, tile.X, tile.Y) && PavingBlock() != null);
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
		var camp = Bot.Faction.Settlement;
		bool entrance = camp != null && IsEntranceWork(camp, tile.X, tile.Y) && !TileQuery.HasTile(tile);
		bool paving = entrance || camp != null && IsRoadGap(camp, tile.X, tile.Y);
		var tool = entrance ? EntrancePlatform() : paving ? PavingBlock() : Bot.Hands.BestPickaxe;
		if (tool == null)
		{
			GiveUp(tile);
			return;
		}
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
