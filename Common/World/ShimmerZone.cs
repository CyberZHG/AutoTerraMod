using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class ShimmerZone
{
	const int Margin = 12;

	static Rectangle? zone;
	static bool scanned;

	public static void Reset()
	{
		zone = null;
		scanned = false;
	}

	public static bool Contains(int x, int y)
	{
		if (!scanned)
			Scan();
		return zone is Rectangle area && area.Contains(x, y);
	}

	const int PoolReach = 150;

	static void Scan()
	{
		scanned = true;
		var cells = new System.Collections.Generic.List<Point>();
		for (int x = 0; x < Main.maxTilesX; x++)
		{
			for (int y = 0; y < Main.maxTilesY; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer || tile.HasTile && tile.TileType == TileID.ShimmerBlock)
					cells.Add(new Point(x, y));
			}
		}
		if (cells.Count == 0)
			return;
		var xs = cells.ConvertAll(cell => cell.X);
		var ys = cells.ConvertAll(cell => cell.Y);
		xs.Sort();
		ys.Sort();
		var center = new Point(xs[xs.Count / 2], ys[ys.Count / 2]);
		int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
		foreach (var cell in cells)
		{
			if (Math.Abs(cell.X - center.X) > PoolReach || Math.Abs(cell.Y - center.Y) > PoolReach)
				continue;
			left = Math.Min(left, cell.X);
			right = Math.Max(right, cell.X);
			top = Math.Min(top, cell.Y);
			bottom = Math.Max(bottom, cell.Y);
		}
		zone = new Rectangle(left - Margin, top - Margin, right - left + 1 + Margin * 2, bottom - top + 1 + Margin * 2);
	}
}
