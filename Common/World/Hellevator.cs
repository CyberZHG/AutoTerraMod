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

	public const int Width = 3;

	public int RopeX => X + 1;

	public int Right => X + Width - 1;

	public bool HasRope(int y) => Main.tile[RopeX, y].HasTile && Main.tileRope[Main.tile[RopeX, y].TileType];

	public bool NeedsCleanup(int x, int y) => x != RopeX && TileQuery.IsPlatform(x, y) && HasRope(y);

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

	public bool Contains(int x, int y) => x >= X && x <= Right && y > TopY && y < Main.UnderworldLayer;

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
