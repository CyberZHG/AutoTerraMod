using System;
using System.Collections.Generic;
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
	const float MonsterFireRange = 16f * 32f;
	const float MinionThreat = 16f * 10f;

	const float TooCloseForRanged = 16f * 3f;
	const float BossStandoff = 12f;
	const float JumpStrikeHeight = 16f * 6f;
	const float AirStrikeRange = 16f * 30f;
	const int AirborneClearance = 3;

	static bool Airborne(NPC target)
	{
		var feet = target.Bottom.ToTileCoordinates();
		for (int y = feet.Y; y <= feet.Y + AirborneClearance; y++)
		{
			if (World.TileQuery.CanStandOn(feet.X, y))
				return false;
		}
		return true;
	}
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

	public static NPC NearestHostile(Bot bot, float radius, bool requireSight, bool ignoreBots = false)
	{
		NPC nearest = null;
		float nearestDistance = radius;
		var body = bot.Npc;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (!IsHostileTo(bot, npc) || bot.IsIgnoring(npc) || ignoreBots && npc.ModNPC is BotNPC)
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

	const float MeleeMinionThreat = 16f * 6f;

	public static List<NPC> HostilesIn(Bot bot, Rectangle area)
	{
		var hit = new List<NPC>();
		var body = bot.Npc;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (IsHostileTo(bot, npc) && !bot.IsIgnoring(npc) && npc.Hitbox.Intersects(area)
				&& Collision.CanHitLine(body.position, body.width, body.height, npc.position, npc.width, npc.height))
				hit.Add(npc);
		}
		return hit;
	}

	static NPC NearestMinion(Bot bot, float radius)
	{
		NPC nearest = null;
		float nearestDistance = radius;
		var body = bot.Npc;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (Bosses.BossCatalog.IsBossPart(npc) || !IsHostileTo(bot, npc) || bot.IsIgnoring(npc))
				continue;
			float distance = Vector2.Distance(body.Center, npc.Center);
			if (distance < nearestDistance && Collision.CanHitLine(body.position, body.width, body.height, npc.position, npc.width, npc.height))
			{
				nearest = npc;
				nearestDistance = distance;
			}
		}
		return nearest;
	}

	const float ReflexFireRange = 16f * 12f;
	const float ReflexMeleeSlack = 32f;
	const float CloseReflexRange = 16f * 4f;
	public const float BossTruceRange = 16f * 100f;

	public static (NPC target, Item weapon, bool close)? ReflexTarget(Bot bot)
	{
		if (!bot.Hands.Ready || bot.Npc == null || bot.Hands.BestWeapon() is not Item weapon)
			return null;
		bool ranged = BotHands.IsProjectileWeapon(weapon);
		bool bossNear = Bosses.BossCatalog.NearestActiveBoss(bot.Center, BossTruceRange) != null;
		float reach = ranged ? ReflexFireRange : BotHands.MeleeReach(weapon) + ReflexMeleeSlack;
		var target = NearestHostile(bot, reach, true, bossNear);
		if (target == null)
			return null;
		bool close = !ranged || Vector2.Distance(bot.Center, target.Center) < CloseReflexRange;
		return (target, weapon, close);
	}

	public static void Reflex(Bot bot, NPC target, Item weapon)
	{
		if (!bot.Hands.Ready || !target.active)
			return;
		if (BotHands.IsProjectileWeapon(weapon))
			bot.Hands.Fire(target, weapon);
		else
			bot.Hands.Strike(target, weapon);
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

	const uint FutileChaseTicks = 360;
	const float ChaseProgress = 16f;

	static readonly Dictionary<int, (int target, uint since, float best)> chases = new();

	static bool ChaseIsFutile(Bot bot, NPC target, Item weapon)
	{
		float distance = Vector2.Distance(bot.Center, target.Center);
		bool canHit = weapon != null && (BotHands.IsProjectileWeapon(weapon)
			? Collision.CanHitLine(bot.Npc.position, bot.Npc.width, bot.Npc.height, target.position, target.width, target.height)
			: bot.Hands.InMeleeReach(target, weapon));
		if (canHit || !chases.TryGetValue(bot.Id, out var chase) || chase.target != target.whoAmI || distance < chase.best - ChaseProgress)
		{
			chases[bot.Id] = (target.whoAmI, Main.GameUpdateCount, canHit ? float.MaxValue : distance);
			return false;
		}
		return Main.GameUpdateCount - chase.since > FutileChaseTicks;
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
		if (bot.Motor.IsStalled)
		{
			bot.Motor.Stop();
			if (!Bosses.BossCatalog.IsBossPart(target) && !CanShootFromHere(bot, target))
			{
				bot.IgnoreTarget(target);
				return;
			}
		}
		if (Bosses.BossCatalog.IsBossPart(target) && !CatchesBreath(bot) && bot.Hands.BestWeapon() is Item held
			&& NearestMinion(bot, BotHands.IsProjectileWeapon(held) ? MinionThreat : MeleeMinionThreat) is NPC minion)
			target = minion;
		var approach = Bosses.BossCatalog.IsBossPart(target) ? target.Bottom : target.Center;
		var weapon = bot.Hands.BestWeapon(target);
		if (Bosses.BossCatalog.IsBossPart(target) && Bosses.BossCatalog.ChallengeOf(target.type) is { Arena: World.Landmark arena }
			&& !Bosses.BossChallenge.InArena(bot.Avatar, arena) && bot.Faction.ArenaPoint is Vector2 arenaPoint
			&& Vector2.Distance(bot.Center, arenaPoint) > ArenaReturnRadius * ArenaSettledFactor)
		{
			bot.Motor.Face(target.Center);
			if (weapon != null && !BotHands.IsProjectileWeapon(weapon) && bot.Hands.InMeleeReach(target, weapon))
				bot.Hands.Strike(target, weapon);
			else if (CanShootFromHere(bot, target))
				bot.Hands.Fire(target, weapon);
			bot.Motor.MoveTo(arenaPoint, ArenaReturnRadius);
			return;
		}
		if (Bosses.BossCatalog.IsBossPart(target) && CatchesBreath(bot))
		{
			if (CanShootFromHere(bot, target))
			{
				bot.Motor.Face(target.Center);
				bot.Hands.Fire(target, weapon);
			}
			int away = bot.Center.X >= target.Center.X ? 1 : -1;
			bot.Motor.MoveTo(new Vector2(target.Center.X + away * BreatherDistance, bot.Center.Y), 16f * 3f);
			DodgeCharge(bot, target);
			return;
		}
		if (weapon != null && !BotHands.IsProjectileWeapon(weapon) && bot.Motor.CanFly && Airborne(target)
			&& Vector2.Distance(bot.Center, target.Center) < AirStrikeRange && !Bosses.BossCatalog.IsBossPart(target))
		{
			bot.Motor.AirStrike(target.Center);
			if (bot.Hands.InMeleeReach(target, weapon))
				bot.Hands.Strike(target, weapon);
			return;
		}
		if (!Bosses.BossCatalog.IsBossPart(target) && ChaseIsFutile(bot, target, weapon))
		{
			bot.IgnoreTarget(target);
			bot.Motor.Stop();
			return;
		}
		if (weapon == null)
		{
			bot.Motor.MoveTo(approach, 24f);
			return;
		}
		weapon = PreferredAgainst(bot, target, weapon);
		var body = bot.Npc;
		float distance = Vector2.Distance(body.Center, target.Center);
		if (BotHands.IsProjectileWeapon(weapon))
		{
			bool clearShot = Collision.CanHitLine(body.position, body.width, body.height, target.position, target.width, target.height);
			bool kiting = Bosses.BossCatalog.IsBossPart(target) && bot.Avatar.statLife < bot.Avatar.statLifeMax2 / 2;
			float gap = distance - Math.Max(target.width, target.height) / 2f;
			float tooClose = Bosses.BossCatalog.IsBossPart(target) ? BossRangedGap : TooCloseForRanged;
			if (clearShot && distance < (Bosses.BossCatalog.IsBossPart(target) ? MaxFireRange : MonsterFireRange))
			{
				bot.Motor.Face(target.Center);
				bot.Hands.Fire(target, weapon);
			}
			if (!clearShot || distance >= RangedStandoff)
				bot.Motor.MoveTo(approach, RangedStandoff * 0.5f);
			else if (kiting || gap < tooClose)
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
			bot.Motor.MoveTo(BossStandingSpot(body, target, weapon), StandingTolerance);
			bool overhead = target.Bottom.Y < body.Top.Y && target.Bottom.Y > body.Top.Y - JumpStrikeHeight
				&& Math.Abs(target.Center.X - body.Center.X) < target.width / 2f + BotHands.MeleeReach(weapon);
			if (overhead)
				bot.Motor.RequestJump();
			DodgeCharge(bot, target);
			return;
		}
		KeepMeleeSpacing(bot, target, weapon);
	}

	const float MinMeleeGap = 6f;
	const float ArenaSettledFactor = 1.5f;
	const float RangedThreatDistance = 16f * 10f;

	static Item PreferredAgainst(Bot bot, NPC target, Item weapon)
	{
		if (BotHands.IsProjectileWeapon(weapon) || bot.Personality.Style is not (CombatStyle.Melee or CombatStyle.Summon))
			return weapon;
		bool rangedThreat = target.noGravity || Vector2.Distance(bot.Center, target.Center) > RangedThreatDistance;
		if (!rangedThreat)
			return weapon;
		return bot.Hands.BestMagicWeapon() ?? bot.Hands.BestProjectileWeapon() ?? weapon;
	}
	const float MeleeStandoff = 0.6f;
	const float BackOffStep = 48f;

	static void KeepMeleeSpacing(Bot bot, NPC target, Item weapon)
	{
		var body = bot.Npc;
		float reach = BotHands.MeleeReach(weapon);
		int side = body.Center.X >= target.Center.X ? 1 : -1;
		float gap = Math.Abs(target.Center.X - body.Center.X) - (target.width + body.width) / 2f;
		bool inReach = bot.Hands.InMeleeReach(target, weapon);
		if (inReach && gap < MinMeleeGap)
		{
			bot.Motor.MoveTo(body.Center + new Vector2(side * BackOffStep, 0f), 8f);
			return;
		}
		if (inReach)
		{
			bot.Motor.Stop();
			return;
		}
		var stand = new Vector2(target.Center.X + side * ((target.width + body.width) / 2f + reach * MeleeStandoff), target.Center.Y);
		bot.Motor.MoveTo(stand, reach * 0.3f);
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

	const int BridgeEndMargin = 3;

	static void KiteTo(Bot bot, float x)
	{
		if (bot.Faction.Hellbridge is { } bridge && bot.CenterTile.Y >= Main.UnderworldLayer)
			x = Math.Clamp(x, (bridge.Frontier(-1) + BridgeEndMargin) * 16f, (bridge.Frontier(1) - BridgeEndMargin) * 16f);
		bot.Motor.MoveTo(new Vector2(x, bot.Center.Y), 16f);
	}

	const float HungryThreat = 16f * 12f;
	const float WallKiteMargin = 16f * 32f;
	const float WallSafetyMin = 16f * 4f;
	const float WallRangeSlack = 16f * 3f;

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
		float safe = HungryReach(mouth) + (hurt ? 160f : 0f);
		float keep = safe + WallKiteMargin;
		if (bot.Hands.BestProjectileWeapon() is Item shooter)
			keep = Math.Max(safe + WallSafetyMin, Math.Min(keep, bot.Hands.EffectiveRange(shooter) - WallRangeSlack));
		if (distance < keep)
			KiteTo(bot, mouth.Center.X + away * (keep + 16f * 8f));
		else if (distance > keep + 16f * 16f)
			KiteTo(bot, mouth.Center.X + away * (keep + 16f * 4f));
		else
			bot.Motor.Stop();
		var hungry = Main.npc
			.Where(npc => npc.active && npc.type == NPCID.TheHungry && Vector2.Distance(npc.Center, body.Center) < HungryThreat)
			.OrderBy(npc => Vector2.Distance(npc.Center, body.Center))
			.FirstOrDefault();
		var parts = Main.npc
			.Where(npc => npc.active && npc.type is NPCID.WallofFlesh or NPCID.WallofFleshEye)
			.OrderBy(npc => npc.type == NPCID.WallofFlesh ? 0 : 1)
			.ThenBy(npc => Vector2.Distance(npc.Center, body.Center))
			.ToList();
		var targetPart = hungry ?? parts.FirstOrDefault(npc => Collision.CanHitLine(body.position, body.width, body.height, npc.position, npc.width, npc.height)) ?? parts.FirstOrDefault() ?? part;
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

	const float ArenaReturnRadius = 16f * 6f;
	const float BreatherHealthFraction = 0.4f;
	const float RejoinHealthFraction = 0.7f;
	const float BreatherDistance = 16f * 25f;
	const float BossRangedGap = 16f * 8f;
	const float ReachMargin = 8f;

	static readonly HashSet<int> catchingBreath = new();

	static bool CatchesBreath(Bot bot)
	{
		float health = bot.Avatar.statLife / (float)bot.Avatar.statLifeMax2;
		if (health < BreatherHealthFraction)
			catchingBreath.Add(bot.Id);
		else if (health >= RejoinHealthFraction)
			catchingBreath.Remove(bot.Id);
		return catchingBreath.Contains(bot.Id);
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

	static Vector2 BossStandingSpot(NPC body, NPC boss, Item weapon)
	{
		int side = body.Center.X >= boss.Center.X ? 1 : -1;
		float x = boss.Center.X + side * (boss.width / 2f + Math.Max(body.width / 2f + BossStandoff, BotHands.MeleeReach(weapon) - ReachMargin));
		float y = boss.velocity.Y == 0f ? boss.Bottom.Y - body.height / 2f : body.Center.Y;
		return new Vector2(x, y);
	}
}
