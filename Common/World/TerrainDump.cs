using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class TerrainDump
{
	public static string Around(Point center, int halfWidth = 30, int halfHeight = 15)
	{
		var map = new StringBuilder();
		for (int y = center.Y - halfHeight; y <= center.Y + halfHeight; y++)
		{
			map.Append('\n').Append(y.ToString().PadLeft(5)).Append(' ');
			for (int x = center.X - halfWidth; x <= center.X + halfWidth; x++)
				map.Append(x == center.X && y == center.Y ? '@' : Glyph(x, y));
		}
		return map.ToString();
	}

	static char Glyph(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return 'X';
		var tile = Main.tile[x, y];
		if (tile.HasTile)
		{
			int type = tile.TileType;
			if (type == TileID.ClosedDoor || type == TileID.OpenDoor)
				return 'D';
			if (TileQuery.IsChoppableTree(type))
				return '|';
			if (Main.tileRope[type])
				return '!';
			if (Main.tileSolidTop[type])
				return '=';
			if (Main.tileSolid[type])
				return TileID.Sets.Ore[type] ? 'o' : '#';
			if (Main.tileFrameImportant[type])
				return 'f';
		}
		if (tile.LiquidAmount > 0)
			return tile.LiquidType == LiquidID.Lava ? '%' : '~';
		return tile.WallType != 0 ? '.' : ' ';
	}
}
