using System.Linq;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class SaucerBodyRepair : GlobalNPC
{
	public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => entity.type == NPCID.MartianSaucerCore;

	public override void PostAI(NPC core)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient || core.localAI[3] != 1f || core.ai[0] != 0f || !BotWorldSystem.IsRunning)
			return;
		if (Main.npc.Any(npc => npc.active && npc.type == NPCID.MartianSaucer && (int)npc.ai[0] == core.whoAmI))
			return;
		var turrets = Linked(core, NPCID.MartianSaucerTurret);
		var cannons = Linked(core, NPCID.MartianSaucerCannon);
		int body = NPC.NewNPC(core.GetSource_FromAI(), (int)core.Center.X, (int)core.Center.Y, NPCID.MartianSaucer, core.whoAmI);
		if (body >= Main.maxNPCs)
			return;
		Main.npc[body].ai[0] = core.whoAmI;
		for (int slot = 0; slot < 4; slot++)
		{
			var parts = slot < 2 ? turrets : cannons;
			int index = slot % 2;
			Main.npc[body].localAI[slot] = index < parts.Length ? parts[index] : core.whoAmI;
		}
		Main.npc[body].netUpdate = true;
		Mod.Logger.Info($"martian saucer #{core.whoAmI} had no body (npc slots were full); rebuilt it over {turrets.Length} turrets and {cannons.Length} cannons");
	}

	static int[] Linked(NPC core, int type) =>
		Main.npc.Where(npc => npc.active && npc.type == type && (int)npc.ai[0] == core.whoAmI).Select(npc => npc.whoAmI).Take(2).ToArray();
}
