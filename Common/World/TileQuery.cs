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

	public static bool IsPlatform(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.HasTile && !tile.IsActuated && Main.tileSolidTop[tile.TileType];
	}

	public static bool CanStandOn(int x, int y) => IsSolid(x, y) || IsPlatform(x, y);

	public static bool HasTile(Point tile) => WorldGen.InWorld(tile.X, tile.Y, 2) && Main.tile[tile.X, tile.Y].HasTile;

	public static int TypeAt(Point tile) => HasTile(tile) ? Main.tile[tile.X, tile.Y].TileType : -1;

	public static bool IsExposed(int x, int y) =>
		!IsSolid(x - 1, y) || !IsSolid(x + 1, y) || !IsSolid(x, y - 1) || !IsSolid(x, y + 1);

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
		Main.tileAxe[tileType] && TileID.Sets.IsATreeTrunk[tileType];

	public static bool IsDangerousLiquid(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava;
	}

	public static int SurfaceY(int x)
	{
		int top = (int)(Main.worldSurface * 0.35);
		for (int y = top; y < Main.maxTilesY - 200; y++)
		{
			if (IsSolid(x, y) || Main.tile[x, y].LiquidAmount > 0)
				return y;
		}
		return (int)Main.worldSurface;
	}
}
