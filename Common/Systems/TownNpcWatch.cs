using System.Collections.Generic;
using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class TownNpcWatch : GlobalNPC
{
	static readonly Dictionary<int, string> lastHitBy = new();

	public override void OnSpawn(NPC npc, IEntitySource source)
	{
		if (!npc.townNPC || !BotWorldSystem.IsRunning)
			return;
		lastHitBy.Remove(npc.whoAmI);
		BotDiagnostics.LogWorld($"town NPC {npc.GivenOrTypeName} ({npc.TypeName}) arrived at tile {npc.Center.ToTileCoordinates()}");
	}

	public override void OnHitNPC(NPC npc, NPC target, NPC.HitInfo hit)
	{
		if (target.townNPC)
			lastHitBy[target.whoAmI] = npc.TypeName;
	}

	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
	{
		if (npc.townNPC)
			lastHitBy[npc.whoAmI] = $"{Lang.GetProjectileName(projectile.type).Value}{(projectile.hostile ? "" : $" of player {projectile.owner}")}";
	}

	public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
	{
		if (npc.townNPC)
			lastHitBy[npc.whoAmI] = $"{item.Name} of {player.name}";
	}

	public override void OnKill(NPC npc)
	{
		if (!npc.townNPC || !BotWorldSystem.IsRunning)
			return;
		BotDiagnostics.LogWorld($"town NPC {npc.GivenOrTypeName} ({npc.TypeName}) died at tile {npc.Center.ToTileCoordinates()}, last hit by {lastHitBy.GetValueOrDefault(npc.whoAmI, "unknown")}");
		lastHitBy.Remove(npc.whoAmI);
	}
}
