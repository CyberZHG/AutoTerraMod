using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Planning;

public static class ResourceSources
{
	const float MinimumHuntDropRate = 0.2f;

	static readonly HashSet<int> WoodItems = new()
	{
		ItemID.Wood, ItemID.BorealWood, ItemID.PalmWood, ItemID.RichMahogany, ItemID.Ebonwood, ItemID.Shadewood, ItemID.Pearlwood, ItemID.AshWood
	};

	static readonly Dictionary<int, int> GemTiles = new()
	{
		[ItemID.Amethyst] = TileID.Amethyst,
		[ItemID.Topaz] = TileID.Topaz,
		[ItemID.Sapphire] = TileID.Sapphire,
		[ItemID.Emerald] = TileID.Emerald,
		[ItemID.Ruby] = TileID.Ruby,
		[ItemID.Diamond] = TileID.Diamond
	};

	static readonly TileSource Trees = new(new HashSet<int> { TileID.Trees, TileID.PalmTree }, true);

	static Dictionary<int, TileSource> tileSources;
	static Dictionary<int, NpcSource> npcSources;

	public static void Reset()
	{
		tileSources = null;
		npcSources = null;
	}

	public static ResourceSource For(int itemType)
	{
		if (WoodItems.Contains(itemType))
			return Trees;
		tileSources ??= BuildTileSources();
		if (tileSources.TryGetValue(itemType, out var tileSource))
			return tileSource;
		if (RecipeIndex.IsCraftable(itemType))
			return null;
		npcSources ??= BuildNpcSources();
		return npcSources.TryGetValue(itemType, out var npcSource) ? npcSource : null;
	}

	static Dictionary<int, TileSource> BuildTileSources()
	{
		var tilesByItem = new Dictionary<int, HashSet<int>>();
		void Add(int itemType, int tileType)
		{
			if (!tilesByItem.TryGetValue(itemType, out var tiles))
				tilesByItem[itemType] = tiles = new HashSet<int>();
			tiles.Add(tileType);
		}

		foreach (var item in ContentSamples.ItemsByType.Values)
		{
			int tile = item.createTile;
			if (tile >= 0 && Main.tileSolid[tile] && !Main.tileSolidTop[tile] && !Main.tileFrameImportant[tile] && !TileID.Sets.Grass[tile] && !IsCraftedFromMaterials(item.type))
				Add(item.type, tile);
		}
		foreach (var (item, tile) in GemTiles)
			Add(item, tile);
		Add(ItemID.Cobweb, TileID.Cobweb);
		Add(ItemID.DirtBlock, TileID.Grass);
		Add(ItemID.MudBlock, TileID.JungleGrass);
		return tilesByItem.ToDictionary(entry => entry.Key, entry => new TileSource(entry.Value, false));
	}

	static bool IsCraftedFromMaterials(int itemType) =>
		RecipeIndex.For(itemType).Any(recipe => recipe.requiredItem.Any(ingredient => ingredient.createWall <= WallID.None));

	static Dictionary<int, NpcSource> BuildNpcSources()
	{
		var npcsByItem = new Dictionary<int, HashSet<int>>();
		var drops = new List<DropRateInfo>();
		for (int npcType = 1; npcType < NPCLoader.NPCCount; npcType++)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(npcType, out var sample) || sample.friendly || sample.boss || sample.townNPC)
				continue;
			drops.Clear();
			foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(npcType, false))
				rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
			foreach (var drop in drops.Where(drop => drop.dropRate >= MinimumHuntDropRate && (drop.conditions == null || drop.conditions.Count == 0)))
			{
				if (!npcsByItem.TryGetValue(drop.itemId, out var npcs))
					npcsByItem[drop.itemId] = npcs = new HashSet<int>();
				npcs.Add(npcType);
			}
		}
		return npcsByItem.ToDictionary(entry => entry.Key, entry => new NpcSource(entry.Value));
	}
}
