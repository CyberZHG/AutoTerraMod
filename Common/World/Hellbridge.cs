using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public sealed record Hellbridge(int CenterX, int Y) : ICorridor
{
	const int DeckPlatformEvery = 8;

	public int End(int direction) => direction > 0 ? RightEnd : LeftEnd;

	public bool WantsPlatform(int x) => x % DeckPlatformEvery == 0 || x == CenterX || x == CenterX + 1;

	public bool RequiresPlatform(int x) => false;

	public const int HalfLength = 300;
	const int LavaClearance = 8;

	public int LeftEnd => System.Math.Max(WorldEdgeMargin, CenterX - HalfLength);
	public int RightEnd => System.Math.Min(Main.maxTilesX - WorldEdgeMargin, CenterX + HalfLength);
	const int WorldEdgeMargin = 45;

	public Vector2 Center => new(CenterX * 16f + 8f, Y * 16f);

	public static Hellbridge Plan(Point anchor)
	{
		for (int y = System.Math.Max(anchor.Y, Main.UnderworldLayer); y < Main.maxTilesY - 10; y++)
		{
			if (StandsHere(anchor.X, y))
				return new Hellbridge(anchor.X, y);
			if (IsLava(anchor.X, y))
				return new Hellbridge(anchor.X, y - LavaClearance);
		}
		return new Hellbridge(anchor.X, Main.maxTilesY - 120);
	}

	static bool IsLava(int x, int y) => Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Lava;

	public int BuiltLength => Frontier(1) - Frontier(-1);

	static bool StandsHere(int x, int y) =>
		TileQuery.CanStandOn(x, y + 1) && !TileQuery.IsSolid(x, y) && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x, y - 2)
		&& Main.tile[x, y].LiquidAmount == 0;

	public bool IsBuilt(int x) => IsDeck(x) && HeadroomClear(x);

	public bool IsDeck(int x) => TileQuery.IsPlatform(x, Y + 1) || TileQuery.IsSolid(x, Y + 1);

	public bool HeadroomClear(int x)
	{
		for (int y = Y - 2; y <= Y; y++)
		{
			if (TileQuery.IsSolid(x, y))
				return false;
		}
		return true;
	}

	readonly uint[] frontierAt = new uint[2];
	readonly int[] frontierCache = new int[2];
	const uint FrontierRecheckTicks = 30;

	public int Frontier(int direction)
	{
		int side = direction > 0 ? 1 : 0;
		int end = direction > 0 ? RightEnd : LeftEnd;
		bool fresh = Main.GameUpdateCount - frontierAt[side] < FrontierRecheckTicks;
		int from = fresh ? frontierCache[side] : CenterX;
		if (!fresh)
			frontierAt[side] = Main.GameUpdateCount;
		for (int x = from; x != end; x += direction)
		{
			if (!IsBuilt(x))
				return frontierCache[side] = x;
		}
		return frontierCache[side] = end;
	}

	public bool SideComplete(int direction) => Frontier(direction) == (direction > 0 ? RightEnd : LeftEnd);

	public const int FightableLength = 400;

	public bool IsComplete => (SideComplete(1) && SideComplete(-1)) || Frontier(1) - Frontier(-1) >= FightableLength;
}
