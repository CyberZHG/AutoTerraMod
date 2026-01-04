using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class VanillaHooks : ModSystem
{
	static readonly List<Player> visibleAvatars = new();

	public override void Load()
	{
		On_NPC.AI += LetHostilesTargetBots;
		On_NPC.SpawnNPC += SpawnAroundBots;
		On_NPC.CheckActive += KeepNpcsNearBotsActive;
		On_Main.DrawPlayers_AfterProjectiles += DrawBotAvatars;
	}

	static void LetHostilesTargetBots(On_NPC.orig_AI orig, NPC self)
	{
		if (self.friendly || !BotWorldSystem.IsRunning)
		{
			orig(self);
			return;
		}
		using (Perf.Measure(PerfSection.VanillaHooks))
		using (AvatarPresence.Enter())
			orig(self);
	}

	static void SpawnAroundBots(On_NPC.orig_SpawnNPC orig)
	{
		if (!BotWorldSystem.IsRunning)
		{
			orig();
			return;
		}
		using (Perf.Measure(PerfSection.VanillaHooks))
		using (AvatarPresence.Enter(hideSpectator: true))
			orig();
	}

	static void KeepNpcsNearBotsActive(On_NPC.orig_CheckActive orig, NPC self)
	{
		if (!BotWorldSystem.IsRunning)
		{
			orig(self);
			return;
		}
		using (AvatarPresence.Enter())
			orig(self);
	}

	static void DrawBotAvatars(On_Main.orig_DrawPlayers_AfterProjectiles orig, Main self)
	{
		orig(self);
		if (!BotWorldSystem.IsRunning)
			return;
		using var _ = Perf.Measure(PerfSection.Drawing);
		var view = new Rectangle((int)Main.screenPosition.X - 64, (int)Main.screenPosition.Y - 64, Main.screenWidth + 128, Main.screenHeight + 128);
		visibleAvatars.Clear();
		foreach (var bot in BotWorldSystem.Bots)
		{
			var body = bot.Npc;
			if (body == null || !view.Intersects(body.Hitbox))
				continue;
			BotVitals.SyncAvatar(body, bot.Avatar);
			visibleAvatars.Add(bot.Avatar);
		}
		if (visibleAvatars.Count == 0)
			return;
		using (AvatarPresence.Enter())
			Main.PlayerRenderer.DrawPlayers(Main.Camera, visibleAvatars);
		StabRenderer.Draw(BotWorldSystem.Bots.Where(bot => bot.IsAlive && visibleAvatars.Contains(bot.Avatar)));
	}
}
