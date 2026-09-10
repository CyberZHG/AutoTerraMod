using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.World;

public sealed record TempleShaftPlan(int X, int TopY, int BottomY)
{
	public const int Width = 3;

	public int Right => X + Width - 1;
	public int RopeX => X + 1;

	public bool Contains(Point tile) => tile.X >= X && tile.X <= Right && tile.Y >= TopY - 1 && tile.Y <= BottomY + 1;

	public Point? NextSolid()
	{
		for (int y = TopY + 1; y <= BottomY; y++)
		{
			for (int x = X; x <= Right; x++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile || Main.tileRope[tile.TileType])
					continue;
				return new Point(x, y);
			}
		}
		return null;
	}

	public Point? NextCap()
	{
		for (int x = X; x <= Right; x++)
		{
			if (!TileQuery.IsPlatform(x, TopY))
				return new Point(x, TopY);
		}
		return null;
	}

	public bool Dug => NextSolid() == null;
	public bool Capped => NextCap() == null;
	public bool IsComplete => Dug && Capped;

	public Point SideStand => !TileQuery.IsSolid(X - 2, TopY - 1) && TileQuery.IsSolid(X - 2, TopY) ? new Point(X - 2, TopY - 1) : new Point(Right + 2, TopY - 1);

	public Vector2 Bottom => new Point(RopeX, BottomY).ToWorldCoordinates(8f, 0f);
	public Vector2 Top => new Point(RopeX, TopY - 1).ToWorldCoordinates(8f, 0f);
}

public sealed class TempleShaft : ModSystem
{
	const int SideGap = 1;
	const int MaxDepth = 160;
	const int ExitRows = 3;
	const int TempleProbe = 2;

	public static TempleShaftPlan Current { get; private set; }
	public static int RopeTries { get; set; }

	public override void OnWorldLoad()
	{
		Current = null;
		RopeTries = 0;
	}

	public override void OnWorldUnload() => OnWorldLoad();

	public override void SaveWorldData(TagCompound tag)
	{
		if (Current == null)
			return;
		tag["templeShaftX"] = Current.X;
		tag["templeShaftTop"] = Current.TopY;
		tag["templeShaftBottom"] = Current.BottomY;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		if (tag.ContainsKey("templeShaftX"))
			Current = new TempleShaftPlan(tag.GetInt("templeShaftX"), tag.GetInt("templeShaftTop"), tag.GetInt("templeShaftBottom"));
	}

	public static TempleShaftPlan PlanFrom(Point altar)
	{
		if (Current != null && !Untouched(Current))
			return Current;
		int floor = altar.Y + 2;
		foreach (int x in new[] { altar.X - SideGap - TempleShaftPlan.Width, altar.X + 3 + SideGap })
		{
			if (!FloorUnder(x, floor) || ExitRow(x, floor) is not int exit)
				continue;
			Current = new TempleShaftPlan(x, floor, OpenBelow(x, exit) ?? exit + ExitRows);
			return Current;
		}
		return null;
	}

	static bool Untouched(TempleShaftPlan shaft)
	{
		for (int x = shaft.X; x <= shaft.Right; x++)
		{
			if (!TileQuery.IsSolid(x, shaft.TopY) || TileQuery.IsPlatform(x, shaft.TopY))
				return false;
		}
		return true;
	}

	static bool FloorUnder(int x, int floor)
	{
		for (int column = x; column < x + TempleShaftPlan.Width; column++)
		{
			if (!WorldGen.InWorld(column, floor, 10) || !TileQuery.IsSolid(column, floor) || TileQuery.IsSolid(column, floor - 1) || TileQuery.IsSolid(column, floor - 2))
				return false;
		}
		return true;
	}

	static int? ExitRow(int x, int floor)
	{
		for (int y = floor + 1; y < floor + MaxDepth && y < Main.UnderworldLayer; y++)
		{
			bool outside = true;
			for (int column = x - TempleProbe; column < x + TempleShaftPlan.Width + TempleProbe && outside; column++)
				outside = !InTemple(column, y);
			if (outside)
				return y;
		}
		return null;
	}

	const int CaveSearch = 80;
	const int CaveRows = 3;

	static int? OpenBelow(int x, int exit)
	{
		int open = 0;
		for (int y = exit; y < exit + CaveSearch && y < Main.UnderworldLayer; y++)
		{
			bool clear = true;
			for (int column = x; column < x + TempleShaftPlan.Width && clear; column++)
				clear = !TileQuery.IsSolid(column, y) && Main.tile[column, y].LiquidAmount == 0;
			open = clear ? open + 1 : 0;
			if (open >= CaveRows)
				return y - CaveRows + 1;
		}
		return null;
	}

	static bool InTemple(int x, int y)
	{
		var tile = Main.tile[x, y];
		return tile.WallType == WallID.LihzahrdBrickUnsafe || tile.HasTile && tile.TileType == TileID.LihzahrdBrick;
	}

	public static bool Usable => Current is { IsComplete: true };
}
