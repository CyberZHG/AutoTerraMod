using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public enum BuildAction
{
	ChopTree,
	ClearTile,
	PlaceWall,
	PlaceBlock,
	PlaceDoor,
	PlaceTorch,
	PlaceTable,
	PlaceChair,
	PlaceBed,
	RemoveScaffold,
	RenovateBlock,
	RenovateWall
}

public enum StructurePurpose
{
	Hall,
	Storage,
	Workshop,
	Bedroom,
	GuestRoom
}

public readonly record struct BuildOperation(BuildAction Action, Point Tile, int Structure);

public sealed class HouseBlueprint
{
	const int DoorHeight = 3;
	const int ShedRoofRun = 3;
	const int ChestWidth = 2;

	readonly List<Point> shellInBuildOrder = new();
	readonly HashSet<Point> shell = new();
	readonly int center;

	public HouseBlueprint(int index, StructurePurpose purpose, int left, int width, int floor, int interiorHeight, RoofStyle roof)
	{
		Index = index;
		Purpose = purpose;
		Left = left;
		Right = left + width - 1;
		Floor = floor;
		Ceiling = floor - 1 - interiorHeight;
		Roof = roof;
		center = (Left + Right) / 2;
		AddFloor();
		AddSideWalls();
		AddCeiling();
		AddRoof();
		Top = shell.Min(tile => tile.Y);
	}

	public int Index { get; }
	public StructurePurpose Purpose { get; }
	public int Left { get; }
	public int Right { get; }
	public int Floor { get; }
	public int Ceiling { get; }
	public int Top { get; }
	public RoofStyle Roof { get; }
	public Point InsideProbe => new(center, Floor - 2);
	public Vector2 CenterWorld => new(center * 16f + 8f, (Floor - 1) * 16f - 8f);

	public bool IsInterior(int x, int y) => x > Left && x < Right && y > Ceiling && y < Floor;
	public bool IsDoorway(int x, int y) => (x == Left || x == Right) && y >= Floor - DoorHeight && y < Floor;
	public bool IsShell(int x, int y) => shell.Contains(new Point(x, y));
	public bool IsOutOfReachFromFloor(Point tile) => tile.Y < Floor - 7;

	public IEnumerable<Point> ChestSlots()
	{
		if (Purpose != StructurePurpose.Storage)
			yield break;
		for (int x = Left + 1; x + ChestWidth - 1 < Right; x += ChestWidth)
			yield return new Point(x, Floor - 1);
	}

	public IEnumerable<BuildOperation> PendingOperations(HouseDesign materials)
	{
		foreach (var tile in SpaceToClear().Distinct())
		{
			var action = TileQuery.IsChoppableTree(TileQuery.TypeAt(tile)) ? BuildAction.ChopTree : BuildAction.ClearTile;
			yield return Operation(action, tile);
		}
		foreach (var tile in InteriorTiles().Where(tile => Main.tile[tile.X, tile.Y].WallType == WallID.None))
			yield return Operation(BuildAction.PlaceWall, tile);
		foreach (var tile in shellInBuildOrder.Where(tile => !TileQuery.IsSolid(tile)))
			yield return Operation(BuildAction.PlaceBlock, tile);
		foreach (int x in new[] { Left, Right })
		{
			if (!HasTileOfSet(x, Floor - 2, TileID.Sets.RoomNeeds.CountsAsDoor))
				yield return Operation(BuildAction.PlaceDoor, new Point(x, Floor - 2));
		}
		if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsTorch))
			yield return Operation(BuildAction.PlaceTorch, new Point(center, Ceiling + 1));
		if (Purpose == StructurePurpose.Bedroom && !InteriorHas(new int[] { TileID.Beds }))
			yield return Operation(BuildAction.PlaceBed, new Point(Left + 2, Floor - 1));
		if (Purpose != StructurePurpose.Storage)
		{
			if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsTable))
				yield return Operation(BuildAction.PlaceTable, new Point(center + 2, Floor - 1));
			if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsChair))
				yield return Operation(BuildAction.PlaceChair, new Point(center + 1, Floor - 1));
		}
		foreach (var tile in shellInBuildOrder.Where(tile => NeedsNewBlock(tile, materials)))
			yield return Operation(BuildAction.RenovateBlock, tile);
		foreach (var tile in InteriorTiles().Where(tile => NeedsNewWall(tile, materials)))
			yield return Operation(BuildAction.RenovateWall, tile);
	}

	BuildOperation Operation(BuildAction action, Point tile) => new(action, tile, Index);

	public int PendingCount(BuildAction action, HouseDesign materials) => PendingOperations(materials).Count(operation => operation.Action == action);

	public IEnumerable<Point> FurnitureSpots(Point preferred)
	{
		yield return preferred;
		for (int x = Left + 1; x < Right; x++)
		{
			if (x != preferred.X)
				yield return new Point(x, Floor - 1);
		}
	}

	public IEnumerable<Point> FloorSpotsNearCenter()
	{
		for (int offset = 1; Left + 1 <= center - offset || center + offset <= Right - 1; offset++)
		{
			foreach (int x in new[] { center + offset, center - offset })
			{
				if (x > Left + 1 && x < Right - 1)
					yield return new Point(x, Floor - 1);
			}
		}
	}

	public Point? FindTileInside(int tileType)
	{
		foreach (var tile in InteriorTiles())
		{
			if (Main.tile[tile.X, tile.Y].HasTile && Main.tile[tile.X, tile.Y].TileType == tileType)
				return tile;
		}
		return null;
	}

	bool NeedsNewBlock(Point tile, HouseDesign materials)
	{
		if (!TileQuery.IsSolid(tile) || Main.tile[tile.X, tile.Y].TileType == materials.BlockTile)
			return false;
		var above = Main.tile[tile.X, tile.Y - 1];
		return !(above.HasTile && Main.tileFrameImportant[above.TileType]);
	}

	static bool NeedsNewWall(Point tile, HouseDesign materials)
	{
		int wall = Main.tile[tile.X, tile.Y].WallType;
		return wall != WallID.None && wall != materials.WallType && Main.wallHouse[wall];
	}

	void AddShell(int x, int y)
	{
		var tile = new Point(x, y);
		if (shell.Add(tile))
			shellInBuildOrder.Add(tile);
	}

	void AddFloor()
	{
		for (int offset = 0; offset <= Right - Left; offset++)
		{
			foreach (int x in new[] { center + offset, center - offset })
			{
				if (x >= Left && x <= Right)
					AddShell(x, Floor);
			}
		}
	}

	void AddSideWalls()
	{
		for (int y = Floor - 1; y >= Ceiling; y--)
		{
			foreach (int x in new[] { Left, Right })
			{
				if (!IsDoorway(x, y))
					AddShell(x, y);
			}
		}
	}

	void AddCeiling()
	{
		for (int offset = 1; Left + offset <= Right - offset; offset++)
		{
			AddShell(Left + offset, Ceiling);
			AddShell(Right - offset, Ceiling);
		}
	}

	void AddRoof()
	{
		switch (Roof)
		{
			case RoofStyle.Gable:
				for (int rise = 1; Right - rise - (Left + rise) >= 0; rise++)
				{
					AddShell(Left + rise, Ceiling - rise);
					AddShell(Right - rise, Ceiling - rise);
					if (Right - rise - (Left + rise) <= 1)
					{
						for (int x = Left + rise; x <= Right - rise; x++)
							AddShell(x, Ceiling - rise);
					}
				}
				break;
			case RoofStyle.Shed:
				for (int x = Left; x <= Right; x++)
				{
					int rise = (Right - x) / ShedRoofRun;
					for (int y = Ceiling - 1; y >= Ceiling - rise; y--)
						AddShell(x, y);
				}
				break;
		}
	}

	IEnumerable<Point> SpaceToClear()
	{
		for (int x = Left; x <= Right; x++)
		{
			for (int y = Top; y < Floor; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile)
					continue;
				if (TileQuery.IsChoppableTree(tile.TileType))
				{
					yield return TreeBaseBelow(x, y);
					continue;
				}
				if (IsShell(x, y) && !TileQuery.IsSolid(x, y) && !TileID.Sets.RoomNeeds.CountsAsDoor.Contains(tile.TileType))
				{
					yield return new Point(x, y);
					continue;
				}
				bool inLivingSpace = IsInterior(x, y) || IsDoorway(x, y);
				if (!inLivingSpace || TileID.Sets.RoomNeeds.CountsAsDoor.Contains(tile.TileType))
					continue;
				if (TileQuery.IsSolid(x, y) || Main.tileCut[tile.TileType])
					yield return new Point(x, y);
			}
		}
		foreach (int x in new[] { Left - 1, Right + 1 })
		{
			for (int y = Floor - DoorHeight; y < Floor; y++)
			{
				if (TileQuery.IsSolid(x, y) && !Main.tileFrameImportant[Main.tile[x, y].TileType] && !HouseInspector.IsProtected(x, y))
					yield return new Point(x, y);
			}
		}
	}

	static Point TreeBaseBelow(int x, int y)
	{
		int treeType = Main.tile[x, y].TileType;
		while (Main.tile[x, y + 1].HasTile && Main.tile[x, y + 1].TileType == treeType)
			y++;
		return new Point(x, y);
	}

	IEnumerable<Point> InteriorTiles()
	{
		for (int y = Floor - 1; y > Ceiling; y--)
		{
			for (int x = Left + 1; x < Right; x++)
				yield return new Point(x, y);
		}
	}

	bool InteriorHas(int[] tileTypes) => InteriorTiles().Any(tile => HasTileOfSet(tile.X, tile.Y, tileTypes));

	static bool HasTileOfSet(int x, int y, int[] tileTypes) =>
		Main.tile[x, y].HasTile && tileTypes.Contains(Main.tile[x, y].TileType);
}
