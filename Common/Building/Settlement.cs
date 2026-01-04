using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace AutoTerraMod.Common.Building;

public sealed class Settlement
{
	const int StorageWidth = 16;
	const int RoomWidth = 10;
	const int RoomInteriorHeight = 5;
	const int MinimumStorageInteriorHeight = 4;
	public const int GuestRoomCount = 3;

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
		for (int i = 0; i < GuestRoomCount; i++)
		{
			var guestRoom = Add(StructurePurpose.GuestRoom, nextLeft, RoomWidth, floor, RoomInteriorHeight, design.Roof);
			GuestRooms.Add(guestRoom);
			nextLeft = guestRoom.Right;
		}
		Left = structures.Min(structure => structure.Left);
		Right = structures.Max(structure => structure.Right);
		Top = structures.Min(structure => structure.Top);
		Floor = floor;
	}

	public HouseBlueprint Hall { get; }
	public HouseBlueprint Storage { get; }
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
