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
	}

	public static void Reset() => markedUntil.Clear();
}
