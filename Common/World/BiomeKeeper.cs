using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class BiomeKeeper
{
	const int Search = 12;
	const int Reach = 3;
	const int EvilMargin = 12;
	const int EvilRows = 20;
	const int RopeMargin = 3;
	const int CampMargin = 10;
	const int HighwayAbove = 5;

	public static bool Protects(int x, int y)
	{
		int type = TileQuery.TypeAt(new Point(x, y));
		if (type < 0 || Main.tileSpelunker[type] || type == TileID.Cobweb)
			return false;
		return y < Main.worldSurface || Near(x, y, 1, IsMushroomGrass);
	}

	static bool IsMushroomGrass(Tile tile) => tile.HasTile && tile.TileType == TileID.MushroomGrass;

	static bool IsHallow(Tile tile) => tile.HasTile && TileID.Sets.Hallow[tile.TileType];

	static bool IsEvil(Tile tile) => tile.HasTile && Building.PlacementRules.IsEvil(tile.TileType);

	static bool IsRope(Tile tile) => tile.HasTile && Main.tileRope[tile.TileType];

	static bool Near(int x, int y, int radius, System.Func<Tile, bool> test) => Near(x, y, radius, radius, test);

	static bool Near(int x, int y, int dxMax, int dyMax, System.Func<Tile, bool> test)
	{
		for (int dx = -dxMax; dx <= dxMax; dx++)
			for (int dy = -dyMax; dy <= dyMax; dy++)
				if (WorldGen.InWorld(x + dx, y + dy, 1) && test(Main.tile[x + dx, y + dy]))
					return true;
		return false;
	}

	public static Point? HoleNear(Point from, bool hallow)
	{
		Point? best = null;
		int bestDistance = int.MaxValue;
		for (int dx = -Search; dx <= Search; dx++)
		{
			for (int dy = -Search; dy <= Search; dy++)
			{
				int x = from.X + dx, y = from.Y + dy, distance = System.Math.Abs(dx) + System.Math.Abs(dy);
				if (distance >= bestDistance || !(hallow ? IsHallowHole(x, y) : IsMushroomHole(x, y)))
					continue;
				best = new Point(x, y);
				bestDistance = distance;
			}
		}
		return best;
	}

	static bool IsOpen(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 2))
			return false;
		var tile = Main.tile[x, y];
		return !tile.HasTile && tile.LiquidAmount == 0 && !Building.HouseInspector.IsProtected(x, y) && !InCamp(x, y);
	}

	static bool IsHallowHole(int x, int y) =>
		y < Main.worldSurface && IsOpen(x, y) && Main.tile[x, y].WallType != WallID.None
		&& (TileQuery.IsSolid(x, y + 1) || TileQuery.IsSolid(x - 1, y) || TileQuery.IsSolid(x + 1, y))
		&& Near(x, y, Reach, IsHallow) && !Near(x, y, EvilMargin, EvilRows, IsEvil) && !Near(x, y, RopeMargin, EvilRows, IsRope);

	static bool IsMushroomHole(int x, int y) =>
		IsOpen(x, y) && (TileQuery.IsSolid(x - 1, y) && TileQuery.IsSolid(x + 1, y) || TileQuery.IsSolid(x, y - 1) && TileQuery.IsSolid(x, y + 1))
		&& Near(x, y, 2, IsMushroomGrass) && !Near(x, y, RopeMargin, EvilRows, IsRope);

	static bool InCamp(int x, int y)
	{
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (faction.Settlement is { } camp && x >= camp.Left - CampMargin && x <= camp.Right + CampMargin && y >= camp.Top - CampMargin && y <= camp.Floor + CampMargin)
				return true;
			if (Highway.For(faction).Any(highway => highway.Spans(x) && y >= highway.Y - HighwayAbove && y <= highway.Y + 1))
				return true;
		}
		return false;
	}
}
