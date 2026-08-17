using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public sealed record Hellbridge(int CenterX, int Y) : ICorridor
{
	const int DeckPlatformEvery = 8;

	public int End(int direction) => direction > 0 ? RightEnd : LeftEnd;

	public bool WantsPlatform(int x) => x % DeckPlatformEvery <= 1 || x == CenterX || x == CenterX + 1;

	public bool RequiresPlatform(int x) => x % DeckPlatformEvery <= 1;

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

	public bool IsBuilt(int x) => Skipped(x) || (IsDeck(x) || ICorridor.HoldsChest(x, FeetRow(x) + 1, FeetRow(x) + 1)) && HeadroomClear(x);

	public bool IsDeck(int x) => (TileQuery.IsPlatform(x, FeetRow(x) + 1) || !RequiresPlatform(x) && TileQuery.IsSolid(x, FeetRow(x) + 1)) && !ICorridor.Scorches(x, FeetRow(x) + 1);

	readonly System.Collections.Generic.Dictionary<int, int> dips = new();
	readonly System.Collections.Generic.HashSet<Point> tolerated = new();

	public bool IsTolerated(int x, int y) => tolerated.Contains(new Point(x, y));

	public void Tolerate(int x, int y)
	{
		tolerated.Add(new Point(x, y));
		frontierAt[0] = frontierAt[1] = 0;
	}

	public int FeetRow(int x) => Y + (dips.TryGetValue(x, out int dip) ? dip : 0);

	public void Dip(int x, int rows)
	{
		dips[x] = System.Math.Max(rows, dips.TryGetValue(x, out int dip) ? dip : 0);
		frontierAt[0] = frontierAt[1] = 0;
	}

	public int Headroom => 5;

	readonly System.Collections.Generic.Dictionary<int, uint> skipped = new();
	const uint SkipRetryTicks = 10 * 3600;

	bool Skipped(int x) => skipped.TryGetValue(x, out uint at) && Terraria.Main.GameUpdateCount - at < SkipRetryTicks;

	public void Skip(int x)
	{
		skipped[x] = Terraria.Main.GameUpdateCount;
		frontierAt[0] = frontierAt[1] = 0;
	}

	public bool HeadroomClear(int x)
	{
		for (int y = FeetRow(x) - Headroom + 1; y <= FeetRow(x); y++)
		{
			if (TileQuery.IsSolid(x, y) && !tolerated.Contains(new Point(x, y)))
				return false;
		}
		return true;
	}

	readonly uint[] frontierAt = new uint[2];
	readonly int[] frontierCache = new int[2];
	const uint FrontierRecheckTicks = 300;

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
