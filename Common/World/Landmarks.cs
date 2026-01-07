using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public enum Landmark
{
	Dungeon,
	Corruption,
	Crimson,
	Jungle,
	Snow,
	Underworld
}

public static class Landmarks
{
	public static Landmark? Classify(int y, int tileType)
	{
		if (y >= Main.UnderworldLayer)
			return Landmark.Underworld;
		if (Main.tileDungeon[tileType])
			return Landmark.Dungeon;
		return tileType switch
		{
			TileID.Ebonstone or TileID.CorruptGrass => Landmark.Corruption,
			TileID.Crimstone or TileID.CrimsonGrass => Landmark.Crimson,
			TileID.JungleGrass or TileID.Hive => Landmark.Jungle,
			TileID.SnowBlock or TileID.IceBlock => Landmark.Snow,
			_ => null
		};
	}

	public static Landmark WorldEvil => WorldGen.crimson ? Landmark.Crimson : Landmark.Corruption;
}
