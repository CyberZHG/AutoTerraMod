using System;
using Terraria;

namespace AutoTerraMod.Common.Systems;

public readonly struct AvatarPresence : IDisposable
{
	static int depth;
	static bool spectatorHidden;

	readonly bool entered;

	AvatarPresence(bool entered) => this.entered = entered;

	public static AvatarPresence Enter(bool hideSpectator = false, bool spawning = false)
	{
		if (!BotWorldSystem.IsRunning)
			return default;
		if (depth++ == 0)
		{
			bool pillarsUp = spawning && Bosses.BossRituals.PillarsStanding();
			foreach (var bot in BotWorldSystem.Bots)
			{
				bool alive = bot.IsAlive;
				if (pillarsUp && alive && !Bosses.BossRituals.FightsPillars(bot) && Bosses.BossRituals.InPillarZone(bot.Center))
					alive = false;
				bot.Avatar.active = alive;
				bot.Avatar.dead = !alive;
			}
			var spectator = Main.LocalPlayer;
			if (hideSpectator && Spectating.SpectatorSystem.IsSpectating && !spectator.dead)
			{
				spectator.dead = true;
				spectatorHidden = true;
			}
		}
		return new AvatarPresence(true);
	}

	public void Dispose()
	{
		if (!entered || --depth > 0)
			return;
		foreach (var bot in BotWorldSystem.Bots)
			bot.Avatar.active = false;
		if (spectatorHidden)
		{
			Main.LocalPlayer.dead = false;
			spectatorHidden = false;
		}
	}
}
