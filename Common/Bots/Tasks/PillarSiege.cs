using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public static class PillarSiege
{
	const int StagingTiles = 35;
	const uint StagingTicks = 1800;
	const float LeashRadius = 16f * 30f;
	const float ChaseRadius = 16f * 40f;
	const float CloseThreatRadius = 16f * 6f;
	const float HoldRadius = 16f * 4f;
	const float SiegeReach = 16f * 90f;
	const float RetreatHealth = 0.45f;
	const float ResumeHealth = 0.75f;

	static readonly Dictionary<(int pillar, int side), (Vector2 spot, uint at)> staging = new();
	static readonly HashSet<int> retreating = new();

	public static Vector2 StagingFor(NPC pillar, Factions.Faction faction)
	{
		int side = faction.BaseCenter.X >= pillar.Center.X ? 1 : -1;
		if (staging.TryGetValue((pillar.whoAmI, side), out var known) && Main.GameUpdateCount - known.at < StagingTicks)
			return known.spot;
		int x = Utils.Clamp((int)(pillar.Center.X / 16f) + side * StagingTiles, 50, Main.maxTilesX - 50);
		var ground = new Point(x, Math.Max((int)(pillar.Bottom.Y / 16f) - 1, TileQuery.SurfaceY(x) - 1));
		var spot = (Grounds.StandingSpotNear(ground) ?? ground).ToWorldCoordinates(8f, 0f);
		staging[(pillar.whoAmI, side)] = (spot, Main.GameUpdateCount);
		return spot;
	}

	public static NPC SiegedPillar(Vector2 near) =>
		Bosses.BossRituals.NearestPillar(near) is NPC pillar && Vector2.Distance(pillar.Center, near) < SiegeReach ? pillar : null;

	public static void Tick(Bot bot, NPC pillar, Func<Vector2, float, bool> travel)
	{
		var camp = StagingFor(pillar, bot.Faction);
		var avatar = bot.Avatar;
		float health = avatar.statLife / (float)Math.Max(1, avatar.statLifeMax2);
		if (health < RetreatHealth)
			retreating.Add(bot.Id);
		else if (health >= ResumeHealth)
			retreating.Remove(bot.Id);
		bot.Watchdog.RecordProgress();
		if (retreating.Contains(bot.Id))
		{
			if (Engagement.NearestHostile(bot, CloseThreatRadius, true) is NPC close)
				Engagement.Fight(bot, close);
			else
				travel(camp, HoldRadius);
			return;
		}
		if (!pillar.dontTakeDamage)
		{
			Engagement.Fight(bot, pillar);
			return;
		}
		var guard = Main.npc.Where(npc => Engagement.IsHostileTarget(npc) && !bot.IsIgnoring(npc) && !Bosses.BossRituals.Pillars.Contains(npc.type)
				&& Vector2.Distance(npc.Center, camp) < LeashRadius && Vector2.Distance(npc.Center, bot.Center) < ChaseRadius)
			.OrderBy(npc => Vector2.DistanceSquared(npc.Center, bot.Center)).FirstOrDefault();
		if (guard != null)
			Engagement.Fight(bot, guard);
		else
			travel(camp, HoldRadius);
	}
}
