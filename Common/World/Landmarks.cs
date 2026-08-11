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
	Underworld,
	Hallow,
	Ocean,
	Temple
}

public static class Landmarks
{
	public static Landmark? Classify(int x, int y, int tileType)
	{
		if (y >= Main.UnderworldLayer)
			return Landmark.Underworld;
		if (Main.tileDungeon[tileType])
			return Landmark.Dungeon;
		if (WorldGen.oceanDepths(x, y) && tileType is TileID.Sand or TileID.ShellPile)
			return Landmark.Ocean;
		return tileType switch
		{
			TileID.LihzahrdBrick => Landmark.Temple,
			TileID.HallowedGrass or TileID.Pearlstone or TileID.Pearlsand => Landmark.Hallow,
			TileID.Ebonstone or TileID.CorruptGrass => Landmark.Corruption,
			TileID.Crimstone or TileID.CrimsonGrass => Landmark.Crimson,
			TileID.JungleGrass or TileID.Hive => Landmark.Jungle,
			TileID.SnowBlock or TileID.IceBlock => Landmark.Snow,
			_ => null
		};
	}

	public static bool? DrunkCrimson;

	public static bool EvilIsCrimson => Main.drunkWorld ? DrunkCrimson ??= WorldGen.crimson : WorldGen.crimson;

	public static Landmark WorldEvil => EvilIsCrimson ? Landmark.Crimson : Landmark.Corruption;
}
