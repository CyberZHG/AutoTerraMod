using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class BossCredit : GlobalNPC
{
	const float CreditShareOfLife = 0.1f;

	static readonly Dictionary<int, Dictionary<int, long>> damageByFight = new();
	static readonly Dictionary<int, (int identity, int factionId)> shots = new();

	public static void Reset()
	{
		damageByFight.Clear();
		shots.Clear();
	}

	public static void Forget(int slot) => damageByFight.Remove(slot);

	public static void RegisterShot(Projectile projectile, Bot shooter) => shots[projectile.whoAmI] = (projectile.identity, shooter.FactionId);

	public static void RecordDamage(NPC target, int factionId, int damage)
	{
		var boss = BossOf(target);
		if (boss == null || damage <= 0)
			return;
		if (!damageByFight.TryGetValue(boss.whoAmI, out var byFaction))
			damageByFight[boss.whoAmI] = byFaction = new Dictionary<int, long>();
		byFaction[factionId] = byFaction.GetValueOrDefault(factionId) + damage;
	}

	public static bool HasDefeated(Faction faction, BossChallenge challenge) =>
		BotWorldSystem.Relation == FactionRelation.Cooperate ? challenge.IsDefeated() : faction.DefeatedBosses.Contains(challenge.BossType);

	public static BossChallenge NextChallengeFor(Faction faction) =>
		BossCatalog.Challenges.FirstOrDefault(challenge => !HasDefeated(faction, challenge));

	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
	{
		if (shots.TryGetValue(projectile.whoAmI, out var shot) && shot.identity == projectile.identity)
			RecordDamage(npc, shot.factionId, damageDone);
	}

	public override void OnKill(NPC npc)
	{
		if (!npc.boss || !damageByFight.Remove(npc.whoAmI, out var byFaction))
			return;
		long total = byFaction.Values.Sum();
		var credited = byFaction
			.Where(entry => entry.Value >= npc.lifeMax * CreditShareOfLife || entry.Value == byFaction.Values.Max())
			.Select(entry => BotWorldSystem.Factions.FirstOrDefault(faction => faction.Id == entry.Key))
			.Where(faction => faction != null)
			.ToList();
		foreach (var faction in credited)
			faction.DefeatedBosses.Add(npc.type);
		string shares = string.Join(", ", byFaction.OrderByDescending(entry => entry.Value)
			.Select(entry => $"{BotWorldSystem.Factions.FirstOrDefault(faction => faction.Id == entry.Key)?.Name} {entry.Value * 100 / System.Math.Max(1, total)}%"));
		BotDiagnostics.LogWorld($"boss {npc.FullName} defeated; damage shares: {shares}; credited: {string.Join(", ", credited.Select(faction => faction.Name))}");
		foreach (var faction in credited)
			Main.NewText(Language.GetTextValue("Mods.AutoTerraMod.UI.BossCredited", faction.Name, npc.FullName, shares), faction.Color);
	}

	static NPC BossOf(NPC npc)
	{
		var root = npc.realLife >= 0 ? Main.npc[npc.realLife] : npc;
		return root.active && root.boss ? root : null;
	}
}
