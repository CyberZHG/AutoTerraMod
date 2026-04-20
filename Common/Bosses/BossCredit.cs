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

	public static void Reset()
	{
		damageByFight.Clear();
		fightersByFight.Clear();
		steeredWalls.Clear();
		summoners.Clear();
		shots.Clear();
	}

	static readonly Dictionary<int, int> summoners = new();

	public static void RecordSummoner(BossChallenge challenge, int factionId) => summoners[challenge.BossType] = factionId;

	public static bool SummonedByOther(NPC boss, int factionId) =>
		BossCatalog.ChallengeOf(boss.type) is { } challenge && summoners.TryGetValue(challenge.BossType, out int owner) && owner != factionId;

	public static void ForgetIfOver(BossChallenge challenge)
	{
		if (!BossCatalog.IsBossAlive(challenge))
		{
			summoners.Remove(challenge.BossType);
			damageByFight.Remove(challenge.BossType);
			fightersByFight.Remove(challenge.BossType);
		}
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

	public static void RecordDamage(NPC target, int factionId, int botId, int damage)
	{
		var challenge = BossCatalog.ChallengeOf(target.type);
		if (challenge == null || damage <= 0)
			return;
		if (!damageByFight.TryGetValue(challenge.BossType, out var byFaction))
			damageByFight[challenge.BossType] = byFaction = new Dictionary<int, long>();
		byFaction[factionId] = byFaction.GetValueOrDefault(factionId) + damage;
		if (!fightersByFight.TryGetValue(challenge.BossType, out var fighters))
			fightersByFight[challenge.BossType] = fighters = new HashSet<int>();
		fighters.Add(botId);
	}

	public static bool HasDefeated(Faction faction, BossChallenge challenge) =>
		BotWorldSystem.Relation == FactionRelation.Cooperate ? challenge.IsDefeated() : faction.DefeatedBosses.Contains(challenge.BossType);

	public static IEnumerable<BossChallenge> OpenChallengesFor(Faction faction) =>
		BossCatalog.Challenges.Where(challenge => BossCatalog.AppliesToThisWorld(challenge) && !HasDefeated(faction, challenge) && challenge.IsUnlocked);

	public static BossChallenge NextChallengeFor(Faction faction) => OpenChallengesFor(faction).FirstOrDefault();

	public static int DefeatedCount(Faction faction) =>
		BossCatalog.Challenges.Count(challenge => BossCatalog.AppliesToThisWorld(challenge) && HasDefeated(faction, challenge));

	static readonly HashSet<int> steeredWalls = new();

	public override void PostAI(NPC npc)
	{
		if (npc.type != NPCID.WallofFlesh)
			return;
		KeepWallTarget(npc);
		steeredWalls.RemoveWhere(index => !Main.npc[index].active || Main.npc[index].type != NPCID.WallofFlesh);
		if (!steeredWalls.Add(npc.whoAmI))
			return;
		if (BossCatalog.ChallengeOf(npc.type) is not { } challenge || !summoners.TryGetValue(challenge.BossType, out int factionId)
			|| FactionById(factionId)?.Hellbridge is not { } bridge)
			return;
		int toward = bridge.CenterX * 16f + 8f < npc.Center.X ? -1 : 1;
		npc.direction = toward;
		npc.velocity.X = toward * System.Math.Max(1f, System.Math.Abs(npc.velocity.X));
	}

	static void KeepWallTarget(NPC npc)
	{
		var current = npc.target >= 0 && npc.target < Main.maxPlayers ? Main.player[npc.target] : null;
		if (current != null && current.active && !current.dead)
			return;
		var nearest = BotWorldSystem.Bots.Where(bot => bot.IsAlive && !bot.Avatar.dead).MinBy(bot => Microsoft.Xna.Framework.Vector2.DistanceSquared(bot.Center, npc.Center));
		if (nearest == null)
			return;
		npc.target = nearest.Avatar.whoAmI;
		nearest.Avatar.gross = true;
	}

	public override void OnKill(NPC npc)
	{
		if (npc.type == NPCID.WallofFlesh)
			steeredWalls.Remove(npc.whoAmI);
		OnBossKill(npc);
	}

	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
	{
		if (shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity)
			RecordDamage(npc, shot.factionId, shot.botId, damageDone);
	}

	const uint LootWindowTicks = 1800;
	static (Microsoft.Xna.Framework.Vector2 at, uint tick)? lastBossDeath;

	public static Microsoft.Xna.Framework.Vector2? RecentLootPoint =>
		lastBossDeath is { } death && Main.GameUpdateCount - death.tick < LootWindowTicks ? death.at : null;

	static void OnBossKill(NPC npc)
	{
		if (npc.boss || BossCatalog.ChallengeOf(npc.type) != null)
			lastBossDeath = (npc.Center, Main.GameUpdateCount);
		var challenge = BossCatalog.ChallengeOf(npc.type);
		if (challenge == null || !IsEssential(challenge, npc) || OtherPartsAlive(challenge, npc) || !damageByFight.Remove(challenge.BossType, out var byFaction))
			return;
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
