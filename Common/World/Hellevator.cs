using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public sealed record Hellevator(int X, int TopY)
{
	public const int PlatformSpacing = 4;
	public const int MaxLavaBlasts = 6;
	const int LavaFloorScan = 40;

	public int LavaBlasts { get; set; }

	public uint LastBlastAt { get; set; }

	public const uint DrainTicks = 600;

	public uint FloodedUntil { get; set; }

	public const uint FloodPauseTicks = 10 * 3600;

	public const int SafeDiveRows = 10;

	public const int DesperateDiveRows = 25;

	public const int MaxWaterBlasts = 6;

	public int WaterBlasts { get; set; }

	public int WaterAbove(int row)
	{
		int rows = 0;
		for (int y = row - 1; y > TopY && Main.tile[RopeX, y].LiquidAmount > 0 && Main.tile[RopeX, y].LiquidType != LiquidID.Lava; y--)
			rows++;
		return rows;
	}

	public const int Width = 3;

	public int RopeX => X + 1;

	public int Right => X + Width - 1;

	public bool HasRope(int y) => Main.tile[RopeX, y].HasTile && Main.tileRope[Main.tile[RopeX, y].TileType];

	public bool NeedsCleanup(int x, int y) => x != RopeX && (TileQuery.IsPlatform(x, y) && HasRope(y) || Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType]);

	public Microsoft.Xna.Framework.Point? LavaFloor(int row)
	{
		for (int y = row; y < System.Math.Min(row + LavaFloorScan, Main.maxTilesY - 10); y++)
		{
			for (int x = X; x <= Right; x++)
			{
				if (TileQuery.IsSolid(x, y))
					return new Microsoft.Xna.Framework.Point(x, y);
			}
		}
		return null;
	}

	public bool Contains(int x, int y) => x >= X && x <= Right && y > TopY && y <= (LandingRow ?? Main.UnderworldLayer - 1);

	public static bool OffRopeColumn(int x, int y)
	{
		foreach (var faction in Common.Systems.BotWorldSystem.Factions)
		{
			if (faction.Hellevator is { } shaft && shaft.Contains(x, y) && x != shaft.RopeX)
				return true;
		}
		return false;
	}

	public static bool InOthersShaft(int factionId, int x, int y)
	{
		foreach (var faction in Common.Systems.BotWorldSystem.Factions)
		{
			if (faction.Id != factionId && faction.Hellevator is { } shaft && x >= shaft.X - 1 && x <= shaft.Right + 1 && shaft.Contains(System.Math.Clamp(x, shaft.X, shaft.Right), y))
				return true;
		}
		return false;
	}

	public static bool InAnyShaft(int x, int y)
	{
		foreach (var faction in Common.Systems.BotWorldSystem.Factions)
		{
			if (faction.Hellevator is { } shaft && shaft.Contains(x, y))
				return true;
		}
		return false;
	}

	public bool IsPassableRow(int y) => !TileQuery.IsSolid(X, y) && !TileQuery.IsSolid(X + 1, y);

	public bool NeedsPlatform(int y) => y > TopY + PlatformSpacing && (y - TopY) % PlatformSpacing == 0 && !HasRope(y);

	public bool HasPlatform(int y) => TileQuery.IsPlatform(X, y) || TileQuery.IsPlatform(X + 1, y) || TileQuery.IsPlatform(Right, y);

	readonly System.Collections.Generic.HashSet<int> narrowRows = new();

	public void LeaveNarrow(int y)
	{
		narrowRows.Add(y);
		scannedAt = uint.MaxValue;
	}

	public bool RowDone(int y)
	{
		for (int x = X; x <= Right; x++)
		{
			if (x == Right && narrowRows.Contains(y))
				continue;
			if (TileQuery.IsSolid(x, y) || NeedsCleanup(x, y))
				return false;
		}
		return true;
	}

	int? landingRow;

	public int? LandingRow
	{
		get => landingRow;
		set
		{
			if (landingRow != value)
				scannedAt = uint.MaxValue;
			landingRow = value;
		}
	}

	int BottomRow => LandingRow is int landing ? System.Math.Max(landing + 1, Main.UnderworldLayer - CompletionSlack) : Main.UnderworldLayer - CompletionSlack;

	uint scannedAt = uint.MaxValue;
	int firstUnfinished, firstImpassable;

	void Scan()
	{
		if (scannedAt == Main.GameUpdateCount)
			return;
		scannedAt = Main.GameUpdateCount;
		int end = System.Math.Max(Main.UnderworldLayer, BottomRow);
		firstImpassable = end;
		firstUnfinished = end;
		for (int y = TopY; y < end; y++)
		{
			if (firstUnfinished == end && !RowDone(y))
				firstUnfinished = y;
			if (!IsPassableRow(y))
			{
				firstImpassable = y;
				break;
			}
		}
	}

	public int FirstUnfinishedRow()
	{
		Scan();
		return firstUnfinished;
	}

	public int FirstImpassableRow()
	{
		Scan();
		return firstImpassable;
	}

	const int CompletionSlack = 6;

	public bool IsComplete => FirstImpassableRow() >= BottomRow;

	public bool IsFinished => FirstUnfinishedRow() >= BottomRow;

	public static bool LavaBelow(int x, int y)
	{
		for (int dx = 0; dx < Width; dx++)
		{
			for (int dy = 0; dy <= 2; dy++)
			{
				var tile = Main.tile[x + dx, y + dy];
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava)
					return true;
			}
		}
		return false;
	}
}
