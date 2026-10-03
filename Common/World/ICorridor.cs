namespace AutoTerraMod.Common.World;

public interface ICorridor
{
	int Y { get; }

	int End(int direction);

	int Frontier(int direction);

	bool IsBuilt(int x);

	bool IsDeck(int x);

	bool WantsPlatform(int x);

	bool RequiresPlatform(int x);

	int Headroom { get; }

	void Skip(int x);

	bool Skipped(int x);

	int FeetRow(int x) => Y;

	void Dip(int x, int rows) { }

	bool Raise(int x, int direction) => false;

	void Tolerate(int x, int y);

	bool IsTolerated(int x, int y);

	public const int BodyRows = 3;

	public static bool HoldsChest(int x, int top, int bottom)
	{
		for (int y = top; y <= bottom; y++)
		{
			if (Terraria.Main.tile[x, y].HasTile && Terraria.ID.TileID.Sets.BasicChest[Terraria.Main.tile[x, y].TileType])
				return true;
		}
		return false;
	}

	public static bool Scorches(int x, int y) => Terraria.Main.tile[x, y].HasTile && Terraria.Main.tile[x, y].TileType is Terraria.ID.TileID.Hellstone or Terraria.ID.TileID.HellstoneBrick;
}
