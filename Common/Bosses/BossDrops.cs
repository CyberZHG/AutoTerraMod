using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public static class BossDrops
{
	const float ReliableDropRate = 0.5f;

	static Dictionary<int, BossChallenge> sourceByItem;

	public static BossChallenge SourceOf(int itemType) => (sourceByItem ??= Build()).GetValueOrDefault(itemType);

	static Dictionary<int, int> bagByBoss;

	public static int? BagOf(BossChallenge challenge)
	{
		bagByBoss ??= BuildBags();
		return bagByBoss.TryGetValue(challenge.BossType, out int bag) ? bag : null;
	}

	static Dictionary<int, int> BuildBags()
	{
		var map = new Dictionary<int, int>();
		var drops = new List<DropRateInfo>();
		foreach (var challenge in BossCatalog.Challenges)
		{
			drops.Clear();
			Report(Main.ItemDropsDB.GetRulesForNPCID(challenge.BossType, false), drops);
			foreach (int part in challenge.Parts)
				Report(Main.ItemDropsDB.GetRulesForNPCID(part, false), drops);
			if (drops.FirstOrDefault(drop => ItemID.Sets.BossBag[drop.itemId]) is { itemId: > 0 } bag)
				map[challenge.BossType] = bag.itemId;
		}
		return map;
	}

	static Dictionary<int, BossChallenge> Build()
	{
		var map = new Dictionary<int, BossChallenge>();
		var drops = new List<DropRateInfo>();
		foreach (var challenge in BossCatalog.Challenges)
		{
			drops.Clear();
			Report(Main.ItemDropsDB.GetRulesForNPCID(challenge.BossType, false), drops);
			foreach (int part in challenge.Parts)
				Report(Main.ItemDropsDB.GetRulesForNPCID(part, false), drops);
			for (int i = 0; i < drops.Count; i++)
			{
				int item = drops[i].itemId;
				if (ItemID.Sets.BossBag[item])
					Report(Main.ItemDropsDB.GetRulesForItemID(item), drops);
				else if (drops[i].dropRate >= ReliableDropRate && !Planning.RecipeIndex.For(item).Any() && !IsFurniture(item))
					map.TryAdd(item, challenge);
			}
		}
		return map;
	}

	static bool IsFurniture(int itemType) =>
		ContentSamples.ItemsByType[itemType].createTile is int tile && tile >= 0 && Main.tileFrameImportant[tile];

	static void Report(List<IItemDropRule> rules, List<DropRateInfo> drops)
	{
		foreach (var rule in rules)
			rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
	}
}
