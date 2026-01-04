using System;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public static class Engagement
{
	const float RangedStandoff = 16f * 14f;
	const float TooCloseForRanged = 16f * 3f;

	public static bool IsHostileTarget(NPC npc) =>
		npc.active
		&& !npc.friendly
		&& !npc.townNPC
		&& npc.lifeMax > 5
		&& !npc.dontTakeDamage
		&& !npc.immortal
		&& !NPCID.Sets.CountsAsCritter[npc.type]
		&& npc.ModNPC is not BotNPC;

	public static NPC NearestHostile(Bot bot, float radius, bool requireSight)
	{
		NPC nearest = null;
		float nearestDistance = radius;
		var body = bot.Npc;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var npc = Main.npc[i];
			if (!IsHostileTarget(npc) || bot.IsIgnoring(npc))
				continue;
			float distance = Vector2.Distance(body.Center, npc.Center);
			if (distance >= nearestDistance)
				continue;
			if (requireSight && !Collision.CanHitLine(body.position, body.width, body.height, npc.position, npc.width, npc.height))
				continue;
			nearest = npc;
			nearestDistance = distance;
		}
		return nearest;
	}

	public static NPC ThreatToTeammates(Bot bot, float teammateRadius, float threatRadius)
	{
		foreach (var mate in BotWorldSystem.Bots)
		{
			if (mate == bot || mate.FactionId != bot.FactionId || !mate.IsAlive)
				continue;
			if (Vector2.Distance(mate.Center, bot.Center) > teammateRadius)
				continue;
			var threat = NearestHostile(mate, threatRadius, false);
			if (threat != null)
				return threat;
		}
		return null;
	}

	public static void Fight(Bot bot, NPC target)
	{
		if (bot.Motor.IsStuck)
		{
			bot.IgnoreTarget(target);
			bot.Motor.Stop();
			return;
		}
		var weapon = bot.Hands.BestWeapon();
		if (weapon == null)
		{
			bot.Motor.MoveTo(target.Center, 24f);
			return;
		}
		var body = bot.Npc;
		float distance = Vector2.Distance(body.Center, target.Center);
		if (BotHands.IsProjectileWeapon(weapon))
		{
			bool clearShot = Collision.CanHitLine(body.position, body.width, body.height, target.position, target.width, target.height);
			if (clearShot && distance < RangedStandoff)
			{
				bot.Motor.Face(target.Center);
				bot.Hands.Fire(target, weapon);
				if (distance < TooCloseForRanged)
					bot.Motor.MoveTo(body.Center + new Vector2(Math.Sign(body.Center.X - target.Center.X) * 96f, 0f), 8f);
				else
					bot.Motor.Stop();
			}
			else
			{
				bot.Motor.MoveTo(target.Center, RangedStandoff * 0.5f);
			}
			return;
		}
		if (bot.Hands.InMeleeReach(target, weapon))
		{
			bot.Motor.Face(target.Center);
			bot.Hands.Strike(target, weapon);
		}
		bot.Motor.MoveTo(target.Center, BotHands.MeleeReach(weapon) * 0.5f);
	}
}
