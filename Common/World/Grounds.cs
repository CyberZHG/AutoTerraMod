using System;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.World;

public static class Grounds
{
	const int DungeonRange = 120;
	const int SurfaceBand = 60;
	public const int UnderworldDepth = 60;

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
		if (Density(ground, x) < BiomeDensity)
			x = CachedDensestColumn(ground, minX, maxX);
		return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
	}

	const int TownQuietRadius = 130;
	const int QuietStep = 8;
	const int QuietDistanceWeight = 4;
	const uint QuietCacheTicks = 3600;

	static readonly System.Collections.Generic.Dictionary<Landmark, (uint at, Vector2? spot)> quiet = new();

	public static Vector2? QuietSurfaceSpot(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (quiet.TryGetValue(ground, out var cached) && Main.GameUpdateCount - cached.at < QuietCacheTicks)
			return cached.spot;
		Vector2? found = null;
		if (knowledge.SpanOf(ground) is var (minX, maxX))
		{
			int bestScore = int.MinValue;
			for (int x = minX; x <= maxX; x += QuietStep)
			{
				if (NearTown(x))
					continue;
				int density = Density(ground, x);
				var feet = new Point(x, TileQuery.SurfaceY(x) - 2);
				if (density < BiomeDensity || feet.Y + 2 > Main.worldSurface || !IsGroundBeneath(ground, feet)
					|| global::AutoTerraMod.Common.Bosses.BossRituals.InPillarZone(feet.ToWorldCoordinates()))
					continue;
				int score = density - Math.Abs(x - from.X) / QuietDistanceWeight;
				if (score > bestScore)
				{
					bestScore = score;
					found = new Vector2(x * 16f, feet.Y * 16f);
				}
			}
		}
		quiet[ground] = (Main.GameUpdateCount, found);
		return found;
	}

	static bool NearTown(int x)
	{
		foreach (var npc in Main.npc)
		{
			if (npc.active && npc.townNPC && Math.Abs(npc.Center.X / 16f - x) < TownQuietRadius)
				return true;
		}
		return false;
	}

	const int ArenaProbeDepth = 6;
	const int StandingSearchRadius = 20;

	const int EvilArenaDepth = 25;

	public static Vector2? ArenaSpot(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (ground is Landmark.Crimson or Landmark.Corruption && knowledge.SpanOf(ground) is var (minX, maxX))
		{
			int x = CachedDensestColumn(ground, minX, maxX);
			if (OpenSpotNear(new Point(x, TileQuery.SurfaceY(x) + EvilArenaDepth), ground) is Point deep)
				return new Vector2(deep.X * 16f + 8f, deep.Y * 16f);
		}
		if (SurfaceSpotNear(knowledge, ground, from) is Vector2 surface && (ground != Landmark.Hallow || IsGroundBeneath(ground, surface.ToTileCoordinates())))
			return surface;
		if (knowledge.LandmarkOf(ground) is not Point anchor || OpenSpotNear(anchor, ground) is not Point feet)
			return null;
		return new Vector2(feet.X * 16f + 8f, feet.Y * 16f);
	}

	const int CampArenaNearest = 14;
	const int CampArenaFarthest = 40;
	const int CampArenaClimbPenalty = 12;

	public static Vector2 CampArena(Faction faction)
	{
		if (faction.Settlement is not { } camp)
			return faction.BaseCenter;
		Point? best = null;
		int bestScore = int.MinValue;
		for (int offset = CampArenaNearest; offset <= CampArenaFarthest; offset += OpenSearchStep)
		{
			foreach (int x in new[] { camp.Right + offset, camp.Left - offset })
			{
				int y = TileQuery.SurfaceY(x) - 1;
				if (!WorldGen.InWorld(x, y, OpennessHalfWidth + OpennessHeight + 2) || !IsDryFooting(x, y) || !SolidUnderfoot(x, y))
					continue;
				int score = Openness(x, y) - CampArenaClimbPenalty * Math.Abs(y + 1 - camp.Floor);
				if (score > bestScore)
				{
					bestScore = score;
					best = new Point(x, y);
				}
			}
		}
		if (best is Point feet)
			return new Vector2(feet.X * 16f + 8f, feet.Y * 16f - 5f);
		var beside = new Point(camp.Hall.Left + HallSideOffset, camp.Hall.Floor - 1);
		return StandingSpotNear(beside) is Point floor ? floor.ToWorldCoordinates(8f, 0f) : faction.BaseCenter;
	}

	const int HallSideOffset = 3;

	const int UnderfootHalfWidth = 3;
	const int UnderfootDepth = 4;

	static bool SolidUnderfoot(int x, int y)
	{
		for (int dx = -UnderfootHalfWidth; dx <= UnderfootHalfWidth; dx++)
			for (int dy = 1; dy <= UnderfootDepth; dy++)
				if (!TileQuery.IsSolid(x + dx, y + dy))
					return false;
		return true;
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

	const int DensitySamples = 12;
	const int DensityHalfWidth = 20;
	const int DensityAbove = 10;
	const int DensityBelow = 30;
	const int DensityStep = 2;

	const int BiomeDensity = 60;

	static int Density(Landmark ground, int x)
	{
		int surface = TileQuery.SurfaceY(x);
		int count = 0;
		for (int dx = -DensityHalfWidth; dx <= DensityHalfWidth; dx += DensityStep)
		{
			for (int y = surface - DensityAbove; y <= surface + DensityBelow; y += DensityStep)
			{
				if (WorldGen.InWorld(x + dx, y, 2) && Main.tile[x + dx, y].HasTile && Landmarks.Classify(x + dx, y, Main.tile[x + dx, y].TileType) == ground)
					count++;
			}
		}
		return count;
	}

	const uint DensestCacheTicks = 10 * 3600;

	static readonly System.Collections.Generic.Dictionary<Landmark, (uint at, int x)> densest = new();

	static int CachedDensestColumn(Landmark ground, int minX, int maxX)
	{
		if (densest.TryGetValue(ground, out var cached) && Main.GameUpdateCount - cached.at < DensestCacheTicks && cached.x >= minX && cached.x <= maxX)
			return cached.x;
		int x = DensestColumn(ground, minX, maxX);
		densest[ground] = (Main.GameUpdateCount, x);
		return x;
	}

	static int DensestColumn(Landmark ground, int minX, int maxX)
	{
		int best = Main.rand.Next(minX, maxX + 1);
		int bestCount = -1;
		for (int sample = 0; sample < DensitySamples; sample++)
		{
			int x = Main.rand.Next(minX, maxX + 1);
			int count = Density(ground, x);
			if (count > bestCount)
			{
				best = x;
				bestCount = count;
			}
		}
		return best;
	}

	const int DungeonSearchTiles = 150000;
	const int DungeonSearchRadius = 320;
	const int DungeonMinDepth = 12;
	const int DungeonDoorstep = 30;

	static System.Collections.Generic.List<Point> dungeonFloors;
	static (int x, int y) dungeonFloorsFor;

	const int DungeonRoamMin = 30;
	const int DungeonRoamMax = 90;
	const int DungeonPickAttempts = 24;

	static Point FarDungeonFloor(System.Collections.Generic.List<Point> floors, Point from)
	{
		Point? fallback = null;
		for (int attempt = 0; attempt < DungeonPickAttempts; attempt++)
		{
			var pick = floors[Main.rand.Next(floors.Count)];
			int distance = Math.Abs(pick.X - from.X) + Math.Abs(pick.Y - from.Y);
			if (distance >= DungeonRoamMin && distance <= DungeonRoamMax)
				return pick;
			if (fallback is not Point best || distance < Math.Abs(best.X - from.X) + Math.Abs(best.Y - from.Y))
				fallback = pick;
		}
		return fallback ?? floors[0];
	}

	public static bool InsideDungeon(Point tile) =>
		WorldGen.InWorld(tile.X, tile.Y, 10) && Main.wallDungeon[Main.tile[tile.X, tile.Y].WallType];

	static bool PassableInDungeon(int x, int y)
	{
		var tile = Main.tile[x, y];
		return !TileQuery.IsSolid(x, y) || tile.TileType == Terraria.ID.TileID.ClosedDoor;
	}

	static System.Collections.Generic.List<Point> DungeonInterior()
	{
		if (Main.dungeonX <= 0)
			return null;
		if (dungeonFloors != null && dungeonFloorsFor == (Main.dungeonX, Main.dungeonY))
			return dungeonFloors;
		dungeonFloorsFor = (Main.dungeonX, Main.dungeonY);
		dungeonFloors = new System.Collections.Generic.List<Point>();
		var start = new Point(Main.dungeonX, Main.dungeonY);
		var seen = new System.Collections.Generic.HashSet<Point> { start };
		var frontier = new System.Collections.Generic.Queue<Point>();
		frontier.Enqueue(start);
		while (frontier.Count > 0 && seen.Count < DungeonSearchTiles)
		{
			var here = frontier.Dequeue();
			if (here.Y > Main.worldSurface + DungeonMinDepth && Main.wallDungeon[Main.tile[here.X, here.Y].WallType]
				&& TileQuery.IsSolid(here.X, here.Y + 1) && !TileQuery.IsSolid(here.X, here.Y - 1) && !TileQuery.IsSolid(here.X, here.Y - 2))
				dungeonFloors.Add(here);
			foreach (var next in new[] { new Point(here.X + 1, here.Y), new Point(here.X - 1, here.Y), new Point(here.X, here.Y + 1), new Point(here.X, here.Y - 1) })
			{
				if (!WorldGen.InWorld(next.X, next.Y, 10) || Math.Abs(next.X - start.X) > DungeonSearchRadius || Math.Abs(next.Y - start.Y) > DungeonSearchRadius
					|| next.Y < Main.dungeonY - DungeonMinDepth || !PassableInDungeon(next.X, next.Y) || !seen.Add(next))
					continue;
				frontier.Enqueue(next);
			}
		}
		return dungeonFloors;
	}

	static readonly System.Collections.Generic.HashSet<int> LihzahrdAltars = new() { Terraria.ID.TileID.LihzahrdAltar };
	static readonly System.Collections.Generic.HashSet<Point> NoExclusions = new();
	const int TempleAltarReach = 400;

	public static Vector2? PickSpot(FactionKnowledge knowledge, Landmark ground, Point from)
	{
		if (knowledge.LandmarkOf(ground) is not Point anchor || knowledge.SpanOf(ground) is not var (minX, maxX))
			return null;
		switch (ground)
		{
			case Landmark.Underworld:
				return new Vector2(from.X * 16f, (Main.UnderworldLayer + UnderworldDepth) * 16f);
			case Landmark.Dungeon:
				if (Main.dungeonX > 0 && !InsideDungeon(from) && Math.Abs(from.X - Main.dungeonX) + Math.Abs(from.Y - Main.dungeonY) > DungeonDoorstep)
					return (StandingSpotNear(new Point(Main.dungeonX, Main.dungeonY)) ?? new Point(Main.dungeonX, Main.dungeonY)).ToWorldCoordinates(8f, 0f);
				return DungeonInterior() is { Count: > 0 } floors
					? FarDungeonFloor(floors, from).ToWorldCoordinates(8f, 0f)
					: new Vector2((anchor.X + Main.rand.Next(-60, 61)) * 16f, (anchor.Y + Main.rand.Next(0, DungeonRange)) * 16f);
			case Landmark.Temple:
				var altar = knowledge.NearestKnown(anchor, LihzahrdAltars, NoExclusions, TempleAltarReach);
				return TempleDoor.HuntSpot(anchor, altar)?.ToWorldCoordinates(8f, 0f);
			default:
				int x = DensestColumn(ground, minX, maxX);
				return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
		}
	}
}
