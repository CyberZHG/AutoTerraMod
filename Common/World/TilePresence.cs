using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public static class TilePresence
{
	const uint RefreshTicks = 60 * 60 * 10;
	const int ColumnsPerTick = 40;

	const int HerbStyleCount = 7;

	static int[] counts;
	static int[] building;
	static int[] herbs;
	static int[] herbsBuilding;
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

	public static int HerbCount(int style)
	{
		Advance();
		return herbs == null || style < 0 || style >= herbs.Length ? int.MaxValue : herbs[style];
	}

	public static void Reset()
	{
		counts = null;
		building = null;
		herbs = null;
		herbsBuilding = null;
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
		herbsBuilding ??= new int[HerbStyleCount];
		int end = System.Math.Min(Main.maxTilesX, nextColumn + ColumnsPerTick);
		for (int x = nextColumn; x < end; x++)
		{
			for (int y = 0; y < Main.maxTilesY; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile)
					continue;
				building[tile.TileType]++;
				if (tile.TileType is Terraria.ID.TileID.MatureHerbs or Terraria.ID.TileID.BloomingHerbs && tile.TileFrameX / 18 < HerbStyleCount)
					herbsBuilding[tile.TileFrameX / 18]++;
			}
		}
		nextColumn = end;
		if (nextColumn < Main.maxTilesX)
			return;
		counts = building;
		building = null;
		herbs = herbsBuilding;
		herbsBuilding = null;
		nextColumn = 0;
		countedAt = Main.GameUpdateCount;
	}
}
