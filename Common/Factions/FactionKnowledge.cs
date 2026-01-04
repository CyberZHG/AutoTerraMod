using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Factions;

public sealed class FactionKnowledge
{
	const int Capacity = 4000;

	readonly Dictionary<Point, int> resourceTiles = new();
	readonly HashSet<int> knownChests = new();
	readonly HashSet<int> lootedChests = new();

	public void RememberChest(int chestIndex) => knownChests.Add(chestIndex);

	public void MarkLooted(int chestIndex) => lootedChests.Add(chestIndex);

	public IEnumerable<int> UnlootedChests => knownChests.Where(index => !lootedChests.Contains(index) && Main.chest[index] != null);

	public void Remember(Point tile, int tileType)
	{
		if (resourceTiles.Count < Capacity || resourceTiles.ContainsKey(tile))
			resourceTiles[tile] = tileType;
	}

	public bool KnowsAny(ISet<int> tileTypes) => resourceTiles.Values.Any(tileTypes.Contains);

	public Point? NearestKnown(Point from, ISet<int> tileTypes, ISet<Point> excluded, int maxDistance)
	{
		ForgetVanished();
		long maxDistanceSq = (long)maxDistance * maxDistance;
		return resourceTiles
			.Where(entry => tileTypes.Contains(entry.Value) && !excluded.Contains(entry.Key))
			.Select(entry => (tile: entry.Key, distanceSq: DistanceSq(from, entry.Key)))
			.Where(candidate => candidate.distanceSq <= maxDistanceSq)
			.OrderBy(candidate => candidate.distanceSq)
			.Select(candidate => (Point?)candidate.tile)
			.FirstOrDefault();
	}

	void ForgetVanished()
	{
		var vanished = resourceTiles
			.Where(entry => !Main.tile[entry.Key.X, entry.Key.Y].HasTile || Main.tile[entry.Key.X, entry.Key.Y].TileType != entry.Value)
			.Select(entry => entry.Key)
			.ToList();
		foreach (var tile in vanished)
			resourceTiles.Remove(tile);
	}

	static long DistanceSq(Point a, Point b)
	{
		long dx = a.X - b.X;
		long dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}
}
