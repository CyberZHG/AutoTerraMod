using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Navigation;

public static class PickRequirement
{
	const double DungeonCenterStart = 0.35;
	const double DungeonCenterEnd = 0.65;

	public static int MinimumPick(int tileType, int x, int y)
	{
		if (TileLoader.GetTile(tileType) is ModTile modTile)
			return modTile.MinPick;
		if (Main.tileDungeon[tileType] && y > Main.worldSurface && (x < Main.maxTilesX * DungeonCenterStart || x > Main.maxTilesX * DungeonCenterEnd))
			return 100;
		return tileType switch
		{
			TileID.Chlorophyte => 200,
			TileID.Ebonstone or TileID.Crimstone or TileID.Pearlstone => 65,
			TileID.Meteorite => 50,
			TileID.Demonite or TileID.Crimtane when y > Main.worldSurface => 55,
			TileID.Obsidian => 55,
			TileID.Hellstone => 65,
			TileID.LihzahrdAltar => int.MaxValue,
			TileID.LihzahrdBrick => 210,
			TileID.Cobalt or TileID.Palladium => 100,
			TileID.Mythril or TileID.Orichalcum => 110,
			TileID.Adamantite or TileID.Titanium => 150,
			_ => 0
		};
	}
}
