using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class BannerCredit : GlobalNPC
{
	public override bool InstancePerEntity => true;

	int lastBotId = -1;

	public static void RecordHit(NPC target, Bot bot)
	{
		if (target.TryGetGlobalNPC(out BannerCredit credit))
			credit.lastBotId = bot.Id;
	}

	public override void OnSpawn(NPC npc, IEntitySource source) => lastBotId = -1;

	public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
	{
		if (Bosses.BossCredit.ShooterBot(projectile) is Bot shooter)
			lastBotId = shooter.Id;
		else if (projectile.owner == Main.myPlayer)
			lastBotId = -1;
	}

	public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) => lastBotId = -1;

	public static Bot KillerOf(NPC npc) =>
		npc.TryGetGlobalNPC(out BannerCredit credit) && credit.lastBotId >= 0 && BotWorldSystem.FindBot(credit.lastBotId) is { IsAlive: true } bot ? bot : null;
}

public sealed class BannerDelivery : GlobalItem
{
	public override void OnSpawn(Item item, IEntitySource source)
	{
		if (item.createTile != TileID.Banners || source is not EntitySource_Loot { Entity: NPC npc } || BannerCredit.KillerOf(npc) is not Bot killer)
			return;
		item.Center = killer.Npc.Center;
	}
}
