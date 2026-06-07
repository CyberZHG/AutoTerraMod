using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class MushroomPatch
{
	const int Cell = 30;
	const int MinGrass = 120;
	const int Headroom = 3;

	static List<(Point stand, int grass)> patches;

	public static void Reset() => patches = null;

	public static Vector2? Nearest(Point from)
	{
		patches ??= Scan();
		Point? best = null;
		float bestScore = float.MaxValue;
		foreach (var (stand, grass) in patches)
		{
			float score = Vector2.Distance(stand.ToVector2(), from.ToVector2()) - grass * 0.2f;
			if (score < bestScore)
			{
				bestScore = score;
				best = stand;
			}
		}
		return best is Point spot ? new Vector2(spot.X * 16f + 8f, (spot.Y - 1) * 16f) : null;
	}

	static List<(Point, int)> Scan()
	{
		var found = new List<(Point, int)>();
		int top = (int)Main.worldSurface;
		for (int cellX = 50; cellX + Cell < Main.maxTilesX - 50; cellX += Cell)
		{
			for (int cellY = top; cellY + Cell < Main.UnderworldLayer; cellY += Cell)
			{
				int grass = 0;
				Point? stand = null;
				for (int x = cellX; x < cellX + Cell; x++)
				{
					for (int y = cellY; y < cellY + Cell; y++)
					{
						var tile = Main.tile[x, y];
						if (!tile.HasTile || tile.TileType != TileID.MushroomGrass)
							continue;
						grass++;
						if (stand == null && Open(x, y - 1))
							stand = new Point(x, y);
					}
				}
				if (grass >= MinGrass && stand is Point floor)
					found.Add((floor, grass));
			}
		}
		return found;
	}

	static bool Open(int x, int y)
	{
		for (int dy = 0; dy < Headroom; dy++)
		{
			for (int dx = 0; dx <= 1; dx++)
			{
				if (TileQuery.IsSolid(x + dx, y - dy) || Main.tile[x + dx, y - dy].LiquidAmount > 0)
					return false;
			}
		}
		return true;
	}
}
