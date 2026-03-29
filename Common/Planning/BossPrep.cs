using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Terraria;

namespace AutoTerraMod.Common.Planning;

public static class BossPrep
{
	const uint CacheTicks = 1800;

	static readonly Dictionary<int, (uint at, bool preparing, float medianDps, int bossDefense)> cache = new();

	public static GearTrack? Focus(Bot bot)
	{
		var state = StateOf(bot.Faction);
		if (!state.preparing)
			return null;
		return Bosses.BossReadiness.DamagePerSecond(bot, state.bossDefense) < state.medianDps ? GearTrack.Weapons : GearTrack.Armor;
	}

	static (uint at, bool preparing, float medianDps, int bossDefense) StateOf(Faction faction)
	{
		if (cache.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.at < CacheTicks)
			return cached;
		var members = BotWorldSystem.Bots.Where(member => member.FactionId == faction.Id && member.IsAlive).ToList();
		var challenge = Bosses.BossCredit.NextChallengeFor(faction);
		var summoner = members.MaxBy(member => member.Personality.Combat);
		var state = (Main.GameUpdateCount, false, 0f, 0);
		if (challenge != null && summoner != null && !Bosses.BossReadiness.Evaluate(faction, challenge, summoner).IsReady)
		{
			int defense = Terraria.ID.ContentSamples.NpcsByNetId[challenge.BossType].defense;
			var dps = members.Select(member => Bosses.BossReadiness.DamagePerSecond(member, defense)).OrderBy(value => value).ToList();
			state = (Main.GameUpdateCount, true, dps[dps.Count / 2], defense);
		}
		cache[faction.Id] = state;
		return state;
	}
}
