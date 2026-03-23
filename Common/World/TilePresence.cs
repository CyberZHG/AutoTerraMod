using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public static class TilePresence
{
	const uint RefreshTicks = 60 * 60 * 10;
	const int ColumnsPerTick = 40;

	static int[] counts;
	static int[] building;
	static int nextColumn;
	static uint countedAt;
	static uint advancedAt = uint.MaxValue;

	public static bool AnyExists(IEnumerable<int> tileTypes)
	{
		Advance();
		if (counts == null)
			return true;
		foreach (int type in tileTypes)
		{
			if (type >= 0 && type < counts.Length && counts[type] > 0)
				return true;
		}
		return false;
	}

	public static void Reset()
	{
		counts = null;
		building = null;
		nextColumn = 0;
		advancedAt = uint.MaxValue;
	}

	static void Advance()
	{
		if (advancedAt == Main.GameUpdateCount || counts != null && building == null && Main.GameUpdateCount - countedAt < RefreshTicks)
			return;
		advancedAt = Main.GameUpdateCount;
		using var _ = Systems.Perf.Measure(Systems.PerfSection.WorldScans);
		building ??= new int[TileLoader.TileCount];
		int end = System.Math.Min(Main.maxTilesX, nextColumn + ColumnsPerTick);
		for (int x = nextColumn; x < end; x++)
		{
			for (int y = 0; y < Main.maxTilesY; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile)
					building[tile.TileType]++;
			}
		}
		nextColumn = end;
		if (nextColumn < Main.maxTilesX)
			return;
		counts = building;
		building = null;
		nextColumn = 0;
		countedAt = Main.GameUpdateCount;
	}
}
