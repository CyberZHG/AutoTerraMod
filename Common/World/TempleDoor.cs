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
		guides.Clear();
		entrances.Clear();
		knownDoor = null;
	}

	public static Point? Near(Point tile)
	{
		var key = new Point(tile.X / CacheCell, tile.Y / CacheCell);
		if (cache.TryGetValue(key, out var cached) && Main.GameUpdateCount - cached.at < CacheTicks)
			return cached.door;
		if (knownDoor is Point remembered && IsLihzahrdDoor(remembered.X, remembered.Y) && !IsLihzahrdDoor(remembered.X, remembered.Y - 1))
			return Math.Abs(remembered.X - tile.X) <= KnownDoorReach && Math.Abs(remembered.Y - tile.Y) <= KnownDoorReach ? remembered : null;
		var door = Scan(tile) ?? (Navigation.PathFinder.InsideMaze(tile) ? DeepScan(tile) : null);
		if (door is Point found)
			knownDoor = found;
		cache[key] = (Main.GameUpdateCount, door);
		return door;
	}

	static Point? knownDoor;
	const int KnownDoorReach = 1000;

	const int DeepScanRise = 600;

	static Point? DeepScan(Point tile)
	{
		int width = (int)(SearchWidth * 2 * Planning.ProgressionPlanner.WorldScale);
		for (int y = Math.Max(10, tile.Y - DeepScanRise); y <= tile.Y; y++)
		{
			for (int x = tile.X - width; x <= tile.X + width; x++)
			{
				if (IsLihzahrdDoor(x, y) && !IsLihzahrdDoor(x, y - 1))
					return new Point(x, y);
			}
		}
		return null;
	}

	static Point? Scan(Point tile)
	{
		float scale = Planning.ProgressionPlanner.WorldScale;
		int width = (int)(SearchWidth * scale), height = (int)(SearchHeight * scale);
		for (int x = tile.X - width; x <= tile.X + width; x++)
		{
			for (int y = tile.Y - height; y <= tile.Y + height; y++)
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

	const int EntranceSearchCells = 40000;
	const int DoorHeight = 3;

	sealed record Entrance(Point Mouth, int Side, HashSet<Point> Tunnel);

	static readonly Dictionary<Point, Entrance> entrances = new();

	public static Point? Mouth(Point door) => EntranceOf(door)?.Mouth;

	public static int? OutsideSide(Point door) => EntranceOf(door)?.Side;

	public static Point InsideStep(Point door, int side, int stepInside) => new(door.X - side * stepInside, door.Y + 1);

	public static bool InTunnel(Point door, Point here) =>
		EntranceOf(door) is { } entrance && (entrance.Tunnel.Contains(here) || entrance.Tunnel.Contains(new Point(here.X, here.Y + 1)) || entrance.Tunnel.Contains(new Point(here.X, here.Y - 1)));

	static Entrance EntranceOf(Point door)
	{
		if (entrances.TryGetValue(door, out var known))
			return known;
		Entrance found = null;
		foreach (int side in new[] { -1, 1 })
		{
			if (FloodToOutside(door, side) is { } entrance)
			{
				found = entrance;
				break;
			}
		}
		entrances[door] = found;
		return found;
	}

	static Entrance FloodToOutside(Point door, int side)
	{
		var start = new Point(door.X + side, door.Y + DoorHeight - 1);
		if (TileQuery.IsSolid(start.X, start.Y))
			return null;
		var seen = new HashSet<Point> { start };
		var queue = new Queue<Point>();
		queue.Enqueue(start);
		while (queue.Count > 0 && seen.Count < EntranceSearchCells)
		{
			var tile = queue.Dequeue();
			if (Main.tile[tile.X, tile.Y].WallType != WallID.LihzahrdBrickUnsafe)
				return new Entrance(tile, side, seen);
			foreach (var (dx, dy) in Neighbors)
			{
				var next = new Point(tile.X + dx, tile.Y + dy);
				if (!WorldGen.InWorld(next.X, next.Y, 10) || next.X == door.X && next.Y >= door.Y && next.Y < door.Y + DoorHeight || TileQuery.IsSolid(next.X, next.Y) || !seen.Add(next))
					continue;
				queue.Enqueue(next);
			}
		}
		return null;
	}

	const int GuideReach = 300;
	const uint GuideTicks = 3600;
	const int GuideSnap = 4;
	const int MaxDrop = 12;

	sealed record Guide(Point Goal, uint At, int Left, int Top, int Width, int Height, int[] Steps);

	static readonly Dictionary<Point, Guide> guides = new();
	const int MaxGuides = 6;

	public static Point? GuideStep(Point goal, Point here, int hop)
	{
		if (!guides.TryGetValue(goal, out var guide) || Main.GameUpdateCount - guide.At > GuideTicks)
		{
			if (guides.Count >= MaxGuides)
				guides.Clear();
			guides[goal] = guide = Flood(goal);
		}
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

	const int HuntSpotTries = 600;
	const int ShallowSteps = 40;
	const int DeepSteps = 450;
	const int NearAltarSteps = 12;
	const int AltarRouteSteps = 220;

	public static Point? HuntSpot(Point temple, Point? altar)
	{
		if ((Near(temple) ?? (altar is Point deep ? Near(deep) : null)) is not Point door || OutsideSide(door) is not int side)
			return null;
		var origin = altar ?? InsideStep(door, side, 2);
		int fewest = altar == null ? ShallowSteps : NearAltarSteps;
		int most = altar == null ? DeepSteps : AltarRouteSteps;
		if (!guides.TryGetValue(origin, out var field) || Main.GameUpdateCount - field.At > GuideTicks)
		{
			if (guides.Count >= MaxGuides)
				guides.Clear();
			guides[origin] = field = Flood(origin);
		}
		for (int attempt = 0; attempt < HuntSpotTries; attempt++)
		{
			int index = Main.rand.Next(field.Steps.Length);
			int steps = field.Steps[index];
			if (steps < fewest || steps > most)
				continue;
			int x = field.Left + index % field.Width, y = field.Top + index / field.Width;
			if (TileQuery.CanStandOn(x, y + 1))
				return new Point(x, y);
		}
		return null;
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
			if (TileQuery.IsSolid(x, row) && !IsLihzahrdDoor(x, row) && !(NPC.downedPlantBoss && Main.tile[x, row].TileType == TileID.WoodenSpikes))
				return false;
		}
		return true;
	}
}
