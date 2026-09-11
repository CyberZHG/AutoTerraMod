using System.Linq;
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
			var drawing = pillarsUp ? PillarSpawnDrawers() : null;
			foreach (var bot in BotWorldSystem.Bots)
			{
				bool alive = bot.IsAlive;
				if (pillarsUp && alive && Bosses.BossRituals.InPillarZone(bot.Center) && !drawing.Contains(bot))
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

	const int PillarSpawnDrawerCount = 4;

	static System.Collections.Generic.HashSet<Bots.Bot> PillarSpawnDrawers() =>
		BotWorldSystem.Bots
			.Where(bot => bot.IsAlive && Bosses.BossRituals.InPillarZone(bot.Center) && Bosses.BossRituals.Targets(bot, bot.Center))
			.OrderBy(bot => Bosses.BossRituals.NearestPillar(bot.Center) is Terraria.NPC pillar ? Microsoft.Xna.Framework.Vector2.DistanceSquared(pillar.Center, bot.Center) : float.MaxValue)
			.Take(PillarSpawnDrawerCount)
			.ToHashSet();

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
