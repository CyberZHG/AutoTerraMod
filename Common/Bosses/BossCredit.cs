using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class BossCredit : GlobalNPC
{
	const float CreditShare = 0.3f;

	static readonly Dictionary<int, Dictionary<int, long>> damageByFight = new();
	static readonly Dictionary<int, (int identity, int factionId, int botId)> shots = new();
	static readonly Dictionary<int, HashSet<int>> fightersByFight = new();
	static readonly Dictionary<int, Dictionary<(int botId, string source), long>> damageBySource = new();

	public static void Reset()
	{
		damageByFight.Clear();
		fightersByFight.Clear();
		damageBySource.Clear();
		summoners.Clear();
		shots.Clear();
	}

	static readonly Dictionary<int, int> summoners = new();

	public static void RecordSummoner(BossChallenge challenge, Bot summoner)
	{
		var (rawDps, factor, bossLife) = BossReadiness.Expectation(summoner.Faction, challenge, summoner);
		summoners[challenge.BossType] = summoner.FactionId;
		expectations[challenge.BossType] = (summoner.FactionId, rawDps, factor, bossLife, Main.GameUpdateCount);
	}

	static readonly Dictionary<int, (int factionId, float rawDps, float factor, float bossLife, uint since)> expectations = new();

	const float CalibrationAfterSeconds = 30f;
	const float MinCalibration = 0.05f;
	const float MaxCalibration = 1.5f;
	const float CalibrationBlend = 0.6f;

	public static void CheckCalibration(BossChallenge challenge, bool over, bool won = false)
	{
		if (!expectations.TryGetValue(challenge.BossType, out var expected) || FactionById(expected.factionId) is not { } faction)
			return;
		float seconds = (Main.GameUpdateCount - expected.since) / 60f;
		if (seconds < CalibrationAfterSeconds || expected.rawDps <= 0f)
		{
			if (over)
				expectations.Remove(challenge.BossType);
			return;
		}
		long dealt = damageByFight.TryGetValue(challenge.BossType, out var byFaction) ? byFaction.GetValueOrDefault(expected.factionId) : 0;
		float measured = Microsoft.Xna.Framework.MathHelper.Clamp(dealt / seconds / expected.rawDps, MinCalibration, MaxCalibration);
		float old = faction.BossDpsCalibration.GetValueOrDefault(challenge.BossType, 1f);
		float progress = expected.bossLife > 0f ? System.Math.Clamp(dealt / expected.bossLife, 0f, 1f) : 0f;
		float updated = won ? System.Math.Max(Microsoft.Xna.Framework.MathHelper.Lerp(old, measured, CalibrationBlend), BossReadiness.ReadyThreshold(faction, challenge))
			: over ? System.Math.Max(MinCalibration, LossCalibration(faction, challenge, expected.factor) * progress)
			: System.Math.Min(old, measured);
		if (over)
			expectations.Remove(challenge.BossType);
		faction.BossDpsCalibration[challenge.BossType] = updated;
		if (over && !won)
			BossReadiness.RestartPlateau(faction, challenge.BossType);
		BotDiagnostics.LogWorld($"{faction.Name} dealt {dealt / seconds:0} dps to {challenge.Name} ({progress:P0} of its life) against {expected.rawDps * expected.factor:0} expected; damage estimate now x{updated:0.00}");
	}

	static float LossCalibration(Faction faction, BossChallenge challenge, float usedFactor)
	{
		float threshold = BossReadiness.ReadyThreshold(faction, challenge);
		return threshold > 0f ? System.Math.Min(usedFactor, threshold) : usedFactor;
	}

	public static void FloorDefeatedCalibrations()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			foreach (int bossType in faction.BossDpsCalibration.Keys.Where(faction.DefeatedBosses.Contains).ToList())
			{
				if (BossCatalog.ChallengeOf(bossType) is { } challenge)
					faction.BossDpsCalibration[bossType] = System.Math.Max(faction.BossDpsCalibration[bossType], BossReadiness.ReadyThreshold(faction, challenge));
			}
		}
	}

	public static bool ForeignFight(NPC boss, int factionId) =>
		BossCatalog.ChallengeOf(boss.type) is { } challenge && summoners.TryGetValue(challenge.BossType, out int owner) && owner != factionId
		&& (BotWorldSystem.Relation == FactionRelation.War || FactionById(factionId) is not { } faction || HasDefeated(faction, challenge));

	public static bool ForgetIfOver(BossChallenge challenge)
	{
		if (BossCatalog.IsBossAlive(challenge))
			return false;
		CheckCalibration(challenge, over: true);
		summoners.Remove(challenge.BossType);
		damageByFight.Remove(challenge.BossType);
		fightersByFight.Remove(challenge.BossType);
		damageBySource.Remove(challenge.BossType);
		return true;
	}

	public static string DescribeFight(BossChallenge challenge) =>
		damageByFight.TryGetValue(challenge.BossType, out var byFaction)
			? string.Join(", ", byFaction.Select(entry => $"{FactionById(entry.Key)?.Name} {entry.Value}"))
			: "no damage yet";

	public static bool IsEngaged(int factionId, BossChallenge challenge) =>
		damageByFight.TryGetValue(challenge.BossType, out var byFaction) && byFaction.ContainsKey(factionId);

	public static NPC EngagedBoss(Bot bot) =>
		Main.npc.Where(npc => npc.active && !npc.dontTakeDamage && !npc.immortal && BossCatalog.ChallengeOf(npc.type) is BossChallenge challenge && IsEngaged(bot.FactionId, challenge))
			.OrderBy(npc => Microsoft.Xna.Framework.Vector2.Distance(npc.Center, bot.Center))
			.FirstOrDefault();

	public static void RegisterShot(Projectile projectile, Bot shooter) => shots[projectile.whoAmI] = (projectile.identity, shooter.FactionId, shooter.Id);

	public static Bot ShooterBot(Projectile projectile) =>
		shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity ? BotWorldSystem.FindBot(shot.botId) : null;

	public static int? ShooterFaction(Projectile projectile) =>
		shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity ? shot.factionId : null;

	public static string DescribeSources(int bossType) =>
		damageBySource.TryGetValue(bossType, out var bySource)
			? string.Join(", ", bySource.OrderByDescending(entry => entry.Value).Select(entry => $"{BotWorldSystem.FindBot(entry.Key.botId)?.Name}:{entry.Key.source}={entry.Value}"))
			: "none";

	public static void RecordDamage(NPC target, int factionId, int botId, int damage, string source = "melee")
	{
		var challenge = BossCatalog.ChallengeOf(target.type);
		if (challenge == null || damage <= 0)
			return;
		if (!damageBySource.TryGetValue(challenge.BossType, out var bySource))
			damageBySource[challenge.BossType] = bySource = new();
		bySource[(botId, source)] = bySource.GetValueOrDefault((botId, source)) + damage;
		if (!damageByFight.TryGetValue(challenge.BossType, out var byFaction))
			damageByFight[challenge.BossType] = byFaction = new Dictionary<int, long>();
		byFaction[factionId] = byFaction.GetValueOrDefault(factionId) + damage;
		if (!expectations.ContainsKey(challenge.BossType) && BotWorldSystem.FindBot(botId) is { } striker)
		{
			var (rawDps, factor, bossLife) = BossReadiness.Expectation(striker.Faction, challenge, striker);
			expectations[challenge.BossType] = (factionId, rawDps, factor, bossLife, Main.GameUpdateCount);
		}
		if (!fightersByFight.TryGetValue(challenge.BossType, out var fighters))
			fightersByFight[challenge.BossType] = fighters = new HashSet<int>();
		fighters.Add(botId);
	}

	public static bool HasDefeated(Faction faction, BossChallenge challenge) =>
		BotWorldSystem.Relation != FactionRelation.Cooperate ? faction.DefeatedBosses.Contains(challenge.BossType)
		: Main.drunkWorld && challenge.BossType is NPCID.EaterofWorldsHead or NPCID.BrainofCthulhu ? BotWorldSystem.Factions.Any(any => any.DefeatedBosses.Contains(challenge.BossType))
		: challenge.IsDefeated();

	public static IEnumerable<BossChallenge> OpenChallengesFor(Faction faction)
	{
		var open = BossCatalog.Challenges.Where(challenge => BossCatalog.AppliesToThisWorld(challenge) && !HasDefeated(faction, challenge) && challenge.IsUnlocked).ToList();
		var ordered = open.Where(challenge => !Deferred(faction, challenge)).Concat(open.Where(challenge => Deferred(faction, challenge)).OrderBy(challenge => challenge.NightOnly && NightComing ? 0 : 1)).ToList();
		if (ordered.Count > 1 && UnorderedEarly.Contains(ordered[0].BossType)
			&& ordered.FirstOrDefault(challenge => UnorderedEarly.Contains(challenge.BossType) && !Deferred(faction, challenge) && CanStartNow(faction, challenge)) is { } ready && ready != ordered[0])
		{
			ordered.Remove(ready);
			ordered.Insert(0, ready);
		}
		return ordered;
	}

	static readonly HashSet<int> UnorderedEarly = new() { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.SkeletronHead };

	static readonly Dictionary<int, (uint at, bool ready)> skeletronReady = new();
	const uint ReadinessCacheTicks = 300;

	static bool CanStartNow(Faction faction, BossChallenge challenge)
	{
		if (challenge.BossType != NPCID.SkeletronHead)
			return SummonOnHand(faction, challenge) || challenge.Triggers.OfType<SummonItemTrigger>().Any() && Systems.WorkbenchSummons.Enabled.IsMet();
		if (Systems.BotWorldSystem.KnowledgeOf(faction).LandmarkOf(World.Landmark.Dungeon) == null)
			return false;
		if (skeletronReady.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.at < ReadinessCacheTicks)
			return cached.ready;
		bool ready = Systems.BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id && bot.IsAlive).MaxBy(bot => bot.Personality.Combat) is { } summoner
			&& BossReadiness.Evaluate(faction, challenge, summoner).IsReady;
		skeletronReady[faction.Id] = (Main.GameUpdateCount, ready);
		return ready;
	}

	static bool SummonOnHand(Faction faction, BossChallenge challenge) =>
		challenge.Triggers.OfType<SummonItemTrigger>().Any(trigger =>
			Building.BaseStorage.Count(faction, trigger.Item) > 0
			|| Systems.BotWorldSystem.Bots.Any(bot => bot.FactionId == faction.Id && bot.IsAlive && Bots.Inventory.Count(bot.Avatar, trigger.Item) > 0));

	const double DuskLead = 6 * 3600;

	static bool NightComing => !Main.dayTime || Main.time >= Main.dayLength - DuskLead;

	static readonly HashSet<int> OptionalBosses = new() { NPCID.DukeFishron, NPCID.HallowBoss };

	public static bool IsOptional(BossChallenge challenge) => OptionalBosses.Contains(challenge.BossType);

	static bool Deferred(Faction faction, BossChallenge challenge) => IsOptional(challenge) ||
		challenge.BossType == NPCID.SkeletronHead && !NPC.AnyNPCs(NPCID.OldMan)
		&& !BotWorldSystem.Bots.Any(bot => bot.FactionId == faction.Id && Inventory.Count(bot.Avatar, ItemID.ClothierVoodooDoll) > 0);

	public static BossChallenge NextChallengeFor(Faction faction) => OpenChallengesFor(faction).FirstOrDefault();

	public static bool MainLineDone(Faction faction) => OpenChallengesFor(faction).All(IsOptional);

	public static bool AllBossesDone(Faction faction) =>
		BossCatalog.Challenges.Where(BossCatalog.AppliesToThisWorld).All(challenge => HasDefeated(faction, challenge));

	public static int DefeatedCount(Faction faction) =>
		BossCatalog.Challenges.Count(challenge => BossCatalog.AppliesToThisWorld(challenge) && HasDefeated(faction, challenge));

	public override bool PreAI(NPC npc)
	{
		if (npc.type == NPCID.WallofFlesh && npc.localAI[0] == 0f)
		{
			SpawnBehindBots(npc);
			if (HeadlessTestMode.WeakBosses)
				npc.life = npc.lifeMax / 10;
		}
		if (npc.type == NPCID.WallofFlesh && npc.velocity.X == 0f && NearestBot(npc) is { } nearest)
		{
			npc.target = nearest.Avatar.whoAmI;
			npc.velocity.X = nearest.Center.X < npc.Center.X ? -1f : 1f;
		}
		return true;
	}

	public override void PostAI(NPC npc)
	{
		if (npc.type != NPCID.WallofFlesh)
			return;
		KeepWallTarget(npc);
		SweepBots(npc);
	}

	const float WallStripOffset = 40f;
	const float WallStripWidth = 140f;
	const float WallStripBackShift = 96f;
	const float WallGrossDepth = 250;

	static void SweepBots(NPC wall)
	{
		float strip = wall.position.X + WallStripOffset - (wall.direction > 0 ? WallStripBackShift : 0f);
		float centre = wall.position.X + wall.width / 2f;
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (!bot.IsAlive || bot.Npc is not NPC body || body.position.Y < (Main.maxTilesY - WallGrossDepth) * 16f)
				continue;
			bool behind = wall.direction < 0 ? body.Center.X > centre + WallStripOffset : body.Center.X < centre - WallStripOffset;
			if (behind)
			{
				body.position.X = wall.direction > 0 ? strip + WallStripWidth : strip - body.width;
				body.velocity.X = wall.direction * 4f;
				bot.Vitals.Teleported(body);
			}
			if (body.position.X + body.width > strip && body.position.X < strip + WallStripWidth || behind)
				bot.Vitals.Hurt(wall.GetAttackDamage_ScaledByStrength(50f), wall.direction, source: wall.GivenOrTypeName);
		}
	}

	const float WallSpawnClearance = 1200f;

	static void SpawnBehindBots(NPC npc)
	{
		int outward = npc.Center.X / 16f > Main.maxTilesX / 2 ? 1 : -1;
		float x = npc.Center.X;
		var bots = BotWorldSystem.Bots.Where(bot => bot.IsAlive).Select(bot => bot.Center.X).ToList();
		while (bots.Any(botX => botX > x - WallSpawnClearance && botX < x + WallSpawnClearance) && x / 16f > 20 && x / 16f < Main.maxTilesX - 20)
			x += outward * 16f;
		npc.position.X += x - npc.Center.X;
	}

	static Bot NearestBot(NPC npc) =>
		BotWorldSystem.Bots.Where(bot => bot.IsAlive && !bot.Avatar.dead).MinBy(bot => Microsoft.Xna.Framework.Vector2.DistanceSquared(bot.Center, npc.Center));

	static void KeepWallTarget(NPC npc)
	{
		var current = npc.target >= 0 && npc.target < Main.maxPlayers ? Main.player[npc.target] : null;
		if (current != null && current.active && !current.dead)
			return;
		var nearest = NearestBot(npc);
		if (nearest == null)
			return;
		npc.target = nearest.Avatar.whoAmI;
		nearest.Avatar.gross = true;
	}

	public static readonly HashSet<int> DollDroppers = new() { NPCID.AngryBones, NPCID.AngryBonesBig, NPCID.AngryBonesBigMuscle, NPCID.AngryBonesBigHelmet, NPCID.ShortBones };

	public override void OnKill(NPC npc)
	{
		if (DollDroppers.Contains(npc.type) && BotWorldSystem.Bots.FirstOrDefault(bot => bot.Avatar.whoAmI == npc.lastInteraction) is { } killer)
			BotDiagnostics.Log(killer, $"killed a doll dropper {npc.TypeName} at {npc.Center.ToTileCoordinates()}");
		OnBossKill(npc);
	}

	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
	{
		if (shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity)
			RecordDamage(npc, shot.factionId, shot.botId, damageDone, projectile.Name);
	}

	const uint LootWindowTicks = 3 * 3600;
	static (Microsoft.Xna.Framework.Vector2 at, uint tick)? lastBossDeath;

	public static Microsoft.Xna.Framework.Vector2? RecentLootPoint =>
		lastBossDeath is { } death && Main.GameUpdateCount - death.tick < LootWindowTicks ? death.at : null;

	static void OnBossKill(NPC npc)
	{
		if (npc.boss || BossCatalog.ChallengeOf(npc.type) != null || npc.type is NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust)
			lastBossDeath = (npc.Center, Main.GameUpdateCount);
		var challenge = BossCatalog.ChallengeOf(npc.type);
		if (challenge == null || !IsEssential(challenge, npc) || OtherPartsAlive(challenge, npc) || !damageByFight.ContainsKey(challenge.BossType))
			return;
		CheckCalibration(challenge, over: true, won: true);
		damageByFight.Remove(challenge.BossType, out var byFaction);
		if (fightersByFight.Remove(challenge.BossType, out var fighters))
			HandOutBags(challenge, fighters);
		long total = System.Math.Max(1, byFaction.Values.Sum());
		long best = byFaction.Values.Max();
		var credited = byFaction
			.Where(entry => entry.Value >= total * CreditShare || entry.Value == best)
			.Select(entry => FactionById(entry.Key))
			.Where(faction => faction != null)
			.ToList();
		foreach (var faction in credited)
		{
			faction.DefeatedBosses.Add(challenge.BossType);
			faction.RefightWishes.Remove(challenge.BossType);
			faction.PoolUntil = Main.GameUpdateCount + Planning.Champions.AfterBossTicks;
		}
		string shares = string.Join(", ", byFaction.OrderByDescending(entry => entry.Value).Select(entry => $"{FactionById(entry.Key)?.Name} {entry.Value * 100 / total}%"));
		BotDiagnostics.LogWorld($"boss {challenge.Name} defeated; damage shares: {shares}; credited: {string.Join(", ", credited.Select(faction => faction.Name))}");
		foreach (var faction in credited)
			Main.NewText(Language.GetTextValue("Mods.AutoTerraMod.UI.BossCredited", faction.Name, challenge.Name, shares), faction.Color);
	}

	static void HandOutBags(BossChallenge challenge, HashSet<int> fighters)
	{
		if (!Main.expertMode || BossDrops.BagOf(challenge) is not int bag)
			return;
		foreach (int botId in fighters)
		{
			if (BotWorldSystem.FindBot(botId) is not Bot fighter)
				continue;
			var leftover = Inventory.Add(fighter.Avatar, new Item(bag));
			if (!leftover.IsAir)
				Item.NewItem(Entity.GetSource_None(), fighter.Center, bag);
			BotDiagnostics.Log(fighter, $"received {Lang.GetItemNameValue(bag)}");
		}
	}

	static bool OtherPartsAlive(BossChallenge challenge, NPC dying) =>
		Main.npc.Any(other => other.active && other.whoAmI != dying.whoAmI && other.life > 0 && !other.dontTakeDamage && challenge.IsPart(other.type) && IsEssential(challenge, other));

	static bool IsEssential(BossChallenge challenge, NPC part) => part.boss || !ContentSamples.NpcsByNetId[challenge.BossType].boss;

	static Faction FactionById(int id) => BotWorldSystem.Factions.FirstOrDefault(faction => faction.Id == id);
}
