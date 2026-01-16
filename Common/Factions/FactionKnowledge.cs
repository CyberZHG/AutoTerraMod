using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Factions;

public sealed class FactionKnowledge
{
	const int Capacity = 4000;

	readonly Dictionary<Point, int> resourceTiles = new();
	readonly HashSet<int> knownChests = new();
	readonly HashSet<int> lootedChests = new();
	readonly Dictionary<Landmark, Point> landmarks = new();
	readonly Dictionary<Landmark, (int minX, int maxX)> landmarkSpans = new();

	public int ScoutDirection { get; set; } = 1;
	public int FruitlessShrineSearches { get; set; }

	public void RememberLandmark(Landmark landmark, Point tile)
	{
		landmarks.TryAdd(landmark, tile);
		if (landmark == Landmark.Ocean && OnOtherHalf(landmarks[landmark], tile))
			return;
		landmarkSpans[landmark] = landmarkSpans.TryGetValue(landmark, out var span)
			? (System.Math.Min(span.minX, tile.X), System.Math.Max(span.maxX, tile.X))
			: (tile.X, tile.X);
	}

	static bool OnOtherHalf(Point anchor, Point tile) => (anchor.X < Main.maxTilesX / 2) != (tile.X < Main.maxTilesX / 2);

	public TagCompound Save() => new()
	{
		["landmarks"] = landmarks.Select(entry => new TagCompound
		{
			["kind"] = (int)entry.Key,
			["x"] = entry.Value.X,
			["y"] = entry.Value.Y,
			["minX"] = landmarkSpans[entry.Key].minX,
			["maxX"] = landmarkSpans[entry.Key].maxX
		}).ToList()
	};

	public void Load(TagCompound tag)
	{
		foreach (var entry in tag.GetList<TagCompound>("landmarks"))
		{
			var kind = (Landmark)entry.GetInt("kind");
			landmarks[kind] = new Point(entry.GetInt("x"), entry.GetInt("y"));
			landmarkSpans[kind] = (entry.GetInt("minX"), entry.GetInt("maxX"));
		}
	}

	public (int minX, int maxX)? SpanOf(Landmark landmark) => landmarkSpans.TryGetValue(landmark, out var span) ? span : null;

	public Point? LandmarkOf(Landmark landmark) => landmarks.TryGetValue(landmark, out var tile) ? tile : null;

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
