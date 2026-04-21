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

	public static bool Scorches(int x, int y) => Terraria.Main.tile[x, y].HasTile && Terraria.Main.tile[x, y].TileType is Terraria.ID.TileID.Hellstone or Terraria.ID.TileID.HellstoneBrick;
}
