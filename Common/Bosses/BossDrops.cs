using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public static class BossDrops
{
	static Dictionary<int, BossChallenge> sourceByItem;

	public static BossChallenge SourceOf(int itemType) => (sourceByItem ??= Build()).GetValueOrDefault(itemType);

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
				map.TryAdd(item, challenge);
			}
		}
		return map;
	}

	static void Report(List<IItemDropRule> rules, List<DropRateInfo> drops)
	{
		foreach (var rule in rules)
			rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
	}
}
