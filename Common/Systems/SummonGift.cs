using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Systems;

public sealed class SummonGift : ModSystem
{
	const int Amount = 100;
	const int SingleAmount = 3;
	static readonly int[] Gifts = { ItemID.TruffleWorm, ItemID.EmpressButterfly, ItemID.ClothierVoodooDoll };

	readonly HashSet<int> granted = new();

	public override void OnWorldLoad() => granted.Clear();

	public override void SaveWorldData(TagCompound tag) => tag["summonGift"] = granted.ToList();

	public override void LoadWorldData(TagCompound tag)
	{
		granted.Clear();
		if (tag.ContainsKey("summonGift"))
			granted.UnionWith(tag.GetList<int>("summonGift"));
	}

	public override void PostUpdateWorld()
	{
		if (!BotWorldSystem.IsRunning || Main.GameUpdateCount % 300 != 0)
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (granted.Contains(faction.Id) || Building.BaseStorage.ChestIndices(faction).Count == 0)
				continue;
			foreach (int type in Gifts)
				Give(faction, type);
			granted.Add(faction.Id);
			Mod.Logger.Info($"summon gift: {faction.Name} got {Amount} of each of {string.Join(", ", Gifts.Select(Lang.GetItemNameValue))}");
		}
	}

	static void Give(Factions.Faction faction, int type)
	{
		var near = faction.BaseTile.ToWorldCoordinates();
		for (int left = ContentSamples.ItemsByType[type].maxStack == 1 ? SingleAmount : Amount; left > 0;)
		{
			var item = new Item(type);
			item.stack = System.Math.Min(left, item.maxStack);
			left -= item.stack;
			var rest = Building.BaseStorage.ChestWithRoom(faction, near) is { } chest ? Building.BaseStorage.Deposit(chest, item) : item;
			if (!rest.IsAir && rest.stack > 0)
				Item.NewItem(new EntitySource_Misc("AutoTerraMod:SummonGift"), near, type, rest.stack);
		}
	}
}
