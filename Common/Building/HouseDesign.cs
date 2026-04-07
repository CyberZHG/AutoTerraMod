using System;
using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;
using Terraria.Utilities;

namespace AutoTerraMod.Common.Building;

public enum RoofStyle
{
	Flat,
	Gable,
	Shed
}

public sealed record HouseDesign(int Width, int InteriorHeight, RoofStyle Roof, BuildingTaste Taste)
{
	const int MinimumRoomTiles = 72;

	public int BlockItem => Taste switch
	{
		BuildingTaste.StoneBrick => ItemID.GrayBrick,
		BuildingTaste.RedBrick => ItemID.RedBrick,
		_ => ItemID.StoneBlock
	};

	public int WallItem => Taste switch
	{
		BuildingTaste.StoneBrick => ItemID.GrayBrickWall,
		BuildingTaste.RedBrick => ItemID.RedBrickWall,
		_ => ItemID.StoneWall
	};

	static readonly int[] RusticBlocks =
	{
		ItemID.StoneBlock, ItemID.Wood, ItemID.BorealWood, ItemID.PalmWood, ItemID.RichMahogany, ItemID.Ebonwood, ItemID.Shadewood,
		ItemID.GraniteBlock, ItemID.MarbleBlock, ItemID.GrayBrick, ItemID.RedBrick
	};

	public static readonly int[] RusticWalls =
	{
		ItemID.StoneWall, ItemID.WoodWall, ItemID.BorealWoodWall, ItemID.PalmWoodWall, ItemID.RichMahoganyWall, ItemID.EbonwoodWall, ItemID.ShadewoodWall,
		ItemID.GraniteWall, ItemID.MarbleWall, ItemID.GrayBrickWall, ItemID.RedBrickWall
	};

	public static readonly int[] RusticWallSources =
	{
		ItemID.StoneBlock, ItemID.Wood, ItemID.BorealWood, ItemID.PalmWood, ItemID.RichMahogany, ItemID.Ebonwood, ItemID.Shadewood,
		ItemID.GraniteBlock, ItemID.MarbleBlock, ItemID.GrayBrick, ItemID.RedBrick
	};

	public static readonly int[] Doors =
	{
		ItemID.WoodenDoor, ItemID.BorealWoodDoor, ItemID.PalmWoodDoor, ItemID.RichMahoganyDoor, ItemID.EbonwoodDoor, ItemID.ShadewoodDoor, ItemID.CactusDoor
	};

	public static readonly int[] Tables =
	{
		ItemID.WoodenTable, ItemID.BorealWoodTable, ItemID.PalmWoodTable, ItemID.RichMahoganyTable, ItemID.EbonwoodTable, ItemID.ShadewoodTable
	};

	public static readonly int[] Chairs =
	{
		ItemID.WoodenChair, ItemID.BorealWoodChair, ItemID.PalmWoodChair, ItemID.RichMahoganyChair, ItemID.EbonwoodChair, ItemID.ShadewoodChair
	};

	public int[] BlockItems => Taste == BuildingTaste.Rustic ? RusticBlocks : new[] { BlockItem };
	public int[] WallItems => Taste == BuildingTaste.Rustic ? RusticWalls : new[] { WallItem };

	public bool AcceptsWall(int wallType)
	{
		foreach (int item in WallItems)
		{
			if (ContentSamples.ItemsByType[item].createWall == wallType)
				return true;
		}
		return false;
	}

	public bool AcceptsBlockTile(int tileType)
	{
		foreach (int item in BlockItems)
		{
			if (ContentSamples.ItemsByType[item].createTile == tileType)
				return true;
		}
		return false;
	}

	public int BlockTile => ContentSamples.ItemsByType[BlockItem].createTile;
	public int WallType => ContentSamples.ItemsByType[WallItem].createWall;
	public HouseDesign AsRustic() => this with { Taste = BuildingTaste.Rustic };

	public static HouseDesign Roll(Personality builder, UnifiedRandom rand)
	{
		int interiorHeight = rand.Next(4, 7);
		int width = rand.Next(10, 17);
		int minimumWidth = (int)Math.Ceiling(MinimumRoomTiles / (double)(interiorHeight + 2));
		return new HouseDesign(Math.Max(width, minimumWidth), interiorHeight, (RoofStyle)rand.Next(3), builder.Taste);
	}

	public TagCompound Save() => new()
	{
		["width"] = Width,
		["interiorHeight"] = InteriorHeight,
		["roof"] = (int)Roof,
		["taste"] = (int)Taste
	};

	public static HouseDesign Load(TagCompound tag) => new(
		tag.GetInt("width"),
		tag.GetInt("interiorHeight"),
		(RoofStyle)tag.GetInt("roof"),
		(BuildingTaste)tag.GetInt("taste"));
}
