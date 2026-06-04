using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class MaterialDrops : GlobalItem
{
	public override void OnSpawn(Item item, IEntitySource source)
	{
		int factor = BotWorldSystem.Settings is { BotsEnabled: true } settings ? settings.MaterialMultiplier : 1;
		if (factor <= 1 || item.maxStack <= 1 || item.IsACoin || ItemID.Sets.BossBag[item.type] || ItemID.Sets.IsAPickup[item.type])
			return;
		if (source is not (EntitySource_TileBreak or EntitySource_Loot or EntitySource_ShakeTree))
			return;
		item.stack = System.Math.Min(item.maxStack, item.stack * factor);
	}
}
