using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace AutoTerraMod.Common.Building;

public sealed class Settlement
{
	const int StorageWidth = 16;
	const int WorkshopWidth = 24;
	const int StorageStories = 4;
	const int WorkshopStorageStories = 2;
	const int WorkshopHatchInset = 3;
	const int RoomWidth = 10;
	public const int ApronWidth = 5;
	const int RoomInteriorHeight = 5;
	const int MinimumStorageInteriorHeight = 4;
	public const int GuestRoomCount = 3;
	public const int GuestStories = 3;

	readonly List<HouseBlueprint> structures = new();
	readonly HashSet<Point> shell = new();

	public Settlement(Point baseTile, HouseDesign design, int bedroomCount)
	{
		int floor = baseTile.Y + 1;
		Hall = Add(StructurePurpose.Hall, baseTile.X - (design.Width - 1) / 2, design.Width, floor, design.InteriorHeight, design.Roof);
		Storage = Add(StructurePurpose.Storage, Hall.Right, StorageWidth, floor, Math.Max(MinimumStorageInteriorHeight, design.InteriorHeight), design.Roof);
		int nextRight = Hall.Left;
		for (int i = 0; i < bedroomCount; i++)
		{
			var bedroom = Add(StructurePurpose.Bedroom, nextRight - RoomWidth + 1, RoomWidth, floor, RoomInteriorHeight, design.Roof);
			Bedrooms.Add(bedroom);
			nextRight = bedroom.Left;
		}
		int nextLeft = Storage.Right;
		var groundRooms = new List<HouseBlueprint>();
		for (int i = 0; i < GuestRoomCount; i++)
		{
			var guestRoom = Add(StructurePurpose.GuestRoom, nextLeft, RoomWidth, floor, RoomInteriorHeight, GuestStories > 1 ? RoofStyle.Flat : design.Roof);
			GuestRooms.Add(guestRoom);
			groundRooms.Add(guestRoom);
			nextLeft = guestRoom.Right;
		}
		Workshop = Add(StructurePurpose.Workshop, nextLeft, WorkshopWidth, floor, RoomInteriorHeight, design.Roof);
		foreach (var ground in groundRooms)
		{
			var below = ground;
			for (int story = 1; story < GuestStories; story++)
			{
				var roof = story == GuestStories - 1 ? design.Roof : RoofStyle.Flat;
				below.AddHatchAbove();
				below = Add(StructurePurpose.GuestRoom, below.Left, RoomWidth, below.Ceiling, RoomInteriorHeight, roof);
				below.AddHatchBelow();
				GuestRooms.Add(below);
			}
		}
		var yardKeeper = structures[^1];
		yardTop = structures.Min(structure => structure.Top);
		Storage.AddShelf();
		var stack = Storage;
		for (int story = 1; story < StorageStories; story++)
			stack = StackStorage(stack, StorageWidth, null);
		stack.AddHatchAbove();
		Gallery = Add(StructurePurpose.Gallery, stack.Left, StorageWidth, stack.Ceiling, RoomInteriorHeight, design.Roof);
		Gallery.AddHatchBelow();
		int workshopHatch = Workshop.Right - WorkshopHatchInset;
		stack = Workshop;
		for (int story = 0; story < WorkshopStorageStories; story++)
			stack = StackStorage(stack, WorkshopWidth, workshopHatch);
		CloseMisalignedDoors();
		AddBalconies(floor);
		Left = structures.Min(structure => structure.Left);
		Right = structures.Max(structure => structure.Right);
		Top = structures.Min(structure => structure.Top);
		Floor = floor;
		AddApron(structures.Where(structure => structure.Floor == floor).MinBy(structure => structure.Left), Left - ApronWidth, Left - 1);
		AddApron(structures.Where(structure => structure.Floor == floor).MaxBy(structure => structure.Right), Right + 1, Right + ApronWidth);
		Hall.AddFloorPlatforms(Hall.Left + 1, Hall.Right - 1);
		yardKeeper.Yard = YardTiles;
	}

	public HouseBlueprint Hall { get; }
	public HouseBlueprint Storage { get; }
	public HouseBlueprint Workshop { get; }
	public HouseBlueprint Gallery { get; }
	public List<HouseBlueprint> StorageAnnexes { get; } = new();
	public IEnumerable<HouseBlueprint> StorageRooms => structures.Where(structure => structure.Purpose == StructurePurpose.Storage);
	public List<HouseBlueprint> Bedrooms { get; } = new();
	public List<HouseBlueprint> GuestRooms { get; } = new();
	public IReadOnlyList<HouseBlueprint> Structures => structures;
	public int Left { get; }
	public int Right { get; }
	public int Top { get; }
	public int Floor { get; }

	public bool IsShell(int x, int y) => shell.Contains(new Point(x, y));

	public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Floor;

	public HouseBlueprint StructureWithInterior(int x, int y) => structures.FirstOrDefault(structure => structure.IsInterior(x, y));

	const int YardHeadroom = 3;
	readonly int yardTop;

	HouseBlueprint StackStorage(HouseBlueprint below, int width, int? hatch)
	{
		below.AddHatchAbove(hatch);
		var room = Add(StructurePurpose.Storage, below.Left, width, below.Ceiling, RoomInteriorHeight, RoofStyle.Flat);
		room.AddShelf();
		room.AddHatchBelow(hatch);
		StorageAnnexes.Add(room);
		return room;
	}

	IEnumerable<Point> YardTiles()
	{
		for (int y = yardTop - YardHeadroom; y < Floor; y++)
		{
			for (int x = Left - ApronWidth; x <= Right + ApronWidth; x++)
			{
				if (!World.TileQuery.IsSolid(x, y) || Terraria.Main.tileFrameImportant[Terraria.Main.tile[x, y].TileType] || shell.Contains(new Point(x, y)))
					continue;
				if (!structures.Any(structure => x >= structure.Left && x <= structure.Right && y >= structure.Top && y <= structure.Floor))
					yield return new Point(x, y);
			}
		}
	}

	void CloseMisalignedDoors()
	{
		foreach (var room in structures)
		{
			foreach (var other in structures.Where(other => other != room && Yields(room, other)))
			{
				foreach (int x in new[] { room.Left, room.Right }.Where(x => room.HasDoorAt(x) && (x == other.Left || x == other.Right) && Clashes(room, other, x)))
					room.CloseSide(x, (cx, cy) => structures.Any(keeper => keeper != room && keeper.IsDoorway(cx, cy)));
			}
		}
		shell.Clear();
		foreach (var tile in structures.SelectMany(structure => structure.ShellTiles))
			shell.Add(tile);
	}

	static bool Yields(HouseBlueprint room, HouseBlueprint other)
	{
		bool roomStorage = room.Purpose == StructurePurpose.Storage, otherStorage = other.Purpose == StructurePurpose.Storage;
		return roomStorage != otherStorage ? roomStorage : room.Index > other.Index;
	}

	static bool Clashes(HouseBlueprint room, HouseBlueprint other, int x)
	{
		for (int y = Math.Min(room.Top, other.Top); y <= Math.Max(room.Floor, other.Floor); y++)
		{
			bool roomDoor = room.IsDoorway(x, y), otherDoor = other.IsDoorway(x, y);
			if (roomDoor != otherDoor && (roomDoor ? other.IsShell(x, y) : otherDoor && room.IsShell(x, y)))
				return true;
		}
		return false;
	}

	const int BalconyWidth = 5;

	void AddBalconies(int groundFloor)
	{
		foreach (var room in structures.Where(structure => structure.Floor != groundFloor).ToList())
		{
			foreach (int side in new[] { -1, 1 })
			{
				int door = side < 0 ? room.Left : room.Right;
				if (structures.Any(other => other != room && other.IsInterior(door + side, room.Floor - 2)))
					continue;
				for (int step = 1; step <= BalconyWidth; step++)
				{
					int x = door + side * step;
					if (shell.Contains(new Point(x, room.Floor)) || structures.Any(other => x >= other.Left && x <= other.Right && room.Floor >= other.Top && room.Floor <= other.Floor))
						continue;
					room.AddFloorPlatforms(x, x);
					shell.Add(new Point(x, room.Floor));
				}
			}
		}
	}

	void AddApron(HouseBlueprint edge, int fromX, int toX)
	{
		edge.AddFloorPlatforms(fromX, toX);
		for (int x = fromX; x <= toX; x++)
			shell.Add(new Point(x, Floor));
	}

	HouseBlueprint Add(StructurePurpose purpose, int left, int width, int floor, int interiorHeight, RoofStyle roof)
	{
		var blueprint = new HouseBlueprint(structures.Count, purpose, left, width, floor, interiorHeight, roof);
		structures.Add(blueprint);
		for (int x = blueprint.Left; x <= blueprint.Right; x++)
		{
			for (int y = blueprint.Top; y <= blueprint.Floor; y++)
			{
				if (blueprint.IsShell(x, y))
					shell.Add(new Point(x, y));
			}
		}
		return blueprint;
	}
}
