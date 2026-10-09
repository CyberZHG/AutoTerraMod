using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Navigation;

public static class DangerSpots
{
	const uint LifetimeTicks = 20 * 60 * 60;
	const int SpreadLeft = 2;
	const int SpreadRight = 3;
	const int SpreadVertical = 1;

	static readonly Dictionary<Point, uint> markedUntil = new();
	static readonly Dictionary<Point, uint> refusedDigs = new();
	const uint RefusedDigTicks = 10 * 60 * 60;

	public static void RefuseDig(Point tile) => refusedDigs[tile] = Main.GameUpdateCount + RefusedDigTicks;

	public static bool DigRefused(int x, int y) =>
		refusedDigs.Count > 0 && refusedDigs.TryGetValue(new Point(x, y), out uint until) && Main.GameUpdateCount < until;
	static readonly List<Point> expired = new();
	static uint lastPruned;

	public static void Mark(Point feet)
	{
		uint until = Main.GameUpdateCount + LifetimeTicks;
		for (int dx = -SpreadLeft; dx <= SpreadRight; dx++)
		{
			for (int dy = -SpreadVertical; dy <= SpreadVertical; dy++)
				markedUntil[new Point(feet.X + dx, feet.Y + dy)] = until;
		}
	}

	public static bool IsMarked(int x, int y) =>
		markedUntil.Count > 0 && markedUntil.TryGetValue(new Point(x, y), out uint until) && Main.GameUpdateCount < until;

	public static void Prune()
	{
		if (Main.GameUpdateCount - lastPruned < 3600)
			return;
		lastPruned = Main.GameUpdateCount;
		foreach (var (tile, until) in markedUntil)
		{
			if (Main.GameUpdateCount >= until)
				expired.Add(tile);
		}
		foreach (var tile in expired)
			markedUntil.Remove(tile);
		expired.Clear();
		foreach (var (tile, until) in refusedDigs)
		{
			if (Main.GameUpdateCount >= until)
				expired.Add(tile);
		}
		foreach (var tile in expired)
			refusedDigs.Remove(tile);
		expired.Clear();
	}

	public static void Reset()
	{
		markedUntil.Clear();
		refusedDigs.Clear();
	}
}
