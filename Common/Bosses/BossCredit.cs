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

	public static void Reset()
	{
		damageByFight.Clear();
		shots.Clear();
	}

	public static void ForgetIfOver(BossChallenge challenge)
	{
		if (!BossCatalog.IsBossAlive(challenge))
			damageByFight.Remove(challenge.BossType);
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

	public static void RecordDamage(NPC target, int factionId, int damage)
	{
		var challenge = BossCatalog.ChallengeOf(target.type);
		if (challenge == null || damage <= 0)
			return;
		if (!damageByFight.TryGetValue(challenge.BossType, out var byFaction))
			damageByFight[challenge.BossType] = byFaction = new Dictionary<int, long>();
		byFaction[factionId] = byFaction.GetValueOrDefault(factionId) + damage;
	}

	public static bool HasDefeated(Faction faction, BossChallenge challenge) =>
		BotWorldSystem.Relation == FactionRelation.Cooperate ? challenge.IsDefeated() : faction.DefeatedBosses.Contains(challenge.BossType);

	public static IEnumerable<BossChallenge> OpenChallengesFor(Faction faction) =>
		BossCatalog.Challenges.Where(challenge => BossCatalog.AppliesToThisWorld(challenge) && !HasDefeated(faction, challenge) && challenge.IsUnlocked);

	public static BossChallenge NextChallengeFor(Faction faction) => OpenChallengesFor(faction).FirstOrDefault();

	public static int DefeatedCount(Faction faction) =>
		BossCatalog.Challenges.Count(challenge => BossCatalog.AppliesToThisWorld(challenge) && HasDefeated(faction, challenge));

	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
	{
		if (shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity)
			RecordDamage(npc, shot.factionId, damageDone);
	}

	public override void OnKill(NPC npc)
	{
		var challenge = BossCatalog.ChallengeOf(npc.type);
		if (challenge == null || !IsEssential(challenge, npc) || OtherPartsAlive(challenge, npc) || !damageByFight.Remove(challenge.BossType, out var byFaction))
			return;
		long total = System.Math.Max(1, byFaction.Values.Sum());
		long best = byFaction.Values.Max();
		var credited = byFaction
			.Where(entry => entry.Value >= total * CreditShare || entry.Value == best)
			.Select(entry => FactionById(entry.Key))
			.Where(faction => faction != null)
			.ToList();
		foreach (var faction in credited)
			faction.DefeatedBosses.Add(challenge.BossType);
		string shares = string.Join(", ", byFaction.OrderByDescending(entry => entry.Value).Select(entry => $"{FactionById(entry.Key)?.Name} {entry.Value * 100 / total}%"));
		BotDiagnostics.LogWorld($"boss {challenge.Name} defeated; damage shares: {shares}; credited: {string.Join(", ", credited.Select(faction => faction.Name))}");
		foreach (var faction in credited)
			Main.NewText(Language.GetTextValue("Mods.AutoTerraMod.UI.BossCredited", faction.Name, challenge.Name, shares), faction.Color);
	}

	static bool OtherPartsAlive(BossChallenge challenge, NPC dying) =>
		Main.npc.Any(other => other.active && other.whoAmI != dying.whoAmI && other.life > 0 && !other.dontTakeDamage && challenge.IsPart(other.type) && IsEssential(challenge, other));

	static bool IsEssential(BossChallenge challenge, NPC part) => part.boss || !ContentSamples.NpcsByNetId[challenge.BossType].boss;

	static Faction FactionById(int id) => BotWorldSystem.Factions.FirstOrDefault(faction => faction.Id == id);
}
