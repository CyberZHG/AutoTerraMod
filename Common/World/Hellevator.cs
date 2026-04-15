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

	public int RopeX => X + 1;

	public Microsoft.Xna.Framework.Point? LavaFloor(int row)
	{
		for (int y = row; y < System.Math.Min(row + LavaFloorScan, Main.maxTilesY - 10); y++)
		{
			for (int x = X; x <= X + 1; x++)
			{
				if (TileQuery.IsSolid(x, y))
					return new Microsoft.Xna.Framework.Point(x, y);
			}
		}
		return null;
	}

	public bool IsShaftRow(int y) => !TileQuery.IsSolid(X, y) && !TileQuery.IsSolid(X + 1, y);

	public bool NeedsPlatform(int y) => y > TopY + PlatformSpacing && (y - TopY) % PlatformSpacing == 0;

	public bool HasPlatform(int y) => TileQuery.IsPlatform(X, y) || TileQuery.IsPlatform(X + 1, y);

	public bool RowDone(int y) => IsShaftRow(y);

	public int FirstUnfinishedRow()
	{
		for (int y = TopY; y < Main.UnderworldLayer; y++)
		{
			if (!RowDone(y))
				return y;
		}
		return Main.UnderworldLayer;
	}

	const int CompletionSlack = 6;

	public bool IsComplete => FirstUnfinishedRow() >= Main.UnderworldLayer - CompletionSlack;

	public static bool LavaBelow(int x, int y)
	{
		for (int dx = 0; dx <= 1; dx++)
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
