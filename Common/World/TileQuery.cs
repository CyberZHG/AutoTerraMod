using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class TileQuery
{
	public static bool IsSolid(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return true;
		var tile = Main.tile[x, y];
		return tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
	}

	public static bool IsSolid(Point tile) => IsSolid(tile.X, tile.Y);

	public const int OceanWidth = 380;

	public static bool InOcean(int x, int y) => (x < OceanWidth || x > Main.maxTilesX - OceanWidth) && y < Main.rockLayer;

	public static bool ClearLine(Vector2 from, Vector2 to)
	{
		var delta = to - from;
		int steps = (int)(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) / 8f) + 1;
		var step = delta / steps;
		for (int i = 1; i < steps; i++)
		{
			var point = from + step * i;
			if (IsSolid((int)(point.X / 16f), (int)(point.Y / 16f)))
				return false;
		}
		return true;
	}

	public static bool IsPlatform(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.HasTile && !tile.IsActuated && Main.tileSolidTop[tile.TileType];
	}

	public static bool SupportsBoulder(int x, int y)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -2; dy <= -1; dy++)
			{
				if (WorldGen.InWorld(x + dx, y + dy, 2) && Main.tile[x + dx, y + dy].HasTile && Main.tile[x + dx, y + dy].TileType is TileID.Boulder or TileID.RollingCactus)
					return true;
			}
		}
		return false;
	}

	public static bool SupportsShrine(int x, int y)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -1; dy <= 1; dy++)
			{
				if (WorldGen.InWorld(x + dx, y + dy, 2) && Main.tile[x + dx, y + dy].HasTile && WorldScan.IsBossShrine(Main.tile[x + dx, y + dy].TileType))
					return true;
			}
		}
		return false;
	}

	public static bool FreeForPlacing(int x, int y)
	{
		var tile = Main.tile[x, y];
		return !tile.HasTile || Main.tileCut[tile.TileType];
	}

	public static bool CanStandOn(int x, int y) => IsSolid(x, y) || IsPlatform(x, y);

	public static bool HasTile(Point tile) => WorldGen.InWorld(tile.X, tile.Y, 2) && Main.tile[tile.X, tile.Y].HasTile;

	public static int TypeAt(Point tile) => HasTile(tile) ? Main.tile[tile.X, tile.Y].TileType : -1;

	public static bool HoldsCactus(int x, int y) => Main.tile[x, y].TileType != TileID.Cactus && Main.tile[x, y - 1] is { HasTile: true, TileType: TileID.Cactus };

	public static bool HoldsTree(int x, int y) => Main.tile[x, y - 1].HasTile && IsChoppableTree(Main.tile[x, y - 1].TileType) && Main.tile[x, y].TileType != Main.tile[x, y - 1].TileType;

	public static bool HoldsChest(int x, int y) => Main.tile[x, y - 1].HasTile && TileID.Sets.BasicChest[Main.tile[x, y - 1].TileType] && !TileID.Sets.BasicChest[Main.tile[x, y].TileType];

	public static bool HoldsRemovableTop(int x, int y) => HoldsCactus(x, y) || HoldsTree(x, y) || HoldsChest(x, y);

	public static bool IsExposed(int x, int y) =>
		!IsSolid(x - 1, y) || !IsSolid(x + 1, y) || !IsSolid(x, y - 1) || !IsSolid(x, y + 1);

	public static Point TreeBaseOf(Point tile)
	{
		int type = Main.tile[tile.X, tile.Y].TileType;
		foreach (int x in new[] { tile.X, tile.X - 1, tile.X + 1 })
		{
			int y = tile.Y;
			while (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type && Main.tile[x, y + 1].HasTile && Main.tile[x, y + 1].TileType == type)
				y++;
			if (IsTreeBase(x, y))
				return new Point(x, y);
		}
		return tile;
	}

	public static bool IsTreeBase(int x, int y)
	{
		var tile = Main.tile[x, y];
		if (!tile.HasTile || !IsChoppableTree(tile.TileType))
			return false;
		var below = Main.tile[x, y + 1];
		var above = Main.tile[x, y - 1];
		bool trunkContinuesUp = above.HasTile && above.TileType == tile.TileType;
		return trunkContinuesUp && (!below.HasTile || below.TileType != tile.TileType);
	}

	public static bool IsChoppableTree(int tileType) =>
		Main.tileAxe[tileType] && (TileID.Sets.IsATreeTrunk[tileType] || tileType is TileID.TreeAsh or TileID.PalmTree);

	public static bool IsExposedLivingWood(int x, int y) =>
		Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.LivingWood && IsExposed(x, y) && !Building.HouseInspector.IsProtected(x, y);

	public static bool IsWoodSource(int x, int y) => IsTreeBase(x, y) || IsExposedLivingWood(x, y);

	public static bool IsDangerousLiquid(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava && !IsSolid(x, y);
	}

	public static bool TouchesLava(int x, int y) =>
		IsDangerousLiquid(x - 1, y) || IsDangerousLiquid(x + 1, y) || IsDangerousLiquid(x, y - 1) || IsDangerousLiquid(x, y + 1);

	public static int SurfaceY(int x)
	{
		int top = (int)(Main.worldSurface * 0.35);
		for (int y = top; y < Main.maxTilesY - 200; y++)
		{
			if (!IsSolid(x, y) && Main.tile[x, y].LiquidAmount == 0)
				continue;
			if (y >= Main.worldSurface * 0.6 || SkyBelow(x, y) is not int skyEnd)
				return y;
			y = skyEnd;
		}
		return (int)Main.worldSurface;
	}

	const int IslandMaxThickness = 30;
	const int SkyGapRows = 8;

	static int? SkyBelow(int x, int y)
	{
		int gap = 0;
		for (int below = y + 1; below <= y + IslandMaxThickness + SkyGapRows; below++)
		{
			var tile = Main.tile[x, below];
			bool open = !IsSolid(x, below) && tile.LiquidAmount == 0 && tile.WallType == 0;
			gap = open ? gap + 1 : 0;
			if (gap >= SkyGapRows)
				return below;
		}
		return null;
	}
}
