using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class TempleDoor
{
	const int SearchWidth = 300;
	const int SearchHeight = 250;
	const int MouthSearch = 200;
	const int LihzahrdDoorTop = 594;
	const int UnlockedDoorBottom = 700;
	const uint CacheTicks = 600;
	const int CacheCell = 100;

	static readonly Dictionary<Point, (uint at, Point? door)> cache = new();

	public static void Reset() => cache.Clear();

	public static Point? Near(Point tile)
	{
		var key = new Point(tile.X / CacheCell, tile.Y / CacheCell);
		if (cache.TryGetValue(key, out var cached) && Main.GameUpdateCount - cached.at < CacheTicks)
			return cached.door;
		var door = Scan(tile);
		cache[key] = (Main.GameUpdateCount, door);
		return door;
	}

	static Point? Scan(Point tile)
	{
		for (int x = tile.X - SearchWidth; x <= tile.X + SearchWidth; x++)
		{
			for (int y = tile.Y - SearchHeight; y <= tile.Y + SearchHeight; y++)
			{
				if (IsLihzahrdDoor(x, y) && !IsLihzahrdDoor(x, y - 1))
					return new Point(x, y);
			}
		}
		return null;
	}

	static bool IsLihzahrdDoor(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 10))
			return false;
		var tile = Main.tile[x, y];
		return tile.HasTile && tile.TileType is TileID.ClosedDoor or TileID.OpenDoor && tile.WallType == WallID.LihzahrdBrickUnsafe
			&& tile.TileFrameY >= LihzahrdDoorTop && tile.TileFrameY <= UnlockedDoorBottom;
	}

	public static Point? Mouth(Point door)
	{
		int row = door.Y + 1;
		Point? mouth = null;
		foreach (int direction in new[] { -1, 1 })
		{
			for (int offset = 1; offset < MouthSearch; offset++)
			{
				int x = door.X + direction * offset;
				if (!WorldGen.InWorld(x, row, 10))
					break;
				bool maze = Main.tile[x, row].WallType == WallID.LihzahrdBrickUnsafe;
				if (maze && TileQuery.IsSolid(x, row) && !IsLihzahrdDoor(x, row))
					break;
				if (maze)
					continue;
				if (mouth is not Point best || offset < Math.Abs(best.X - door.X))
					mouth = new Point(x, row);
				break;
			}
		}
		return mouth;
	}
}
