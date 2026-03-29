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
	static Dictionary<int, List<int>> itemsPlacingTile;

	public static void Reset()
	{
		baseCache.Clear();
		itemsPlacingTile = null;
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
		columns = columns.Concat(OutdoorColumns(faction).Select(x => (x, top: World.TileQuery.SurfaceY(x) - BaseRowsAbove, bottom: World.TileQuery.SurfaceY(x) + BaseRowsBelow)));
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
		var settlement = faction.Settlement;
		if (settlement == null)
			yield break;
		if (Building.BaseStorage.IsStorageItem(item))
		{
			foreach (var slot in settlement.Storage.ChestSlots())
				yield return slot;
			yield break;
		}
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
			if (y < settlement.Floor)
				yield return new Point(x, y);
		}
	}

	const int OutdoorPlacementRange = 30;
	const int OutdoorClearance = 2;

	static IEnumerable<int> OutdoorColumns(Faction faction)
	{
		var settlement = faction.Settlement;
		int left = (settlement?.Left ?? faction.BaseTile.X - BaseHalfWidth) - OutdoorClearance;
		int right = (settlement?.Right ?? faction.BaseTile.X + BaseHalfWidth) + OutdoorClearance;
		for (int offset = 0; offset <= OutdoorPlacementRange; offset++)
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
