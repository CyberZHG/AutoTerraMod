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
	RenovateWall,
	PlacePlatform,
	Foundation
}

public enum StructurePurpose
{
	Hall,
	Storage,
	Workshop,
	Bedroom,
	GuestRoom,
	Gallery
}

public readonly record struct BuildOperation(BuildAction Action, Point Tile, int Structure);

public sealed class HouseBlueprint
{
	const int DoorHeight = 3;
	const int ChestWidth = 2;

	const int StepHeight = 3;
	const int FoundationDepth = 2;

	readonly List<Point> shellInBuildOrder = new();
	readonly HashSet<Point> shell = new();
	readonly List<Point> platforms = new();
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
	public Point InsideProbe
	{
		get
		{
			var preferred = new Point(center, Floor - 2);
			return InteriorTiles()
				.Where(tile => !(Main.tile[tile.X, tile.Y].HasTile && Main.tileSolid[Main.tile[tile.X, tile.Y].TileType]))
				.Cast<Point?>()
				.MinBy(tile => System.Math.Abs(tile.Value.X - preferred.X) + System.Math.Abs(tile.Value.Y - preferred.Y)) ?? preferred;
		}
	}
	public Vector2 CenterWorld => new(center * 16f + 8f, (Floor - 1) * 16f - 8f);

	public bool IsInterior(int x, int y) => x > Left && x < Right && y > Ceiling && y < Floor;
	public bool IsDoorway(int x, int y) => HasDoorAt(x) && y >= Floor - DoorHeight && y < Floor;
	public bool HasDoorAt(int x) => (x == Left || x == Right) && !closedSides.Contains(x);
	public IEnumerable<Point> ShellTiles => shell;

	readonly HashSet<int> closedSides = new();

	public void CloseSide(int x, System.Func<int, int, bool> claimed)
	{
		closedSides.Add(x);
		for (int y = Ceiling; y < Floor; y++)
		{
			var tile = new Point(x, y);
			if (!claimed(x, y))
				AddShell(x, y);
			else if (shell.Remove(tile))
				shellInBuildOrder.Remove(tile);
		}
	}
	public bool IsShell(int x, int y) => shell.Contains(new Point(x, y));
	bool IsPlatformSpot(Point tile) => platforms.Contains(tile);

	readonly HashSet<int> hatchColumns = new();
	const int ShelfMinimumHeight = 5;
	const int ShelfRow = 3;

	public bool HasShelf { get; private set; }

	public void AddHatchAbove(int? at = null)
	{
		int start = at ?? center;
		foreach (int x in new[] { start, start + 1 })
		{
			hatchColumns.Add(x);
			platforms.Add(new Point(x, Ceiling));
			var step = new Point(x, Floor - StepHeight);
			if (!platforms.Contains(step))
				platforms.Add(step);
		}
	}

	public void AddRoofHatch()
	{
		if (hatchColumns.Any(column => platforms.Contains(new Point(column, Ceiling))) || center - 1 <= Left || center + 1 >= Right)
			return;
		for (int x = center - 1; x <= center + 1; x++)
		{
			hatchColumns.Add(x);
			platforms.Add(new Point(x, Ceiling));
			var step = new Point(x, Floor - StepHeight);
			if (!platforms.Contains(step))
				platforms.Add(step);
		}
	}

	public void AddHatchBelow(int? at = null)
	{
		int start = at ?? center;
		foreach (int x in new[] { start, start + 1 })
		{
			hatchColumns.Add(x);
			platforms.Add(new Point(x, Floor));
		}
	}

	public void KeepHatchesOf(HouseBlueprint below)
	{
		foreach (var spot in below.platforms.Where(spot => spot.Y == Floor && spot.X > Left && spot.X < Right).ToList())
		{
			hatchColumns.Add(spot.X);
			platforms.Add(spot);
		}
	}

	public void AddShelf()
	{
		if (Floor - 1 - Ceiling < ShelfMinimumHeight)
			return;
		HasShelf = true;
		for (int x = Left + 2; x <= Right - 2; x++)
		{
			var shelf = new Point(x, Floor - ShelfRow);
			if (!platforms.Contains(shelf))
				platforms.Add(shelf);
		}
	}

	bool NearHatch(int x) => hatchColumns.Any(column => System.Math.Abs(column - x) <= 2);
	public void AddFloorPlatforms(int fromX, int toX)
	{
		for (int x = fromX; x <= toX; x++)
			platforms.Add(new Point(x, Floor));
	}

	static bool IsFurniture(Point tile) => Main.tile[tile.X, tile.Y].HasTile && Main.tileFrameImportant[Main.tile[tile.X, tile.Y].TileType] && !Main.tileRope[Main.tile[tile.X, tile.Y].TileType] && !TileQuery.IsChoppableTree(Main.tile[tile.X, tile.Y].TileType);

	static bool HoldsFurniture(Point tile)
	{
		var above = Main.tile[tile.X, tile.Y - 1];
		return above.HasTile && Main.tileFrameImportant[above.TileType] && !TileQuery.IsChoppableTree(above.TileType);
	}

	public System.Func<IEnumerable<Point>> Yard { get; set; }

	public bool IsOutOfReachFromFloor(Point tile) => tile.Y < Floor - 7;

	public IEnumerable<Point> ChestSlots()
	{
		if (Purpose != StructurePurpose.Storage)
			yield break;
		for (int x = Left + 1; x + ChestWidth - 1 < Right; x += ChestWidth)
		{
			if (x > Left + 1 && x + ChestWidth - 1 < Right - 1)
				yield return new Point(x, Floor - 1);
		}
		if (!HasShelf)
			yield break;
		for (int x = Left + 2; x + ChestWidth - 1 <= Right - 2; x += ChestWidth)
		{
			if (hatchColumns.Contains(x) || hatchColumns.Contains(x + 1) || x == center || x + 1 == center)
				continue;
			if (TileQuery.IsPlatform(x, Floor - ShelfRow) && TileQuery.IsPlatform(x + 1, Floor - ShelfRow))
				yield return new Point(x, Floor - ShelfRow - 1);
		}
	}

	public IEnumerable<BuildOperation> PendingOperations(HouseDesign materials)
	{
		foreach (var tile in SpaceToClear().Distinct())
		{
			var action = TileQuery.IsChoppableTree(TileQuery.TypeAt(tile)) ? BuildAction.ChopTree : BuildAction.ClearTile;
			yield return Operation(action, tile);
		}
		var frame = shellInBuildOrder.Where(tile => !IsPlatformSpot(tile) && !TileQuery.IsSolid(tile) && !Hellevator.InAnyShaft(tile.X, tile.Y) && !(Main.tile[tile.X, tile.Y].HasTile && Main.tileRope[Main.tile[tile.X, tile.Y].TileType] && Bots.Tasks.TidyCampTask.IsLifelineColumn(tile.X))).ToList();
		foreach (var tile in frame.Where(tile => tile.Y == Floor).OrderBy(tile => Bots.BotHands.Anchored(tile) ? 0 : 1))
			yield return Operation(BuildAction.PlaceBlock, tile);
		foreach (var tile in InteriorTiles().Concat(shellInBuildOrder.Where(tile => tile.Y < Floor && !Hellevator.InAnyShaft(tile.X, tile.Y))).Where(tile => Main.tile[tile.X, tile.Y].WallType == WallID.None).OrderByDescending(tile => tile.Y))
			yield return Operation(BuildAction.PlaceWall, tile);
		foreach (var tile in platforms.Where(tile => Main.tile[tile.X, tile.Y].HasTile && !TileQuery.IsPlatform(tile.X, tile.Y) && !HoldsFurniture(tile) && !IsFurniture(tile)))
			yield return Operation(BuildAction.ClearTile, tile);
		foreach (var tile in frame.Where(tile => tile.Y != Floor).OrderBy(tile => Bots.BotHands.Anchored(tile) ? 0 : 1))
			yield return Operation(BuildAction.PlaceBlock, tile);
		foreach (var tile in platforms.Where(tile => !TileQuery.IsPlatform(tile.X, tile.Y) && !TileQuery.IsSolid(tile) && !IsFurniture(tile)))
			yield return Operation(BuildAction.PlacePlatform, tile);
		for (int y = Floor; y <= Floor + FoundationDepth; y++)
			for (int x = Left; x <= Right; x++)
				if (TileQuery.IsSolid(x, y) && TileID.Sets.Falling[Main.tile[x, y].TileType] && !Hellevator.InAnyShaft(x, y))
					yield return Operation(BuildAction.Foundation, new Point(x, y));
		foreach (int x in new[] { Left, Right }.Where(HasDoorAt))
		{
			if (IsRealDoor(x, Floor - 2) && !(IsRealDoor(x, Floor - DoorHeight) && IsRealDoor(x, Floor - 1)))
			{
				yield return Operation(BuildAction.ClearTile, new Point(x, Floor - 2));
				continue;
			}
			if (IsRealDoor(x, Floor - 2))
			{
				foreach (var blocker in SwingToClear(x))
					yield return Operation(BuildAction.ClearTile, blocker);
				continue;
			}
			for (int y = Floor - DoorHeight; y < Floor; y++)
			{
				if (Main.tile[x, y].HasTile && !IsRealDoor(x, y) && !TileQuery.IsSolid(x, y))
					yield return Operation(BuildAction.ClearTile, new Point(x, y));
			}
			for (int y = Top + 1; y < Floor - DoorHeight; y++)
			{
				if (!TileQuery.IsSolid(x, y) && !IsPlatformSpot(new Point(x, y)))
					yield return Operation(BuildAction.PlaceBlock, new Point(x, y));
			}
			yield return Operation(BuildAction.PlaceDoor, new Point(x, Floor - 2));
		}
		if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsTorch))
			yield return Operation(BuildAction.PlaceTorch, TorchSpot());
		if (Purpose == StructurePurpose.Bedroom && !InteriorHas(new int[] { TileID.Beds }))
			yield return Operation(BuildAction.PlaceBed, new Point(Left + 3, Floor - 1));
		if (Purpose != StructurePurpose.Storage)
		{
			if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsTable))
				yield return Operation(BuildAction.PlaceTable, new Point(center + 2, Floor - 1));
			if (!InteriorHas(TileID.Sets.RoomNeeds.CountsAsChair))
				yield return Operation(BuildAction.PlaceChair, new Point(center + 1, Floor - 1));
		}
		foreach (var tile in shellInBuildOrder.Where(tile => !IsPlatformSpot(tile) && NeedsNewBlock(tile, materials)))
			yield return Operation(BuildAction.RenovateBlock, tile);
		foreach (var tile in InteriorTiles().Where(tile => NeedsNewWall(tile, materials)))
			yield return Operation(BuildAction.RenovateWall, tile);
		if (Yard != null)
		{
			foreach (var tile in Yard())
				yield return Operation(BuildAction.ClearTile, tile);
		}
	}

	BuildOperation Operation(BuildAction action, Point tile) => new(action, tile, Index);

	public int PendingCount(BuildAction action, HouseDesign materials) => PendingOperations(materials).Count(operation => operation.Action == action);

	public IEnumerable<Point> FurnitureSpots(Point preferred, int reachLeft = 1, int reachRight = 1)
	{
		bool Fits(int x) => x - reachLeft >= Left + 2 && x + reachRight <= Right - 2 && !Enumerable.Range(x - reachLeft, reachLeft + reachRight + 1).Any(column => OverShaft(column, 0));
		if (Fits(preferred.X))
			yield return preferred;
		for (int x = Left + 2; x <= Right - 2; x++)
		{
			if (x != preferred.X && Fits(x))
				yield return new Point(x, Floor - 1);
		}
	}

	bool OverShaft(int x, int margin = 2)
	{
		for (int dx = -margin; dx <= margin; dx++)
			if (Hellevator.InAnyShaft(x + dx, Floor + 1))
				return true;
		return false;
	}

	public IEnumerable<Point> FloorSpotsNearCenter()
	{
		for (int offset = 1; Left + 1 <= center - offset || center + offset <= Right - 1; offset++)
		{
			foreach (int x in new[] { center + offset, center - offset })
			{
				if (x > Left + 2 && x < Right - 2 && !IsPlatformSpot(new Point(x, Floor)) && !NearHatch(x))
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
		int type = Main.tile[tile.X, tile.Y].TileType;
		if (!TileQuery.IsSolid(tile) || materials.AcceptsBlockTile(type) && !PlacementRules.IsEvil(type))
			return false;
		return WorldGen.IsTileReplacable(tile.X, tile.Y);
	}

	static bool NeedsNewWall(Point tile, HouseDesign materials)
	{
		int wall = Main.tile[tile.X, tile.Y].WallType;
		return wall != WallID.None && (!Main.wallHouse[wall] || !materials.AcceptsWall(wall));
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

	IEnumerable<Point> SpaceToClear()
	{
		for (int x = Left; x <= Right; x++)
		{
			for (int y = Top; y <= Floor; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile)
					continue;
				if (TileQuery.IsChoppableTree(tile.TileType))
				{
					yield return TreeBaseBelow(x, y);
					continue;
				}
				if (y == Floor)
					continue;
				if (IsPlatformSpot(new Point(x, y)) || Main.tileRope[tile.TileType] && (Bots.Tasks.TidyCampTask.IsLifelineColumn(x) || Bots.Tasks.TidyCampTask.BotNear(new Point(x, y))))
					continue;
				if (IsShell(x, y) && !TileQuery.IsSolid(x, y))
				{
					yield return new Point(x, y);
					continue;
				}
				if (y == Floor - 1 && IsInterior(x, y) && TileID.Sets.Platforms[tile.TileType])
				{
					yield return new Point(x, y);
					continue;
				}
				bool inLivingSpace = IsInterior(x, y) || IsDoorway(x, y);
				if (!inLivingSpace || TileID.Sets.RoomNeeds.CountsAsDoor.Contains(tile.TileType))
					continue;
				if (TileQuery.IsSolid(x, y) || Main.tileRope[tile.TileType] && !Bots.Tasks.TidyCampTask.KeptRope(x, y) || TileID.Sets.TreeSapling[tile.TileType])
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

	Point TorchSpot() =>
		InteriorTiles().Where(tile => !Main.tile[tile.X, tile.Y].HasTile && !IsPlatformSpot(tile)).OrderBy(tile => tile.Y).ThenBy(tile => System.Math.Abs(tile.X - center)).Cast<Point?>().FirstOrDefault()
		?? new Point(center, Ceiling + 1);

	IEnumerable<Point> InteriorTiles()
	{
		for (int y = Floor - 1; y > Ceiling; y--)
		{
			for (int x = Left + 1; x < Right; x++)
				yield return new Point(x, y);
		}
	}

	bool InteriorHas(int[] tileTypes) => InteriorTiles().Any(tile => HasTileOfSet(tile.X, tile.Y, tileTypes));

	IEnumerable<Point> SwingToClear(int doorX)
	{
		var left = SwingBlockers(doorX - 1);
		var right = SwingBlockers(doorX + 1);
		if (left.Count == 0 || right.Count == 0)
			return System.Array.Empty<Point>();
		return left.Concat(right).Where(tile => !IsPrecious(tile));
	}

	List<Point> SwingBlockers(int x)
	{
		var blockers = new List<Point>();
		for (int y = Floor - DoorHeight; y < Floor; y++)
		{
			var tile = Main.tile[x, y];
			if (tile.HasTile && !Main.tileCut[tile.TileType] && tile.TileType is not (TileID.ClosedDoor or TileID.OpenDoor))
				blockers.Add(new Point(x, y));
		}
		return blockers;
	}

	bool IsPrecious(Point tile)
	{
		int type = Main.tile[tile.X, tile.Y].TileType;
		return IsShell(tile.X, tile.Y) || TileQuery.IsSolid(tile.X, tile.Y) || type == TileID.DemonAltar || TileID.Sets.BasicChest[type] && !IsEmptyChest(tile);
	}

	static bool IsEmptyChest(Point tile)
	{
		int index = Chest.FindChestByGuessing(tile.X, tile.Y);
		return index >= 0 && Main.chest[index].item.All(item => item == null || item.IsAir);
	}

	static bool IsRealDoor(int x, int y) =>
		HasTileOfSet(x, y, TileID.Sets.RoomNeeds.CountsAsDoor) && !TileQuery.IsPlatform(x, y);

	static bool HasTileOfSet(int x, int y, int[] tileTypes) =>
		Main.tile[x, y].HasTile && tileTypes.Contains(Main.tile[x, y].TileType);
}
