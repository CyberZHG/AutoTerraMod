using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.World;

public static class Grounds
{
	const int DungeonRange = 120;
	const int SurfaceBand = 60;
	const int UnderworldDepth = 20;

	public static bool IsInside(FactionKnowledge knowledge, Landmark ground, Point tile)
	{
		switch (ground)
		{
			case Landmark.Underworld:
				return tile.Y >= Main.UnderworldLayer;
			case Landmark.Dungeon:
				return knowledge.LandmarkOf(ground) is Point entrance && Vector2.Distance(entrance.ToVector2(), tile.ToVector2()) < DungeonRange;
			default:
				return knowledge.SpanOf(ground) is var (minX, maxX) && tile.X >= minX && tile.X <= maxX
					&& System.Math.Abs(tile.Y - TileQuery.SurfaceY(tile.X)) < SurfaceBand;
		}
	}

	const int SurfaceSpotInset = 40;

	public static Vector2? SurfaceSpotNear(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (knowledge.SpanOf(ground) is not var (minX, maxX))
			return null;
		int inset = System.Math.Min(SurfaceSpotInset, (maxX - minX) / 2);
		int x = Utils.Clamp(from.X, minX + inset, maxX - inset);
		return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
	}

	public static Vector2? PickSpot(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (knowledge.LandmarkOf(ground) is not Point anchor || knowledge.SpanOf(ground) is not var (minX, maxX))
			return null;
		switch (ground)
		{
			case Landmark.Underworld:
				return new Vector2(from.X * 16f, (Main.UnderworldLayer + UnderworldDepth) * 16f);
			case Landmark.Dungeon:
				return new Vector2((anchor.X + Main.rand.Next(-60, 61)) * 16f, (anchor.Y + Main.rand.Next(0, DungeonRange)) * 16f);
			default:
				int x = Main.rand.Next(minX, maxX + 1);
				return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
		}
	}
}
