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
	const int StagingNearest = 20;
	const int StagingFarthest = 60;
	const int StagingRise = 25;
	const uint StagingTicks = 1800;
	const float LeashRadius = 16f * 30f;
	const float ChaseRadius = 16f * 40f;
	const float CloseThreatRadius = 16f * 6f;
	const float HoldRadius = 16f * 4f;
	const float SiegeReach = 16f * 120f;
	const float RetreatHealth = 0.35f;
	const float ResumeHealth = 0.6f;
	const float RetreatReach = 16f * 60f;
	const uint SiegeLogTicks = 600;
	const float ApproachFirstPixels = 16f * 50f;

	static readonly Dictionary<(int pillar, int side), (Vector2 spot, uint at)> staging = new();
	static readonly HashSet<int> retreating = new();

	public static Vector2 StagingFor(NPC pillar, Factions.Faction faction)
	{
		int side = faction.BaseCenter.X >= pillar.Center.X ? 1 : -1;
		if (staging.TryGetValue((pillar.whoAmI, side), out var known) && Main.GameUpdateCount - known.at < StagingTicks)
			return known.spot;
		int pillarX = (int)(pillar.Center.X / 16f), pillarFloor = (int)(pillar.Bottom.Y / 16f);
		Point? ground = null;
		foreach (int sign in new[] { side, -side })
		{
			for (int offset = StagingNearest; offset <= StagingFarthest && ground == null; offset++)
			{
				int x = Utils.Clamp(pillarX + sign * offset, 50, Main.maxTilesX - 50);
				if (FootingNear(x, pillarFloor) is int y)
					ground = new Point(x, y);
			}
			if (ground != null)
				break;
		}
		var fallback = new Point(Utils.Clamp(pillarX + side * StagingTiles, 50, Main.maxTilesX - 50), pillarFloor - 1);
		var spot = (Grounds.StandingSpotNear(ground ?? fallback) ?? ground ?? fallback).ToWorldCoordinates(8f, 0f);
		staging[(pillar.whoAmI, side)] = (spot, Main.GameUpdateCount);
		return spot;
	}

	static int? FootingNear(int x, int floor)
	{
		for (int y = Math.Max(10, floor - StagingRise); y <= Math.Min(Main.maxTilesY - 10, floor + StagingRise); y++)
		{
			if (TileQuery.CanStandOn(x, y + 1) && !TileQuery.IsSolid(x, y) && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x, y - 2) && Main.tile[x, y].LiquidAmount == 0)
				return y;
		}
		return null;
	}

	public static NPC SiegedPillar(Vector2 near) =>
		Bosses.BossRituals.NearestPillar(near) is NPC pillar && Math.Abs(pillar.Center.X - near.X) < SiegeReach ? pillar : null;

	public static void Tick(Bot bot, NPC pillar, Func<Vector2, float, bool> travel)
	{
		var camp = StagingFor(pillar, bot.Faction);
		var avatar = bot.Avatar;
		float health = avatar.statLife / (float)Math.Max(1, avatar.statLifeMax2);
		if (Vector2.Distance(bot.Center, camp) > RetreatReach || health >= ResumeHealth)
			retreating.Remove(bot.Id);
		else if (health < RetreatHealth)
			retreating.Add(bot.Id);
		bot.Watchdog.RecordProgress();
		if ((Main.GameUpdateCount + (uint)bot.Id * 37) % SiegeLogTicks == 0)
			BotDiagnostics.Log(bot, $"siege {pillar.TypeName}: shield={(pillar.dontTakeDamage ? "up" : "down")} life={pillar.life} distance={Vector2.Distance(bot.Center, pillar.Center) / 16f:0} retreating={retreating.Contains(bot.Id)} health={health:0.00} weapon={bot.Hands.BestWeapon()?.Name}");
		if (retreating.Contains(bot.Id))
		{
			if (Engagement.NearestHostile(bot, CloseThreatRadius, true) is NPC close)
				Engagement.Fight(bot, close);
			else
				travel(camp, HoldRadius);
			return;
		}
		if (Vector2.Distance(bot.Center, camp) > ApproachFirstPixels && Vector2.Distance(bot.Center, pillar.Center) > ApproachFirstPixels)
		{
			if (Engagement.NearestHostile(bot, CloseThreatRadius, true) is NPC blocker)
				Engagement.Fight(bot, blocker);
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
