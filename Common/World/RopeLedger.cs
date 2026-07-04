using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public sealed class RopeLedger : ModSystem
{
	const uint RopeLifetimeTicks = 10 * 3600;
	const uint SweepIntervalTicks = 600;
	const int MaxEntries = 20000;

	static readonly Dictionary<Point, uint> placed = new();

	public static void Note(Point tile)
	{
		if (placed.Count < MaxEntries)
			placed[tile] = Main.GameUpdateCount;
	}

	public override void OnWorldUnload() => placed.Clear();

	public override void PostUpdateWorld()
	{
		if (Main.GameUpdateCount % SweepIntervalTicks != 0 || placed.Count == 0)
			return;
		uint now = Main.GameUpdateCount;
		foreach (var (tile, at) in placed.ToList())
		{
			if (now - at < RopeLifetimeTicks)
				continue;
			var current = Main.tile[tile.X, tile.Y];
			if (!current.HasTile || !Main.tileRope[current.TileType])
			{
				placed.Remove(tile);
				continue;
			}
			if (Bots.Tasks.TidyCampTask.KeptRope(tile.X, tile.Y) || Bots.Tasks.TidyCampTask.BotNear(tile))
				continue;
			placed.Remove(tile);
			WorldGen.KillTile(tile.X, tile.Y, noItem: true);
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendTileSquare(-1, tile.X, tile.Y);
		}
	}
}
