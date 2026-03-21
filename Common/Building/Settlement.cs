using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace AutoTerraMod.Common.Building;

public sealed class Settlement
{
	const int StorageWidth = 16;
	const int WorkshopWidth = 24;
	const int RoomWidth = 10;
	const int ApronWidth = 5;
	const int HallHatchInset = 2;
	const int HallHatchWidth = 2;
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
		Left = structures.Min(structure => structure.Left);
		Right = structures.Max(structure => structure.Right);
		Top = structures.Min(structure => structure.Top);
		Floor = floor;
		AddApron(structures.Where(structure => structure.Floor == floor).MinBy(structure => structure.Left), Left - ApronWidth, Left - 1);
		AddApron(structures.Where(structure => structure.Floor == floor).MaxBy(structure => structure.Right), Right + 1, Right + ApronWidth);
		Hall.AddFloorPlatforms(Hall.Left + HallHatchInset, Hall.Left + HallHatchInset + HallHatchWidth - 1);
		structures[^1].Yard = YardTiles;
	}

	public HouseBlueprint Hall { get; }
	public HouseBlueprint Storage { get; }
	public HouseBlueprint Workshop { get; }
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

	IEnumerable<Point> YardTiles()
	{
		for (int y = Top - YardHeadroom; y < Floor; y++)
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
