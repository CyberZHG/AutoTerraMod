using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Planning;

public static class Shops
{
	static Dictionary<int, List<(int npcType, NPCShop.Entry entry)>> sellers;

	public static void Reset()
	{
		sellers = null;
		shopkeepers = null;
		offers.Clear();
		offersAt = 0;
	}

	static HashSet<int> shopkeepers;

	public static void Warm()
	{
		Sellers();
		shopkeepers ??= NPCShopDatabase.AllShops.OfType<NPCShop>().Select(shop => shop.NpcType).ToHashSet();
	}

	const float SellingReach = 16f * 12f;
	const int SellValueDivisor = 5;

	public static NPC NearbyShopkeeper(Microsoft.Xna.Framework.Vector2 position)
	{
		shopkeepers ??= NPCShopDatabase.AllShops.OfType<NPCShop>().Select(shop => shop.NpcType).ToHashSet();
		return Main.npc.FirstOrDefault(npc => npc.active && npc.townNPC && shopkeepers.Contains(npc.type)
			&& Microsoft.Xna.Framework.Vector2.Distance(npc.Center, position) < SellingReach);
	}

	public static long SellPrice(Item item, int amount) => (long)item.value / SellValueDivisor * amount;

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

	public static (NPC seller, int price)? Offer(Bots.Bot bot, int itemType)
	{
		if (!Sellers().TryGetValue(itemType, out var offers))
			return null;
		foreach (var (npcType, entry) in offers)
		{
			int index = NPC.FindFirstNPC(npcType);
			if (index < 0)
				continue;
			var seller = Main.npc[index];
			using (new ShopperScope(bot.Avatar, seller))
			{
				if (!entry.ConditionsMet())
					continue;
				double adjustment = bot.Avatar.currentShoppingSettings.PriceAdjustment;
				return (seller, (int)System.Math.Round((entry.Item.shopCustomPrice ?? entry.Item.value) * adjustment));
			}
		}
		return null;
	}

	readonly struct ShopperScope : System.IDisposable
	{
		readonly Player shopper;
		readonly int previousLocalPlayer;
		readonly Microsoft.Xna.Framework.Vector2 previousPosition;
		readonly Systems.AvatarPresence presence;

		public ShopperScope(Player shopper, NPC seller)
		{
			this.shopper = shopper;
			previousLocalPlayer = Main.myPlayer;
			previousPosition = shopper.position;
			presence = Systems.AvatarPresence.Enter();
			shopper.Center = seller.Center;
			Main.myPlayer = shopper.whoAmI;
			shopper.currentShoppingSettings = Main.ShopHelper.GetShoppingSettings(shopper, seller);
		}

		public void Dispose()
		{
			Main.myPlayer = previousLocalPlayer;
			shopper.position = previousPosition;
			presence.Dispose();
		}
	}

	public static bool IsSold(int itemType) => Sellers().ContainsKey(itemType);

	const int OfferRefreshTicks = 600;

	static readonly List<(int itemType, int sellerType, int price)> offers = new();
	static uint offersAt;

	public static IReadOnlyList<(int itemType, int sellerType, int price)> CurrentOffers(Bots.Bot shopper)
	{
		if (offersAt != 0 && Main.GameUpdateCount - offersAt < OfferRefreshTicks)
			return offers;
		offersAt = System.Math.Max(1u, Main.GameUpdateCount);
		offers.Clear();
		foreach (var shop in NPCShopDatabase.AllShops.OfType<NPCShop>())
		{
			int index = NPC.FindFirstNPC(shop.NpcType);
			if (index < 0)
				continue;
			var seller = Main.npc[index];
			using var scope = new ShopperScope(shopper.Avatar, seller);
			double adjustment = shopper.Avatar.currentShoppingSettings.PriceAdjustment;
			foreach (var entry in shop.Entries)
			{
				if (entry.Item.type > 0 && entry.ConditionsMet())
					offers.Add((entry.Item.type, shop.NpcType, (int)System.Math.Round((entry.Item.shopCustomPrice ?? entry.Item.value) * adjustment)));
			}
		}
		return offers;
	}
}
