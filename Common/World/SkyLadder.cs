using Terraria;

namespace AutoTerraMod.Common.World;

public sealed record SkyLadder(int X, int GroundY, int TopY)
{
	public const int RungSpacing = 4;

	public static SkyLadder Beside(Factions.Faction faction, int offset)
	{
		int x = faction.BaseTile.X + offset;
		return new SkyLadder(x, TileQuery.SurfaceY(x) - 1, (int)(Main.worldSurface * SkyFraction) - RungSpacing);
	}

	const double SkyFraction = 0.35;

	public bool HasRung(int y) => TileQuery.IsPlatform(X, y) || TileQuery.IsPlatform(X + 1, y);

	public int? NextRung()
	{
		for (int y = GroundY - RungSpacing; y > TopY; y -= RungSpacing)
		{
			if (!HasRung(y))
				return y;
		}
		return null;
	}

	public bool IsComplete => NextRung() == null;

	public int TopRung
	{
		get
		{
			int top = GroundY;
			for (int y = GroundY - RungSpacing; y > TopY && HasRung(y); y -= RungSpacing)
				top = y;
			return top;
		}
	}
}
