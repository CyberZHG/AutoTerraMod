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
			case Landmark.Temple:
				return Main.tile[tile.X, tile.Y].WallType == Terraria.ID.WallID.LihzahrdBrickUnsafe;
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

	const int ArenaProbeDepth = 6;
	const int StandingSearchRadius = 20;

	public static Vector2? ArenaSpot(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (SurfaceSpotNear(knowledge, ground, from) is Vector2 surface && (ground != Landmark.Hallow || IsGroundBeneath(ground, surface.ToTileCoordinates())))
			return surface;
		if (knowledge.LandmarkOf(ground) is not Point anchor || OpenSpotNear(anchor, ground) is not Point feet)
			return null;
		return new Vector2(feet.X * 16f + 8f, feet.Y * 16f);
	}

	const int OpenSearchRadius = 150;
	const int OpenSearchStep = 4;
	const int OpennessHalfWidth = 20;
	const int OpennessHeight = 16;

	static readonly System.Collections.Generic.Dictionary<(Landmark, Point), Point?> openSpots = new();

	static Point? OpenSpotNear(Point anchor, Landmark ground)
	{
		if (openSpots.TryGetValue((ground, anchor), out var cached))
			return cached;
		Point? best = null;
		int bestScore = -1;
		for (int x = anchor.X - OpenSearchRadius; x <= anchor.X + OpenSearchRadius; x += OpenSearchStep)
		{
			for (int y = anchor.Y - OpenSearchRadius; y <= anchor.Y + OpenSearchRadius; y++)
			{
				if (!WorldGen.InWorld(x, y, OpennessHalfWidth + OpennessHeight + 2) || y >= Main.UnderworldLayer || !IsDryFooting(x, y) || !IsGroundBeneath(ground, new Point(x, y + 1)))
					continue;
				int score = Openness(x, y);
				if (score > bestScore)
				{
					bestScore = score;
					best = new Point(x, y);
				}
			}
		}
		best ??= StandingSpotNear(anchor);
		openSpots[(ground, anchor)] = best;
		Bots.BotDiagnostics.LogWorld($"arena for {ground} near {anchor}: {best} openness {bestScore}");
		return best;
	}

	static bool IsDryFooting(int x, int y) =>
		TileQuery.CanStandOn(x, y + 1) && !TileQuery.IsSolid(x, y) && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x, y - 2)
		&& Main.tile[x, y].LiquidAmount == 0 && Main.tile[x, y - 1].LiquidAmount == 0;

	static int Openness(int x, int y)
	{
		int open = 0;
		for (int dx = -OpennessHalfWidth; dx <= OpennessHalfWidth; dx++)
		{
			for (int dy = 1; dy <= OpennessHeight; dy++)
			{
				var tile = Main.tile[x + dx, y - dy];
				if (!TileQuery.IsSolid(x + dx, y - dy) && tile.LiquidAmount == 0)
					open++;
			}
		}
		return open;
	}

	static bool IsGroundBeneath(Landmark ground, Point spot)
	{
		for (int y = spot.Y; y < spot.Y + ArenaProbeDepth; y++)
		{
			var tile = Main.tile[spot.X, y];
			if (tile.HasTile && Landmarks.Classify(spot.X, y, tile.TileType) == ground)
				return true;
		}
		return false;
	}

	public static Point? StandingSpotNear(Point center)
	{
		for (int radius = 1; radius < StandingSearchRadius; radius++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					int x = center.X + dx, y = center.Y + dy;
					bool clear = !TileQuery.IsSolid(x, y) && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x, y - 2) && Main.tile[x, y].LiquidAmount == 0;
					if (clear && TileQuery.CanStandOn(x, y + 1))
						return new Point(x, y);
				}
			}
		}
		return null;
	}

	const int UndergroundHuntMinDepth = 30;
	const int UndergroundHuntMaxDepth = 200;

	public static Vector2? PickUndergroundSpot(FactionKnowledge knowledge, Landmark ground)
	{
		if (knowledge.SpanOf(ground) is not var (minX, maxX))
			return null;
		int x = Main.rand.Next(minX, maxX + 1);
		int y = (int)Main.rockLayer + Main.rand.Next(UndergroundHuntMinDepth, UndergroundHuntMaxDepth);
		return new Vector2(x * 16f, y * 16f);
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
