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
		On_Main.UpdateTime_SpawnTownNPCs += LetBotsAttractTownNpcs;
		On_Projectile.Update += UpdateAvatarProjectiles;
		On_Player.KillMe += KeepAvatarsOutOfDeath;
		On_Main.StartInvasion += SizeInvasionForOneFaction;
		On_NPC.NPCLoot += LootBesideNearestBot;
	}

	static void LootBesideNearestBot(On_NPC.orig_NPCLoot orig, NPC self)
	{
		using (AvatarPresence.Enter())
			orig(self);
	}

	static void SizeInvasionForOneFaction(On_Main.orig_StartInvasion orig, int type)
	{
		bool idle = Main.invasionType == 0 || Main.invasionSize == 0;
		orig(type);
		if (!idle || Main.invasionType == 0 || !BotWorldSystem.IsRunning || BotWorldSystem.Bots.Count == 0)
			return;
		int players = Enumerable.Range(0, FirstAvatarSlot).Count(slot => Main.player[slot].active && Main.player[slot].ConsumedLifeCrystals >= 5)
			+ BotWorldSystem.Bots.GroupBy(bot => bot.FactionId).Max(faction => faction.Count());
		int size = type switch
		{
			3 => 120 + 60 * players,
			4 => 160 + 40 * players,
			_ => 80 + 40 * players
		};
		if (size >= Main.invasionSize)
			return;
		Main.invasionSize = Main.invasionSizeStart = Main.invasionProgressMax = size;
	}

	const int FirstAvatarSlot = 200;
	const int MinionKeepAliveTicks = 10;

	static void KeepAvatarsOutOfDeath(On_Player.orig_KillMe orig, Player self, Terraria.DataStructures.PlayerDeathReason damageSource, double dmg, int hitDirection, bool pvp)
	{
		if (self.whoAmI >= FirstAvatarSlot && self.whoAmI < Main.maxPlayers)
			return;
		orig(self, damageSource, dmg, hitDirection, pvp);
	}

	static void UpdateAvatarProjectiles(On_Projectile.orig_Update orig, Projectile self, int i)
	{
		if (Net.BotNet.IsClientAvatar(self.owner))
		{
			var owner = Main.player[self.owner];
			owner.active = true;
			try
			{
				orig(self, i);
				if (self.active && (self.minion || Main.projPet[self.type]))
					self.timeLeft = MinionKeepAliveTicks;
			}
			finally
			{
				owner.active = false;
			}
			return;
		}
		if (!BotWorldSystem.IsRunning || self.owner < FirstAvatarSlot || self.owner >= Main.maxPlayers)
		{
			orig(self, i);
			return;
		}
		int localPlayer = Main.myPlayer;
		using (Perf.Measure(PerfSection.VanillaHooks))
		using (AvatarPresence.Enter())
		{
			Main.myPlayer = self.owner;
			try
			{
				orig(self, i);
				if (self.active && self.minion && !Main.player[self.owner].dead)
					self.timeLeft = MinionKeepAliveTicks;
			}
			finally
			{
				Main.myPlayer = localPlayer;
			}
		}
	}

	static void LetBotsAttractTownNpcs(On_Main.orig_UpdateTime_SpawnTownNPCs orig)
	{
		if (!BotWorldSystem.IsRunning)
		{
			orig();
			return;
		}
		using (AvatarPresence.Enter())
			orig();
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
		using (AvatarPresence.Enter(hideSpectator: true, spawning: true))
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
		if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
		{
			Net.BotNet.DrawClientAvatars();
			return;
		}
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
