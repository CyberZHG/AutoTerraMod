using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
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

	public const int ExploreCell = 40;

	readonly HashSet<int> exploredCells = new();

	static int CellKey(int cellX, int cellY) => cellY * 10000 + cellX;

	public void MarkExplored(Point tile) => exploredCells.Add(CellKey(tile.X / ExploreCell, tile.Y / ExploreCell));

	public bool IsExplored(int cellX, int cellY) => exploredCells.Contains(CellKey(cellX, cellY));

	public int ResourceCount => resourceTiles.Count;
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
		}).ToList(),
		["explored"] = exploredCells.ToArray()
	};

	public void Load(TagCompound tag)
	{
		if (tag.ContainsKey("explored"))
			exploredCells.UnionWith(tag.GetIntArray("explored"));
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

	readonly HashSet<int> abandonedChests = new();

	public void AbandonChest(int chestIndex) => abandonedChests.Add(chestIndex);

	public IEnumerable<int> CollectableChests => knownChests.Where(index =>
		Main.chest[index] is Chest chest && !abandonedChests.Contains(index)
		&& (!lootedChests.Contains(index) || chest.item.All(item => item == null || item.IsAir)));

	readonly Dictionary<int, int> knownCountByType = new();
	readonly List<Point> vanished = new();

	public void Remember(Point tile, int tileType)
	{
		if (resourceTiles.TryGetValue(tile, out int previous))
		{
			if (previous == tileType)
				return;
			Uncount(previous);
		}
		else if (resourceTiles.Count >= Capacity)
			return;
		resourceTiles[tile] = tileType;
		knownCountByType[tileType] = knownCountByType.GetValueOrDefault(tileType) + 1;
	}

	void Uncount(int tileType)
	{
		if (knownCountByType.TryGetValue(tileType, out int count))
			knownCountByType[tileType] = count - 1;
	}

	public int KnownCount(ISet<int> tileTypes) => tileTypes.Sum(type => knownCountByType.GetValueOrDefault(type));

	public bool KnowsAny(ISet<int> tileTypes)
	{
		foreach (int tileType in tileTypes)
		{
			if (knownCountByType.GetValueOrDefault(tileType) > 0)
				return true;
		}
		return false;
	}

	public Point? NearestKnown(Point from, ISet<int> tileTypes, ISet<Point> excluded, int maxDistance)
	{
		if (tileTypes.Contains(TileID.DemonAltar) && World.SharedAltar.Stands && World.SharedAltar.Origin is Point altar && !excluded.Contains(altar))
			Remember(altar, TileID.DemonAltar);
		if (!KnowsAny(tileTypes))
			return null;
		ForgetVanished();
		long nearestDistanceSq = (long)maxDistance * maxDistance;
		Point? nearest = null;
		foreach (var (tile, tileType) in resourceTiles)
		{
			if (!tileTypes.Contains(tileType) || excluded.Contains(tile))
				continue;
			long distanceSq = DistanceSq(from, tile);
			if (distanceSq < nearestDistanceSq || distanceSq == nearestDistanceSq && nearest == null)
			{
				nearestDistanceSq = distanceSq;
				nearest = tile;
			}
		}
		return nearest;
	}

	void ForgetVanished()
	{
		foreach (var (tile, tileType) in resourceTiles)
		{
			var current = Main.tile[tile.X, tile.Y];
			if (!current.HasTile || current.TileType != tileType)
				vanished.Add(tile);
		}
		foreach (var tile in vanished)
		{
			Uncount(resourceTiles[tile]);
			resourceTiles.Remove(tile);
		}
		vanished.Clear();
	}

	static long DistanceSq(Point a, Point b)
	{
		long dx = a.X - b.X;
		long dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}
}
