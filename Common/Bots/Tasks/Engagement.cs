using System;
using System.Linq;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public static class Engagement
{
	const float RangedStandoff = 16f * 14f;
	const float MaxFireRange = 16f * 60f;
	const float MinionThreat = 16f * 10f;

	const float TooCloseForRanged = 16f * 3f;
	const float BossStandoff = 12f;
	const float JumpStrikeHeight = 16f * 6f;
	const float StandingTolerance = 8f;

	public static bool IsHostileTarget(NPC npc) =>
		npc.active
		&& !npc.friendly
		&& !npc.townNPC
		&& (npc.lifeMax > 5 || npc.damage > 0)
		&& !npc.dontTakeDamage
		&& !npc.immortal
		&& !NPCID.Sets.CountsAsCritter[npc.type]
		&& npc.ModNPC is not BotNPC;

	public static bool AtWar(int factionA, int factionB) => BotWorldSystem.Relation == Config.FactionRelation.War && factionA != factionB;

	public static Bot EnemyBotOf(Bot bot, NPC npc) =>
		npc.active && npc.ModNPC is BotNPC body && body.Bot is { IsAlive: true } other && AtWar(bot.FactionId, other.FactionId) ? other : null;

	public static bool IsHostileTo(Bot bot, NPC npc) => IsHostileTarget(npc) || EnemyBotOf(bot, npc) != null;

	public static NPC NearestHostile(Bot bot, float radius, bool requireSight)
	{
		NPC nearest = null;
		float nearestDistance = radius;
		var body = bot.Npc;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (!IsHostileTo(bot, npc) || bot.IsIgnoring(npc))
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

	const int WormAiStyle = 6;
	const float WormChaseHeight = 16f * 6f;
	const float WormSearchRadius = 16f * 60f;

	const float SpreadDistance = 16f * 5f;
	const float SpreadStep = 16f * 6f;

	static Bot CrowdingTeammate(Bot bot) =>
		BotWorldSystem.Bots.FirstOrDefault(mate => mate != bot && mate.FactionId == bot.FactionId && mate.IsAlive && Vector2.Distance(mate.Center, bot.Center) < SpreadDistance);

	static int SpreadDirection(Bot bot, Bot mate)
	{
		float apart = bot.Center.X - mate.Center.X;
		return Math.Abs(apart) > 1f ? Math.Sign(apart) : bot.Id > mate.Id ? 1 : -1;
	}

	public static void Fight(Bot bot, NPC target)
	{
		CommandMinions(bot, target);
		if (target.aiStyle == WormAiStyle && Bosses.BossCatalog.IsBossPart(target))
		{
			FightWorm(bot, target);
			return;
		}
		if (target.type is NPCID.WallofFlesh or NPCID.WallofFleshEye)
		{
			FightWall(bot, target);
			return;
		}
		if (bot.Motor.IsStuck)
		{
			bot.Motor.Stop();
			if (!Bosses.BossCatalog.IsBossPart(target) && !CanShootFromHere(bot, target))
			{
				bot.IgnoreTarget(target);
				return;
			}
		}
		if (Bosses.BossCatalog.IsBossPart(target) && bot.Hands.BestWeapon() is Item gun && BotHands.IsProjectileWeapon(gun)
			&& NearestHostile(bot, MinionThreat, true) is NPC minion && !Bosses.BossCatalog.IsBossPart(minion))
			target = minion;
		var approach = Bosses.BossCatalog.IsBossPart(target) ? target.Bottom : target.Center;
		var weapon = bot.Hands.BestWeapon();
		if (weapon == null)
		{
			bot.Motor.MoveTo(approach, 24f);
			return;
		}
		var body = bot.Npc;
		float distance = Vector2.Distance(body.Center, target.Center);
		if (BotHands.IsProjectileWeapon(weapon))
		{
			bool clearShot = Collision.CanHitLine(body.position, body.width, body.height, target.position, target.width, target.height);
			bool kiting = Bosses.BossCatalog.IsBossPart(target) && bot.Avatar.statLife < bot.Avatar.statLifeMax2 / 2;
			if (clearShot && distance < MaxFireRange)
			{
				bot.Motor.Face(target.Center);
				bot.Hands.Fire(target, weapon);
			}
			if (!clearShot || distance >= RangedStandoff)
				bot.Motor.MoveTo(approach, RangedStandoff * 0.5f);
			else if (kiting || distance < TooCloseForRanged)
				bot.Motor.MoveTo(body.Center + new Vector2(Math.Sign(body.Center.X - target.Center.X) * 96f, 0f), 8f);
			else if (Bosses.BossCatalog.IsBossPart(target) && CrowdingTeammate(bot) is Bot mate)
				bot.Motor.MoveTo(body.Center + new Vector2(SpreadDirection(bot, mate) * SpreadStep, 0f), 8f);
			else
				bot.Motor.Stop();
			DodgeCharge(bot, target);
			return;
		}
		if (bot.Hands.InMeleeReach(target, weapon))
		{
			bot.Motor.Face(target.Center);
			bot.Hands.Strike(target, weapon);
		}
		if (Bosses.BossCatalog.IsBossPart(target))
		{
			bot.Motor.MoveTo(BossStandingSpot(body, target), StandingTolerance);
			bool overhead = target.Bottom.Y < body.Top.Y && target.Bottom.Y > body.Top.Y - JumpStrikeHeight
				&& Math.Abs(target.Center.X - body.Center.X) < target.width / 2f + BotHands.MeleeReach(weapon);
			if (overhead)
				bot.Motor.RequestJump();
			return;
		}
		bot.Motor.MoveTo(approach, BotHands.MeleeReach(weapon) * 0.5f);
	}

	static void FightWorm(Bot bot, NPC anyPart)
	{
		var body = bot.Npc;
		var challenge = Bosses.BossCatalog.ChallengeOf(anyPart.type);
		var segment = Main.npc
			.Where(npc => npc.active && challenge.IsPart(npc.type) && Vector2.Distance(npc.Center, body.Center) < WormSearchRadius)
			.OrderBy(npc => Vector2.Distance(npc.Center, body.Center))
			.FirstOrDefault() ?? anyPart;
		if (challenge.Arena is World.Landmark arena && !Bosses.BossChallenge.InArena(bot.Avatar, arena)
			&& World.Grounds.SurfaceSpotNear(Systems.BotWorldSystem.KnowledgeOf(bot.Faction), arena, bot.Faction.BaseTile) is Vector2 spot)
		{
			bot.Motor.MoveTo(spot, 32f);
			return;
		}
		var weapon = bot.Hands.BestWeapon();
		if (weapon == null)
			return;
		bot.Motor.Face(segment.Center);
		if (BotHands.IsProjectileWeapon(weapon))
		{
			if (Collision.CanHitLine(body.position, body.width, body.height, segment.position, segment.width, segment.height))
				bot.Hands.Fire(segment, weapon);
			bot.Motor.Stop();
			return;
		}
		if (bot.Hands.InMeleeReach(segment, weapon))
			bot.Hands.Strike(segment, weapon);
		bool nearSameLevel = Math.Abs(segment.Center.Y - body.Center.Y) < WormChaseHeight;
		if (nearSameLevel)
			bot.Motor.MoveTo(new Vector2(segment.Center.X, body.Center.Y), BotHands.MeleeReach(weapon) * 0.5f);
		else
			bot.Motor.Stop();
	}

	const float HungryThreat = 16f * 12f;

	static float HungryReach(NPC mouth) =>
		mouth.life < mouth.lifeMax * 0.5f ? 700f : mouth.life < mouth.lifeMax * 0.75f ? 500f : 300f;

	public static bool KitingWall(Bot bot) =>
		Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active && bot.CenterTile.Y >= Main.UnderworldLayer;

	static void FightWall(Bot bot, NPC part)
	{
		var body = bot.Npc;
		var mouth = Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active ? Main.npc[Main.wofNPCIndex] : part;
		float offset = body.Center.X - mouth.Center.X;
		int away = offset >= 0f ? 1 : -1;
		float distance = Math.Abs(offset);
		bool hurt = bot.Avatar.statLife < bot.Avatar.statLifeMax2 / 2;
		float reach = HungryReach(mouth) + (hurt ? 160f : 0f);
		if (distance < reach || distance > reach + 16f * 16f)
			bot.Motor.MoveTo(new Vector2(mouth.Center.X + away * (reach + 16f * 6f), body.Center.Y), 16f);
		else
			bot.Motor.Stop();
		var hungry = Main.npc
			.Where(npc => npc.active && npc.type == NPCID.TheHungry && Vector2.Distance(npc.Center, body.Center) < HungryThreat)
			.OrderBy(npc => Vector2.Distance(npc.Center, body.Center))
			.FirstOrDefault();
		var targetPart = hungry ?? Main.npc
			.Where(npc => npc.active && npc.type is NPCID.WallofFlesh or NPCID.WallofFleshEye)
			.OrderBy(npc => Vector2.Distance(npc.Center, body.Center))
			.FirstOrDefault() ?? part;
		bot.Motor.Face(targetPart.Center);
		var ranged = bot.Hands.BestProjectileWeapon();
		if (ranged != null && Collision.CanHitLine(body.position, body.width, body.height, targetPart.position, targetPart.width, targetPart.height))
		{
			bot.Hands.Fire(targetPart, ranged);
			return;
		}
		var melee = bot.Hands.BestWeapon();
		if (melee != null && bot.Hands.InMeleeReach(targetPart, melee))
			bot.Hands.Strike(targetPart, melee);
	}

	const float ChargeSpeed = 9f;
	const float ChargeAlignment = 0.9f;
	const int ChargeWarningTicks = 30;
	const int ChargeFlightTicks = 40;

	static void DodgeCharge(Bot bot, NPC boss)
	{
		if (!Bosses.BossCatalog.IsBossPart(boss) || boss.velocity.Length() < ChargeSpeed)
			return;
		var toBot = bot.Center - boss.Center;
		float distance = toBot.Length();
		if (distance > boss.velocity.Length() * ChargeWarningTicks || Vector2.Dot(Vector2.Normalize(toBot), Vector2.Normalize(boss.velocity)) < ChargeAlignment)
			return;
		if (Math.Abs(boss.velocity.X) > Math.Abs(boss.velocity.Y) && bot.Motor.CanFly)
			bot.Motor.RequestFlight(ChargeFlightTicks);
		else if (Math.Abs(boss.velocity.X) > Math.Abs(boss.velocity.Y))
			bot.Motor.RequestJump();
		else
			bot.Motor.MoveTo(bot.Center + new Vector2((bot.Center.X >= boss.Center.X ? 1 : -1) * 96f, 0f), 8f);
	}

	static bool CanShootFromHere(Bot bot, NPC target)
	{
		var body = bot.Npc;
		return bot.Hands.BestWeapon() is Item weapon && BotHands.IsProjectileWeapon(weapon)
			&& Vector2.Distance(body.Center, target.Center) < MaxFireRange
			&& Collision.CanHitLine(body.position, body.width, body.height, target.position, target.width, target.height);
	}

	static void CommandMinions(Bot bot, NPC target)
	{
		if (bot.Personality.Style == CombatStyle.Summon)
			bot.Avatar.MinionAttackTargetNPC = target.whoAmI;
	}

	static Vector2 BossStandingSpot(NPC body, NPC boss)
	{
		int side = body.Center.X >= boss.Center.X ? 1 : -1;
		float x = boss.Center.X + side * (boss.width / 2f + body.width / 2f + BossStandoff);
		float y = boss.velocity.Y == 0f ? boss.Bottom.Y - body.height / 2f : body.Center.Y;
		return new Vector2(x, y);
	}
}
