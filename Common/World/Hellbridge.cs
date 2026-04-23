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

	public const int HalfLength = 900;

	int Away => CenterX > Main.maxTilesX / 2 ? 1 : -1;
	public int LeftEnd => Away < 0 ? System.Math.Max(WorldEdgeMargin, CenterX - HalfLength) : WorldEdgeMargin;
	public int RightEnd => Away > 0 ? System.Math.Min(Main.maxTilesX - WorldEdgeMargin, CenterX + HalfLength) : Main.maxTilesX - WorldEdgeMargin;
	const int WorldEdgeMargin = 45;

	public Vector2 Center => new(CenterX * 16f + 8f, Y * 16f);

	public const int DeckFromBottom = 150;

	public static int FightRow => Main.maxTilesY - DeckFromBottom;

	public static Hellbridge Plan(Point anchor) => new(anchor.X, FightRow);

	const int SummonInset = 4;
	const int SummonReach = 400;
	int? summonEdge;

	public Vector2 SummonPoint
	{
		get
		{
			int away = Away;
			int built = Frontier(away) - away * SummonInset;
			int edge = SideComplete(away) ? built : away > 0 ? System.Math.Min(built, CenterX + SummonReach) : System.Math.Max(built, CenterX - SummonReach);
			if (summonEdge is int chosen && (Frontier(away) - chosen) * away > 0 && (chosen - edge) * away > 0)
				edge = chosen;
			summonEdge = edge;
			return new Vector2(edge * 16f + 8f, Y * 16f);
		}
	}

	public int BuiltLength => Frontier(1) - Frontier(-1);

	public bool IsBuilt(int x) => skipped.Contains(x) || IsDeck(x) && HeadroomClear(x) || ICorridor.HoldsChest(x, Y - Headroom + 1, Y + 1);

	public bool IsDeck(int x) => (TileQuery.IsPlatform(x, Y + 1) || TileQuery.IsSolid(x, Y + 1)) && !ICorridor.Scorches(x, Y + 1);

	public int Headroom => 3;

	readonly System.Collections.Generic.HashSet<int> skipped = new();

	public void Skip(int x)
	{
		skipped.Add(x);
		frontierAt[0] = frontierAt[1] = 0;
	}

	public bool HeadroomClear(int x)
	{
		for (int y = Y - Headroom + 1; y <= Y; y++)
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

	public const int FightableLength = 800;

	public bool IsComplete => SideComplete(1) && SideComplete(-1);
}
