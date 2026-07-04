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
		tombstone = null;
		workingOn = null;
		if (failedTiles.Count > 256)
			failedTiles.Clear();
		IsFinished = true;
	}

	public TidyCampTask(Bot bot) : base(bot) { }

	public override string NameKey => "TidyCamp";

	const float GraveDiggerPriority = 50f;

	public override float Evaluate()
	{
		if (Bot.Faction.Settlement is not { } settlement || !NearCamp(settlement))
			return 0f;
		if (!Building.HouseInspector.IsDesignatedBuilder(Bot))
		{
			if (tombstone is not Point grave || !IsGrave(grave) || RecentlyFailed(grave))
				tombstone = NearestGrave();
			Detail = "";
			return tombstone != null && Bot.Hands.BestPickaxe != null ? GraveDiggerPriority : 0f;
		}
		if (tombstone is not Point held || !NeedsWork(held) || RecentlyFailed(held))
			tombstone = FindTombstone();
		if (tombstone == null || Bot.Hands.BestPickaxe == null)
			return 0f;
		Detail = "";
		return 42f + Traits.Building * 10f;
	}

	const int TidySlack = 20;

	bool NearCamp(Building.Settlement settlement)
	{
		var here = Bot.CenterTile;
		return here.X >= settlement.Left - CampMargin - TidySlack && here.X <= settlement.Right + CampMargin + TidySlack
			&& here.Y >= settlement.Top - CampRowsAbove - TidySlack && here.Y <= settlement.Floor + CampRowsBelow + TidySlack;
	}
	const uint TargetPatienceTicks = 300;
	Point? workingOn;
	uint workingSince;

	const int ScanIntervalTicks = 120;
	const uint RopeScanIntervalTicks = 1800;

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

	static bool IsGrave(Point tile) => Main.tile[tile.X, tile.Y].HasTile && Main.tile[tile.X, tile.Y].TileType == TileID.Tombstones;

	static readonly System.Collections.Generic.Dictionary<int, (uint at, System.Collections.Generic.List<Point> graves)> graveScans = new();

	Point? NearestGrave()
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		if (!graveScans.TryGetValue(Bot.FactionId, out var scan) || Main.GameUpdateCount - scan.at >= ScanIntervalTicks)
		{
			var graves = new System.Collections.Generic.List<Point>();
			for (int x = settlement.Left - CampMargin; x <= settlement.Right + CampMargin; x++)
			{
				for (int y = settlement.Top - CampRowsAbove; y <= settlement.Floor + CampRowsBelow; y++)
				{
					if (WorldGen.InWorld(x, y, 5) && IsGrave(new Point(x, y)))
						graves.Add(new Point(x, y));
				}
			}
			graveScans[Bot.FactionId] = scan = (Main.GameUpdateCount, graves);
		}
		return scan.graves.Where(grave => IsGrave(grave) && !RecentlyFailed(grave)).Cast<Point?>().MinBy(grave => WorldScan.DistanceSq(Bot.CenterTile, grave.Value));
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
				if (Main.tileRope[Main.tile[x, y].TileType] && (!ropes || BotNear(new Point(x, y))))
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

	public static bool IsEntrance(Building.Settlement settlement, int x)
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
		return !Main.tileFrameImportant[type] && TileQuery.IsExposed(x, y) && !Building.HouseInspector.IsProtected(x, y) && !InAnotherCamp(settlement, x, y) && WorldGen.CanKillTile(x, y);
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
			if (fallback == null && item.type != ItemID.StoneBlock && Building.Outpost.IsBlock(item) && Building.PlacementRules.Allows(Bot, item))
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

	public static bool BotNear(Point tile)
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
		return Main.tileRope[tile.TileType] && !KeptRope(x, y) && RopeBeside(x, y);
	}

	const int RedundantRopeSpan = 5;

	static bool RopeBeside(int x, int y)
	{
		for (int dx = 1; dx <= RedundantRopeSpan; dx++)
		{
			var side = Main.tile[x - dx, y];
			if (side.HasTile && Main.tileRope[side.TileType])
				return true;
			if (TileQuery.IsSolid(x - dx, y))
				return false;
		}
		return false;
	}

	public static bool KeptRope(int x, int y)
	{
		if (Building.HouseInspector.IsProtected(x, y))
			return true;
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (faction.Hellevator is { } shaft && System.Math.Abs(x - shaft.RopeX) <= 1)
				return true;
			if (World.SkyArena.For(faction) is { } arena && System.Math.Abs(x - arena.CenterX) <= 2)
				return true;
		}
		return false;
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
		Bot.Motor.PlacementForbidden = true;
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
			workingOn = null;
			Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
			if (Bot.Motor.IsStuck || Bot.Motor.HasArrived)
				GiveUp(tile);
			return;
		}
		if (workingOn != tile)
		{
			workingOn = tile;
			workingSince = Main.GameUpdateCount;
		}
		else if (Main.GameUpdateCount - workingSince > TargetPatienceTicks)
		{
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
		{
			if (Main.tileRope[Main.tile[tile.X, tile.Y].TileType])
				SweepRopes(tile);
			Accomplished = true;
		}
	}

	const int RopeSweepRadius = 10;

	static void SweepRopes(Point around)
	{
		for (int x = around.X - RopeSweepRadius; x <= around.X + RopeSweepRadius; x++)
		{
			for (int y = around.Y - RopeSweepRadius; y <= around.Y + RopeSweepRadius; y++)
			{
				if (!WorldGen.InWorld(x, y, 10) || x == around.X && y == around.Y || !IsClutter(x, y) || !Main.tileRope[Main.tile[x, y].TileType] || BotNear(new Point(x, y)))
					continue;
				WorldGen.KillTile(x, y, noItem: true);
				if (Main.netMode == Terraria.ID.NetmodeID.Server)
					NetMessage.SendTileSquare(-1, x, y);
			}
		}
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
