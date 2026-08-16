using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

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

	public override void ClearWorld() => placed.Clear();

	public override void SaveWorldData(TagCompound tag)
	{
		uint now = Main.GameUpdateCount;
		tag["placed"] = placed.SelectMany(entry => new[] { entry.Key.X, entry.Key.Y, (int)System.Math.Min(now - entry.Value, RopeLifetimeTicks) }).ToArray();
	}

	public override void LoadWorldData(TagCompound tag)
	{
		placed.Clear();
		var saved = tag.GetIntArray("placed");
		for (int i = 0; i + 2 < saved.Length; i += 3)
			placed[new Point(saved[i], saved[i + 1])] = Main.GameUpdateCount - (uint)saved[i + 2];
	}

	static bool Temporary(Point tile)
	{
		var current = Main.tile[tile.X, tile.Y];
		return current.HasTile && (Main.tileRope[current.TileType] || TileQuery.IsPlatform(tile.X, tile.Y));
	}

	static bool Kept(Point tile) =>
		Bots.Tasks.TidyCampTask.KeptRope(tile.X, tile.Y) || Bots.Tasks.TidyCampTask.BotNear(tile)
		|| Systems.BotWorldSystem.Factions.Any(faction => faction.Hellevator is { } shaft && shaft.Contains(tile.X, tile.Y) || faction.Settlement?.Contains(tile.X, tile.Y) == true);

	public override void PostUpdateWorld()
	{
		if (Main.GameUpdateCount % SweepIntervalTicks != 0 || placed.Count == 0)
			return;
		uint now = Main.GameUpdateCount;
		foreach (var (tile, at) in placed.ToList())
		{
			if (now - at < RopeLifetimeTicks)
				continue;
			if (!Temporary(tile))
			{
				placed.Remove(tile);
				continue;
			}
			if (Kept(tile))
				continue;
			placed.Remove(tile);
			WorldGen.KillTile(tile.X, tile.Y, noItem: true);
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendTileSquare(-1, tile.X, tile.Y);
		}
	}
}
