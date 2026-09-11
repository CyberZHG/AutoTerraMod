using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class HerbGarden
{
	public const int Size = 30;
	const int HighwayReach = 28;
	public const int Seed = ItemID.DaybloomSeeds;
	public const int Box = ItemID.DayBloomPlanterBox;
	public const int HerbStyle = 0;
	const int DoorClearance = 3;
	const uint ScanTicks = 300;

	static readonly Dictionary<int, (uint at, List<Point> boxes)> scans = new();

	public static IReadOnlyList<Point> Boxes(Factions.Faction faction)
	{
		if (faction.Settlement is not { } settlement)
			return System.Array.Empty<Point>();
		if (!scans.TryGetValue(faction.Id, out var scan) || Main.GameUpdateCount - scan.at >= ScanTicks)
		{
			var boxes = Columns(settlement)
				.Select(x => new Point(x, settlement.Floor))
				.Where(spot => WorldGen.InWorld(spot.X, spot.Y, 5) && Main.tile[spot.X, spot.Y].HasTile && Main.tile[spot.X, spot.Y].TileType == TileID.PlanterBox)
				.ToList();
			scans[faction.Id] = scan = (Main.GameUpdateCount, boxes);
		}
		return scan.boxes;
	}

	public static void Forget(Factions.Faction faction) => scans.Remove(faction.Id);

	static IEnumerable<int> Columns(Building.Settlement settlement)
	{
		for (int x = settlement.Left - DoorClearance; x >= settlement.Left - Building.Settlement.ApronWidth - HighwayReach; x--)
			yield return x;
		for (int x = settlement.Right + DoorClearance; x <= settlement.Right + Building.Settlement.ApronWidth + HighwayReach; x++)
			yield return x;
	}

	public static bool IsHerb(Point box) => WorldScan.IsHerb(box.X, box.Y - 1, HerbStyle) || Main.tile[box.X, box.Y - 1].HasTile && Main.tile[box.X, box.Y - 1].TileType == TileID.ImmatureHerbs;

	public static bool IsRipe(Point box)
	{
		var herb = Main.tile[box.X, box.Y - 1];
		if (!herb.HasTile || herb.TileFrameX / 18 != HerbStyle)
			return false;
		return herb.TileType == TileID.BloomingHerbs || herb.TileType == TileID.MatureHerbs && Main.dayTime;
	}

	public static bool IsEmpty(Point box)
	{
		var above = Main.tile[box.X, box.Y - 1];
		return above.LiquidAmount == 0 && (!above.HasTile || IsWeed(above.TileType));
	}

	public static bool IsWeed(int tileType) => Main.tileCut[tileType] && tileType is not (TileID.ImmatureHerbs or TileID.MatureHerbs or TileID.BloomingHerbs);

	public static bool HasSeeds(Bot bot) =>
		Building.BaseStorage.Count(bot.Faction, Seed) > 0
		|| Systems.BotWorldSystem.Bots.Any(member => member.FactionId == bot.FactionId && Inventory.Count(member.Avatar, Seed) > 0)
		|| Boxes(bot.Faction).Any(IsHerb);

	public static int BoxesOnHand(Bot bot) =>
		Building.BaseStorage.Count(bot.Faction, Box)
		+ Systems.BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId).Sum(member => Inventory.Count(member.Avatar, Box));

	public static Point? FindBoxSpot(Bot bot)
	{
		if (bot.Faction.Settlement is not { } settlement)
			return null;
		return Columns(settlement)
			.Select(x => new Point(x, settlement.Floor))
			.Where(IsFreeSpot)
			.Cast<Point?>()
			.MinBy(spot => WorldScan.DistanceSq(bot.CenterTile, spot.Value));
	}

	public static bool OwnHighwayDeck(Point spot) =>
		Systems.BotWorldSystem.Factions.Any(faction => World.Highway.For(faction).Any(highway => highway.Y + 1 == spot.Y && highway.Spans(spot.X)))
		&& TileQuery.IsSolid(spot.X, spot.Y) && Main.tile[spot.X, spot.Y].TileType is TileID.Stone or TileID.GrayBrick or TileID.WoodBlock or TileID.Dirt;

	public static bool IsFreeSpot(Point spot)
	{
		if (!WorldGen.InWorld(spot.X, spot.Y, 10))
			return false;
		var ground = Main.tile[spot.X, spot.Y];
		if (ground.HasTile && ground.TileType != TileID.Platforms && !OwnHighwayDeck(spot))
			return false;
		for (int y = spot.Y - 2; y < spot.Y; y++)
		{
			var tile = Main.tile[spot.X, y];
			if (tile.HasTile && !Main.tileCut[tile.TileType] || tile.LiquidAmount > 0)
				return false;
		}
		return true;
	}
}
