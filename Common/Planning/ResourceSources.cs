using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
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

	static readonly TileSource Trees = new(new HashSet<int> { TileID.Trees, TileID.PalmTree, TileID.TreeAsh, TileID.VanityTreeSakura, TileID.VanityTreeYellowWillow, TileID.LivingWood }, true);

	static readonly TileSource Cacti = new(new HashSet<int> { TileID.Cactus }, false, OnSurface: true);

	static readonly HashSet<int> HerbTiles = new() { TileID.MatureHerbs, TileID.BloomingHerbs };

	static readonly Dictionary<int, Landmark> HerbGrounds = new()
	{
		[ItemID.Moonglow] = Landmark.Jungle,
		[ItemID.Shiverthorn] = Landmark.Snow,
		[ItemID.Fireblossom] = Landmark.Underworld
	};

	static readonly Dictionary<int, Landmark> ForageGrounds = new()
	{
		[ItemID.VileMushroom] = Landmark.Corruption,
		[ItemID.ViciousMushroom] = Landmark.Crimson
	};

	static readonly Dictionary<int, (int tileType, Landmark ground)> FurnitureLoot = new()
	{
		[ItemID.Hellforge] = (TileID.Hellforge, Landmark.Underworld)
	};

	static readonly Dictionary<int, Landmark> HuntingGrounds = new()
	{
		[ItemID.RottenChunk] = Landmark.Corruption,
		[ItemID.Vertebrae] = Landmark.Crimson,
		[ItemID.GuideVoodooDoll] = Landmark.Underworld,
		[ItemID.Stinger] = Landmark.Jungle,
		[ItemID.ClothierVoodooDoll] = Landmark.Dungeon,
		[ItemID.LihzahrdPowerCell] = Landmark.Temple
	};

	static readonly Dictionary<int, NpcSource> RareDrops = new()
	{
		[ItemID.ClothierVoodooDoll] = new NpcSource(new HashSet<int> { NPCID.AngryBones, NPCID.AngryBonesBig, NPCID.AngryBonesBigMuscle, NPCID.AngryBonesBigHelmet, NPCID.ShortBones }, Landmark.Dungeon),
		[ItemID.TruffleWorm] = new NpcSource(new HashSet<int> { NPCID.TruffleWorm, NPCID.TruffleWormDigger }) { Critter = true },
		[ItemID.EmpressButterfly] = new NpcSource(new HashSet<int> { NPCID.EmpressButterfly }, Landmark.Hallow) { Critter = true }
	};

	static Dictionary<int, TileSource> tileSources;
	static Dictionary<int, NpcSource> npcSources;

	public static void Warm()
	{
		tileSources ??= BuildTileSources();
		npcSources ??= BuildNpcSources();
	}

	public static void Reset()
	{
		soulSources.Clear();
		tileSources = null;
		npcSources = null;
	}

	public static bool IsWood(int itemType) => WoodItems.Contains(itemType);

	public static ResourceSource For(int itemType)
	{
		if (WoodItems.Contains(itemType))
			return Trees;
		if (itemType == ItemID.Cactus)
			return Cacti;
		if (World.WorldScan.ForageTiles.TryGetValue(itemType, out var forage))
			return ForageSource(itemType, forage.tileType);
		if (World.WorldScan.HerbStyles.TryGetValue(itemType, out int herb))
			return new TileSource(HerbTiles, false, (x, y) => World.WorldScan.IsHerb(x, y, herb), herb is not (2 or 5), HerbGrounds.TryGetValue(itemType, out var herbGround) ? herbGround : null);
		if (RareDrops.TryGetValue(itemType, out var rare))
			return rare;
		if (itemType == ItemID.SoulofFlight)
			return SkySoul;
		if (BiomeSouls.TryGetValue(itemType, out var soul))
			return soulSources.TryGetValue(itemType, out var cachedSoul) ? cachedSoul : soulSources[itemType] = new NpcSource(NpcsLivingIn(soul.bestiaryKeys), soul.ground()) { Underground = true };
		if (FurnitureLoot.TryGetValue(itemType, out var furniture))
			return new TileSource(new HashSet<int> { furniture.tileType }, false, null, false, furniture.ground);
		tileSources ??= BuildTileSources();
		if (tileSources.TryGetValue(itemType, out var tileSource))
			return tileSource;
		if (RecipeIndex.AnyFor(itemType).Count > 0)
			return null;
		npcSources ??= BuildNpcSources();
		if (!npcSources.TryGetValue(itemType, out var npcSource))
			return null;
		return HuntingGrounds.TryGetValue(itemType, out var ground) ? npcSource with { Ground = ground } : npcSource;
	}

	static TileSource ForageSource(int itemType, int tileType) =>
		new(new HashSet<int> { tileType }, false, (x, y) => World.WorldScan.IsForage(x, y, tileType), true, ForageGrounds.TryGetValue(itemType, out var ground) ? ground : null);

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
		return tilesByItem.ToDictionary(entry => entry.Key, entry => new TileSource(entry.Value, false, Ground: TileGrounds.TryGetValue(entry.Key, out var ground) ? ground : null));
	}

	static readonly Dictionary<int, Landmark> TileGrounds = new()
	{
		[ItemID.Hellstone] = Landmark.Underworld,
		[ItemID.AshBlock] = Landmark.Underworld
	};

	static bool IsCraftedFromMaterials(int itemType) =>
		RecipeIndex.AnyFor(itemType).Any(recipe => recipe.requiredItem.Any(ingredient => ingredient.createWall <= WallID.None));

	static Dictionary<int, NpcSource> BuildNpcSources()
	{
		var npcsByItem = new Dictionary<int, Dictionary<int, float>>();
		var drops = new List<DropRateInfo>();
		for (int npcType = 1; npcType < NPCLoader.NPCCount; npcType++)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(npcType, out var sample) || sample.friendly || sample.boss || sample.townNPC || Bosses.BossCatalog.ChallengeOf(npcType) != null)
				continue;
			drops.Clear();
			foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(npcType, false))
				rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
			foreach (var drop in drops.Where(drop => drop.conditions == null || drop.conditions.Count == 0))
			{
				if (!npcsByItem.TryGetValue(drop.itemId, out var npcs))
					npcsByItem[drop.itemId] = npcs = new Dictionary<int, float>();
				npcs[npcType] = System.Math.Max(npcs.GetValueOrDefault(npcType), drop.dropRate);
			}
		}
		return npcsByItem.ToDictionary(entry => entry.Key, entry => SourceFrom(entry.Value));
	}

	static NpcSource SourceFrom(Dictionary<int, float> rates)
	{
		bool common = rates.Values.Any(rate => rate >= MinimumHuntDropRate);
		var hunted = rates.Where(entry => !common || entry.Value >= MinimumHuntDropRate).Select(entry => entry.Key).ToHashSet();
		var ground = hunted.Select(GroundOf).GroupBy(landmark => landmark).OrderByDescending(group => group.Count()).ThenBy(group => group.Key.HasValue).Select(group => group.Key).FirstOrDefault();
		return new NpcSource(hunted, ground) { IsRare = !common };
	}

	static readonly Dictionary<int, (string[] bestiaryKeys, System.Func<Landmark> ground)> BiomeSouls = new()
	{
		[ItemID.SoulofLight] = (new[] { "Bestiary_Biomes.UndergroundHallow" }, () => Landmark.Hallow),
		[ItemID.SoulofNight] = (new[] { "Bestiary_Biomes.UndergroundCorruption", "Bestiary_Biomes.UndergroundCrimson" }, () => Landmarks.WorldEvil)
	};

	static readonly Dictionary<int, NpcSource> soulSources = new();

	static readonly NpcSource SkySoul = new(new HashSet<int> { NPCID.WyvernHead }) { Sky = true };

	static HashSet<int> NpcsLivingIn(string[] bestiaryKeys)
	{
		var npcs = new HashSet<int>();
		for (int npcType = 1; npcType < NPCLoader.NPCCount; npcType++)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(npcType, out var sample) || sample.friendly || sample.townNPC || sample.boss || sample.lifeMax <= 5)
				continue;
			var entry = Main.BestiaryDB.FindEntryByNPCID(npcType);
			if (entry?.Info != null && entry.Info.OfType<Terraria.GameContent.Bestiary.SpawnConditionBestiaryInfoElement>().Any(element => bestiaryKeys.Contains(element.GetDisplayNameKey())))
				npcs.Add(npcType);
		}
		return npcs;
	}

	static readonly Dictionary<string, Landmark> BestiaryGrounds = new()
	{
		["Bestiary_Biomes.TheDungeon"] = Landmark.Dungeon,
		["Bestiary_Biomes.TheCorruption"] = Landmark.Corruption,
		["Bestiary_Biomes.UndergroundCorruption"] = Landmark.Corruption,
		["Bestiary_Biomes.TheCrimson"] = Landmark.Crimson,
		["Bestiary_Biomes.UndergroundCrimson"] = Landmark.Crimson,
		["Bestiary_Biomes.Jungle"] = Landmark.Jungle,
		["Bestiary_Biomes.UndergroundJungle"] = Landmark.Jungle,
		["Bestiary_Biomes.Snow"] = Landmark.Snow,
		["Bestiary_Biomes.UndergroundSnow"] = Landmark.Snow,
		["Bestiary_Biomes.TheUnderworld"] = Landmark.Underworld,
		["Bestiary_Biomes.TheHallow"] = Landmark.Hallow,
		["Bestiary_Biomes.UndergroundHallow"] = Landmark.Hallow,
		["Bestiary_Biomes.Ocean"] = Landmark.Ocean,
		["Bestiary_Biomes.TheTemple"] = Landmark.Temple
	};

	static Landmark? GroundOf(int npcType)
	{
		var entry = Main.BestiaryDB.FindEntryByNPCID(npcType);
		if (entry?.Info == null)
			return null;
		foreach (var element in entry.Info.OfType<Terraria.GameContent.Bestiary.SpawnConditionBestiaryInfoElement>())
		{
			if (BestiaryGrounds.TryGetValue(element.GetDisplayNameKey(), out var ground))
				return ground;
		}
		return null;
	}
}
