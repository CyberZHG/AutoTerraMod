using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class TerrainDump
{
	const int CaptureHalfWidth = 24;
	const int CaptureHalfHeight = 14;

	public static string Capture(Point feet, Vector2? destination)
	{
		var rows = new StringBuilder();
		for (int y = feet.Y - CaptureHalfHeight; y <= feet.Y + CaptureHalfHeight; y++)
		{
			if (rows.Length > 0)
				rows.Append(';');
			for (int x = feet.X - CaptureHalfWidth; x <= feet.X + CaptureHalfWidth; x++)
				rows.Append(CaptureGlyph(Glyph(x, y)));
		}
		var dest = destination?.ToTileCoordinates() ?? feet;
		return $"navcapture origin={feet.X - CaptureHalfWidth},{feet.Y - CaptureHalfHeight} feet={feet.X},{feet.Y} dest={dest.X},{dest.Y} rows={rows}";
	}

	static char CaptureGlyph(char glyph) => glyph switch
	{
		'o' => '#',
		'|' or 'f' or 'D' or 'X' or '^' => ' ',
		_ => glyph
	};

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
			if (TileID.Sets.TouchDamageImmediate[type] > 0)
				return '^';
			if (Main.tileFrameImportant[type])
				return 'f';
		}
		if (tile.LiquidAmount > 0)
			return tile.LiquidType == LiquidID.Lava ? '%' : '~';
		return tile.WallType != 0 ? '.' : ' ';
	}
}
