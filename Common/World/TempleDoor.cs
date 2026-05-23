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

	public static void Reset()
	{
		cache.Clear();
		guide = null;
	}

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

	const int GuideReach = 300;
	const uint GuideTicks = 3600;
	const int GuideSnap = 4;
	const int MaxDrop = 12;

	sealed record Guide(Point Goal, uint At, int Left, int Top, int Width, int Height, int[] Steps);

	static Guide guide;

	public static Point? GuideStep(Point goal, Point here, int hop)
	{
		if (guide == null || guide.Goal != goal || Main.GameUpdateCount - guide.At > GuideTicks)
			guide = Flood(goal);
		if (Nearest(guide, here) is not Point start)
			return null;
		var at = start;
		for (int step = 0; step < hop; step++)
		{
			int current = StepsAt(guide, at.X, at.Y);
			if (current <= 0)
				break;
			Point? next = null;
			foreach (var (dx, dy) in Neighbors)
			{
				if (StepsAt(guide, at.X + dx, at.Y + dy) is int value && value >= 0 && value < current)
				{
					next = new Point(at.X + dx, at.Y + dy);
					break;
				}
			}
			if (next is not Point moved)
				break;
			at = moved;
		}
		for (int drop = 0; drop < MaxDrop && !TileQuery.CanStandOn(at.X, at.Y + 1) && Open(at.X, at.Y + 1); drop++)
			at.Y++;
		return at;
	}

	static readonly (int dx, int dy)[] Neighbors = { (0, 1), (-1, 0), (1, 0), (0, -1) };

	static int StepsAt(Guide field, int x, int y)
	{
		int cx = x - field.Left, cy = y - field.Top;
		return cx < 0 || cy < 0 || cx >= field.Width || cy >= field.Height ? -1 : field.Steps[cx + cy * field.Width];
	}

	static Point? Nearest(Guide field, Point here)
	{
		for (int radius = 0; radius <= GuideSnap; radius++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					if (StepsAt(field, here.X + dx, here.Y + dy) >= 0)
						return new Point(here.X + dx, here.Y + dy);
				}
			}
		}
		return null;
	}

	static Guide Flood(Point goal)
	{
		int left = Math.Max(10, goal.X - GuideReach), top = Math.Max(10, goal.Y - GuideReach);
		int right = Math.Min(Main.maxTilesX - 10, goal.X + GuideReach), bottom = Math.Min(Main.maxTilesY - 10, goal.Y + GuideReach);
		int width = right - left + 1, height = bottom - top + 1;
		var steps = new int[width * height];
		Array.Fill(steps, -1);
		var queue = new Queue<Point>();
		if (Open(goal.X, goal.Y))
		{
			steps[goal.X - left + (goal.Y - top) * width] = 0;
			queue.Enqueue(goal);
		}
		else
		{
			for (int dy = -GuideSnap; dy <= GuideSnap && queue.Count == 0; dy++)
			{
				for (int dx = -GuideSnap; dx <= GuideSnap; dx++)
				{
					if (Open(goal.X + dx, goal.Y + dy))
					{
						steps[goal.X + dx - left + (goal.Y + dy - top) * width] = 0;
						queue.Enqueue(new Point(goal.X + dx, goal.Y + dy));
					}
				}
			}
		}
		while (queue.Count > 0)
		{
			var cell = queue.Dequeue();
			int value = steps[cell.X - left + (cell.Y - top) * width];
			foreach (var (dx, dy) in Neighbors)
			{
				int x = cell.X + dx, y = cell.Y + dy;
				if (x < left || x > right || y < top || y > bottom)
					continue;
				int index = x - left + (y - top) * width;
				if (steps[index] >= 0 || !Open(x, y))
					continue;
				steps[index] = value + 1;
				queue.Enqueue(new Point(x, y));
			}
		}
		return new Guide(goal, Main.GameUpdateCount, left, top, width, height, steps);
	}

	static bool Open(int x, int y)
	{
		if (Main.tile[x, y].WallType != WallID.LihzahrdBrickUnsafe)
			return false;
		for (int row = y - 2; row <= y; row++)
		{
			if (TileQuery.IsSolid(x, row) && !IsLihzahrdDoor(x, row))
				return false;
		}
		return true;
	}
}
