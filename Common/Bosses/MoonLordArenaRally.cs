using System.Linq;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class MoonLordArenaRally : ModSystem
{
	const uint RallyTicks = 600;

	public override void PostUpdateNPCs()
	{
		if (!BotWorldSystem.IsRunning || NPC.MoonLordCountdown <= 0 && !NPC.AnyNPCs(NPCID.MoonLordCore))
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (SkyArena.For(faction) is not { IsComplete: true } arena)
				continue;
			var leader = BotWorldSystem.Bots.FirstOrDefault(bot => bot.FactionId == faction.Id && bot.IsAlive);
			if (leader == null)
				continue;
			faction.Rally = (arena.Center, Main.GameUpdateCount + RallyTicks);
			faction.RallyBossType = NPCID.MoonLordCore;
			faction.RallyLeaderId = leader.Id;
			faction.RallyLeaderSeenAt = Main.GameUpdateCount;
			faction.RallyAssembled = true;
		}
	}
}
