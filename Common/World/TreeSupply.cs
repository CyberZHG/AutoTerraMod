using System.Collections.Generic;
using AutoTerraMod.Common.Factions;
using Terraria;

namespace AutoTerraMod.Common.World;

public static class TreeSupply
{
	const int SearchHalfWidth = 150;
	const int RowsAboveSurface = 30;
	const int RowsBelowSurface = 10;
	const int PlentifulTrees = 12;
	const uint RecountTicks = 3600;

	static readonly Dictionary<int, (uint at, int trees)> counts = new();

	public static void Reset() => counts.Clear();

	public static bool Plentiful(Faction faction) => TreesNearBase(faction) >= PlentifulTrees;

	public static int TreesNearBase(Faction faction)
	{
		if (counts.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.at < RecountTicks)
			return cached.trees;
		int trees = Count(faction.BaseCenter.ToTileCoordinates().X);
		counts[faction.Id] = (Main.GameUpdateCount, trees);
		return trees;
	}

	static int Count(int centerX)
	{
		int trees = 0;
		for (int x = centerX - SearchHalfWidth; x <= centerX + SearchHalfWidth; x++)
		{
			if (!WorldGen.InWorld(x, 0, 10))
				continue;
			int surface = TileQuery.SurfaceY(x);
			for (int y = surface - RowsAboveSurface; y <= surface + RowsBelowSurface; y++)
			{
				if (WorldGen.InWorld(x, y, 10) && TileQuery.IsTreeBase(x, y))
				{
					trees++;
					break;
				}
			}
		}
		return trees;
	}
}
