using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Planning;

public static class Shops
{
	static Dictionary<int, List<(int npcType, NPCShop.Entry entry)>> sellers;

	public static void Reset() => sellers = null;

	static Dictionary<int, List<(int npcType, NPCShop.Entry entry)>> Sellers()
	{
		if (sellers != null)
			return sellers;
		sellers = new Dictionary<int, List<(int, NPCShop.Entry)>>();
		foreach (var shop in NPCShopDatabase.AllShops.OfType<NPCShop>())
		{
			foreach (var entry in shop.Entries)
			{
				if (!sellers.TryGetValue(entry.Item.type, out var list))
					sellers[entry.Item.type] = list = new List<(int, NPCShop.Entry)>();
				list.Add((shop.NpcType, entry));
			}
		}
		return sellers;
	}

	public static int Price(Item item) => item.shopCustomPrice ?? item.value;

	public static (NPC seller, int price)? Offer(int itemType)
	{
		if (!Sellers().TryGetValue(itemType, out var offers))
			return null;
		foreach (var (npcType, entry) in offers)
		{
			int index = NPC.FindFirstNPC(npcType);
			if (index < 0 || !entry.ConditionsMet())
				continue;
			return (Main.npc[index], Price(entry.Item));
		}
		return null;
	}

	public static bool IsSold(int itemType) => Sellers().ContainsKey(itemType);
}
