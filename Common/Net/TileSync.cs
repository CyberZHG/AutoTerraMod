using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Net;

public sealed class TileSync : ModSystem
{
	const int TileSquare = 3;
	const int ObjectSquare = 7;
	const int MaxSquaresPerTick = 400;

	static readonly Dictionary<Point, int> dirty = new();

	static bool Tracking => Main.netMode == NetmodeID.Server && World.ShrineGuard.IsBotActing;

	public override void Load()
	{
		On_WorldGen.KillTile += KillTile;
		On_WorldGen.PlaceTile += PlaceTile;
		On_WorldGen.ReplaceTile += ReplaceTile;
		On_WorldGen.KillWall += KillWall;
		On_WorldGen.PlaceWall += PlaceWall;
		On_WorldGen.ReplaceWall += ReplaceWall;
		On_WorldGen.PlaceObject += PlaceObject;
		On_WorldGen.OpenDoor += OpenDoor;
		On_WorldGen.CloseDoor += CloseDoor;
		On_WorldGen.UnlockDoor += UnlockDoor;
	}

	public override void Unload() => dirty.Clear();

	public static void Mark(int x, int y, int size = TileSquare)
	{
		if (Main.netMode != NetmodeID.Server || !WorldGen.InWorld(x, y, 2))
			return;
		var key = new Point(x, y);
		dirty[key] = System.Math.Max(dirty.GetValueOrDefault(key), size);
	}

	public override void PostUpdateEverything()
	{
		if (dirty.Count == 0)
			return;
		if (Main.netMode != NetmodeID.Server)
		{
			dirty.Clear();
			return;
		}
		int sent = 0;
		var covered = new HashSet<Point>();
		foreach (var (tile, size) in dirty)
		{
			if (sent >= MaxSquaresPerTick)
				break;
			if (size > TileSquare)
			{
				NetMessage.SendTileSquare(-1, tile.X, tile.Y, size);
				sent++;
				continue;
			}
			var cell = new Point(tile.X / TileSquare, tile.Y / TileSquare);
			if (!covered.Add(cell))
				continue;
			NetMessage.SendTileSquare(-1, cell.X * TileSquare + 1, cell.Y * TileSquare + 1, TileSquare + 2);
			sent++;
		}
		dirty.Clear();
	}

	static void KillTile(On_WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
	{
		orig(i, j, fail, effectOnly, noItem);
		if (Tracking)
			Mark(i, j);
	}

	static bool PlaceTile(On_WorldGen.orig_PlaceTile orig, int i, int j, int type, bool mute, bool forced, int plr, int style)
	{
		bool placed = orig(i, j, type, mute, forced, plr, style);
		if (placed && Tracking)
			Mark(i, j, Main.tileFrameImportant[type] ? ObjectSquare : TileSquare);
		return placed;
	}

	static bool ReplaceTile(On_WorldGen.orig_ReplaceTile orig, int x, int y, ushort targetType, int targetStyle)
	{
		bool replaced = orig(x, y, targetType, targetStyle);
		if (replaced && Tracking)
			Mark(x, y);
		return replaced;
	}

	static void KillWall(On_WorldGen.orig_KillWall orig, int i, int j, bool fail)
	{
		orig(i, j, fail);
		if (Tracking)
			Mark(i, j);
	}

	static void PlaceWall(On_WorldGen.orig_PlaceWall orig, int i, int j, int type, bool mute)
	{
		orig(i, j, type, mute);
		if (Tracking)
			Mark(i, j);
	}

	static bool ReplaceWall(On_WorldGen.orig_ReplaceWall orig, int x, int y, ushort targetWall)
	{
		bool replaced = orig(x, y, targetWall);
		if (replaced && Tracking)
			Mark(x, y);
		return replaced;
	}

	static bool PlaceObject(On_WorldGen.orig_PlaceObject orig, int x, int y, int type, bool mute, int style, int alternate, int random, int direction)
	{
		bool placed = orig(x, y, type, mute, style, alternate, random, direction);
		if (placed && Tracking)
			Mark(x, y, ObjectSquare);
		return placed;
	}

	static bool OpenDoor(On_WorldGen.orig_OpenDoor orig, int i, int j, int direction)
	{
		bool opened = orig(i, j, direction);
		if (opened && Tracking)
			Mark(i, j, ObjectSquare);
		return opened;
	}

	static bool CloseDoor(On_WorldGen.orig_CloseDoor orig, int i, int j, bool forced)
	{
		bool closed = orig(i, j, forced);
		if (closed && Tracking)
			Mark(i, j, ObjectSquare);
		return closed;
	}

	static void UnlockDoor(On_WorldGen.orig_UnlockDoor orig, int i, int j)
	{
		orig(i, j);
		if (Tracking)
			Mark(i, j, ObjectSquare);
	}
}
