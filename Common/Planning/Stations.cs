using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Planning;

public static class Stations
{
	public const int BaseHalfWidth = 8;
	const int BaseRowsAbove = 6;
	const int BaseRowsBelow = 2;
	const int CacheTicks = 60;

	static readonly Dictionary<int, int[]> VanillaAliases = new()
	{
		[TileID.Hellforge] = new int[] { TileID.Furnaces },
		[TileID.GlassKiln] = new int[] { TileID.Furnaces },
		[TileID.AdamantiteForge] = new int[] { TileID.Furnaces, TileID.Hellforge },
		[TileID.MythrilAnvil] = new int[] { TileID.Anvils },
		[TileID.BewitchingTable] = new int[] { TileID.Tables },
		[TileID.PicnicTable] = new int[] { TileID.Tables },
		[TileID.Tables2] = new int[] { TileID.Tables },
		[TileID.AlchemyTable] = new int[] { TileID.Bottles, TileID.Tables }
	};

	static readonly Dictionary<int, (uint tick, Dictionary<int, Point> locations)> baseCache = new();
	const int NearCampStationTiles = 60;
	static readonly HashSet<Point> NoExclusions = new();
	static Dictionary<int, List<int>> itemsPlacingTile;

	public static void Reset()
	{
		baseCache.Clear();
		itemsPlacingTile = null;
	}

	const uint FetchClaimTicks = 36000;
	static readonly Dictionary<(int faction, int tile), (int botId, uint until)> fetchers = new();

	public static void ClaimFetch(Bots.Bot bot, IEnumerable<int> stationItems)
	{
		foreach (int item in stationItems)
			fetchers[(bot.FactionId, ContentSamples.ItemsByType[item].createTile)] = (bot.Id, Main.GameUpdateCount + FetchClaimTicks);
	}

	public static bool TeammateFetching(Bots.Bot bot, IEnumerable<int> stationItems)
	{
		foreach (var member in Systems.BotWorldSystem.Bots)
		{
			if (member != bot && member.FactionId == bot.FactionId && member.IsAlive && stationItems.Any(item => Bots.Inventory.Count(member.Avatar, item) > 0))
				return true;
		}
		foreach (int item in stationItems)
		{
			if (fetchers.TryGetValue((bot.FactionId, ContentSamples.ItemsByType[item].createTile), out var claim) && claim.botId != bot.Id
				&& Main.GameUpdateCount < claim.until && Systems.BotWorldSystem.FindBot(claim.botId) is { IsAlive: true })
				return true;
		}
		return false;
	}

	static HashSet<int> craftingTiles;

	public static bool IsCraftingStation(int tileType)
	{
		craftingTiles ??= Main.recipe.Take(Recipe.numRecipes).SelectMany(recipe => recipe.requiredTile).Where(required => required >= 0).ToHashSet();
		return tileType >= 0 && craftingTiles.Contains(tileType);
	}

	public static bool BaseHas(Faction faction, int tileType) => StationsAtBase(faction).ContainsKey(tileType);

	public static Point? Locate(Faction faction, int tileType) =>
		StationsAtBase(faction).TryGetValue(tileType, out var location) ? location : null;

	public static void Invalidate(Faction faction) => baseCache.Remove(faction.Id);

	static Dictionary<int, Point> StationsAtBase(Faction faction)
	{
		if (baseCache.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.tick < CacheTicks)
			return cached.locations;
		var locations = new Dictionary<int, Point>();
		var origin = faction.BaseTile;
		var settlement = faction.Settlement;
		var columns = settlement != null
			? Enumerable.Range(settlement.Left, settlement.Right - settlement.Left + 1).Select(x => (x, top: settlement.Top, bottom: settlement.Floor))
			: Enumerable.Range(origin.X - BaseHalfWidth, BaseHalfWidth * 2 + 1).Select(x => (x, top: origin.Y - BaseRowsAbove, bottom: origin.Y + BaseRowsBelow));
		columns = columns.Concat(OutdoorColumns(faction, OutdoorScanRange).Select(x => (x, top: World.TileQuery.SurfaceY(x) - BaseRowsAbove, bottom: World.TileQuery.SurfaceY(x) + BaseRowsBelow)));
		foreach (var (x, top, bottom) in columns)
		{
			for (int y = top; y <= bottom; y++)
			{
				if (!WorldGen.InWorld(x, y, 2) || !Main.tile[x, y].HasTile)
					continue;
				var here = new Point(x, y);
				foreach (int provided in ProvidedStations(Main.tile[x, y].TileType))
				{
					if (!locations.TryGetValue(provided, out var known) || World.WorldScan.DistanceSq(origin, here) < World.WorldScan.DistanceSq(origin, known))
						locations[provided] = here;
				}
			}
		}
		var knowledge = Systems.BotWorldSystem.KnowledgeOf(faction);
		int nearCamp = NearCampStationTiles + (settlement == null ? 0 : System.Math.Max(origin.X - settlement.Left, settlement.Right - origin.X));
		foreach (int furniture in World.WorldScan.LootableFurniture)
		{
			if (locations.ContainsKey(furniture) || knowledge.NearestKnown(origin, new HashSet<int> { furniture }, NoExclusions, nearCamp) is not Point nearby)
				continue;
			foreach (int provided in ProvidedStations(furniture))
				locations.TryAdd(provided, nearby);
		}
		baseCache[faction.Id] = (Main.GameUpdateCount, locations);
		return locations;
	}

	static IEnumerable<int> ProvidedStations(int tileType)
	{
		yield return tileType;
		if (VanillaAliases.TryGetValue(tileType, out var aliases))
		{
			foreach (int alias in aliases)
				yield return alias;
		}
		if (TileLoader.GetTile(tileType) is ModTile modTile)
		{
			foreach (int alias in modTile.AdjTiles)
				yield return alias;
		}
	}

	const int RemoteStationSearchRadius = 500;
	const int WorldStationSearchRadius = 3000;

	public static bool IsWorldOnly(int tileType) => ItemsPlacing(tileType).Count == 0;

	public static Point? LocateAnywhere(Faction faction, int tileType, Point from) =>
		Locate(faction, tileType)
		?? Systems.BotWorldSystem.KnowledgeOf(faction).NearestKnown(from, new HashSet<int> { tileType }, new HashSet<Point>(), IsWorldOnly(tileType) ? WorldStationSearchRadius : RemoteStationSearchRadius);

	public static bool IsReachableStation(Faction faction, int tileType, Point from) =>
		BaseHas(faction, tileType) || (IsWorldOnly(tileType) && LocateAnywhere(faction, tileType, from) != null);

	public static IReadOnlyList<int> ItemsPlacing(int tileType)
	{
		itemsPlacingTile ??= ContentSamples.ItemsByType.Values
			.Where(item => item.createTile >= 0)
			.GroupBy(item => item.createTile)
			.ToDictionary(group => group.Key, group => group.Select(item => item.type).OrderBy(type => type).ToList());
		return itemsPlacingTile.TryGetValue(tileType, out var items) ? items : new List<int>();
	}

	public static IEnumerable<Point> PlacementSpots(Faction faction, Item item)
	{
		var spots = AllPlacementSpots(faction, item);
		if (Building.BaseStorage.IsStorageItem(item) || faction.Settlement is not { } camp)
			return spots;
		var storage = camp.StorageRooms.ToList();
		return spots.Where(spot => !storage.Any(room => spot.X >= room.Left && spot.X <= room.Right && spot.Y >= room.Top && spot.Y <= room.Floor + 1));
	}

	static IEnumerable<Point> AllPlacementSpots(Faction faction, Item item)
	{
		var settlement = faction.Settlement;
		if (settlement == null)
			yield break;
		if (Building.BaseStorage.IsStorageItem(item))
		{
			foreach (var room in settlement.StorageRooms.Where(room => room == settlement.Storage || Building.HouseInspector.IsComplete(faction, room)))
				foreach (var slot in room.ChestSlots())
					yield return slot;
			yield break;
		}
		bool hobby = item.type == ItemID.Mannequin || Bots.Tasks.NaturalistTask.IsCage(item);
		if (hobby && Building.HouseInspector.IsComplete(faction, settlement.Gallery))
			foreach (var spot in settlement.Gallery.FloorSpotsNearCenter())
				yield return spot;
		if (hobby)
			foreach (var spot in HighwaySpots(faction))
				yield return spot;
		if (settlement.Atelier is { } atelier && Building.HouseInspector.IsComplete(faction, atelier))
			foreach (var spot in atelier.FloorSpotsNearCenter())
				yield return spot;
		foreach (var spot in settlement.Workshop.FloorSpotsNearCenter())
			yield return spot;
		foreach (var spot in settlement.Hall.FloorSpotsNearCenter())
		{
			yield return spot;
			yield return new Point(spot.X, spot.Y - 1);
			yield return new Point(spot.X, spot.Y + 1);
		}
		foreach (var spot in settlement.Hall.FloorSpotsNearCenter())
		{
			int groundY = GroundBelow(spot.X, settlement.Hall.Ceiling + 1);
			if (groundY > 0)
				yield return new Point(spot.X, groundY - 1);
		}
		foreach (int x in OutdoorColumns(faction))
		{
			int y = World.TileQuery.SurfaceY(x) - 1;
			if (y < settlement.Floor + OutdoorDrop)
				yield return new Point(x, y);
		}
		if (item.createTile >= 0 && Main.tileFrameImportant[item.createTile] && !Main.tileTable[item.createTile])
			foreach (var top in TableTops(settlement))
				yield return top;
	}

	static IEnumerable<Point> TableTops(Building.Settlement settlement)
	{
		for (int x = settlement.Left; x <= settlement.Right; x++)
			for (int y = settlement.Top; y <= settlement.Floor; y++)
				if (Main.tile[x, y].HasTile && Main.tileTable[Main.tile[x, y].TileType] && !Main.tile[x, y - 1].HasTile)
					yield return new Point(x, y - 1);
	}

	const int HighwayDisplayEvery = 8;
	const int HighwayDisplayReach = 120;

	static IEnumerable<Point> HighwaySpots(Faction faction)
	{
		foreach (var highway in World.Highway.For(faction))
		{
			for (int step = HighwayDisplayEvery; step <= HighwayDisplayReach; step += HighwayDisplayEvery)
			{
				int x = highway.StartX + highway.Direction * step;
				if (!highway.Spans(x) || !highway.IsBuilt(x) || !highway.IsBuilt(x + 1) || !highway.IsBuilt(x - 1))
					break;
				yield return new Point(x, highway.Y);
			}
		}
	}

	const int OutdoorPlacementRange = 8;
	const int OutdoorScanRange = 30;
	const int OutdoorClearance = 2;

	const int OutdoorDrop = 20;

	public static bool IsMisplaced(Faction faction, int x, int y)
	{
		if (faction.Settlement is not { } settlement || !Main.tile[x, y].HasTile)
			return false;
		int type = Main.tile[x, y].TileType;
		if (type == TileID.TeleportationPylon)
			return Bots.PylonTravel.IsStranded(x, y);
		if (!IsCraftingStation(type) || IsWorldOnly(type) || Main.tileContainer[type])
			return false;
		if (settlement.StorageRooms.Any(room => x > room.Left && x < room.Right && y >= room.Top && y < room.Floor))
			return true;
		int gap = x < settlement.Left ? settlement.Left - x : x - settlement.Right;
		return gap > OutdoorPlacementRange + OutdoorClearance && gap <= OutdoorScanRange + OutdoorClearance && y <= World.TileQuery.SurfaceY(x) && y < settlement.Floor + OutdoorDrop;
	}

	static IEnumerable<int> OutdoorColumns(Faction faction, int range = OutdoorPlacementRange)
	{
		var settlement = faction.Settlement;
		int left = (settlement?.Left ?? faction.BaseTile.X - BaseHalfWidth) - OutdoorClearance;
		int right = (settlement?.Right ?? faction.BaseTile.X + BaseHalfWidth) + OutdoorClearance;
		for (int offset = 0; offset <= range; offset++)
		{
			yield return right + offset;
			yield return left - offset;
		}
	}

	static int GroundBelow(int x, int fromY)
	{
		for (int y = fromY; y < fromY + 30; y++)
		{
			if (World.TileQuery.CanStandOn(x, y))
				return y;
		}
		return -1;
	}
}
