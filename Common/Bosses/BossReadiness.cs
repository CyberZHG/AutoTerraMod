using System;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public readonly record struct ReadinessReport(int Fighters, float TeamDps, float BossLife, float KillSeconds, float SurviveSeconds, float RequiredRatio)
{
	public bool IsReady => Fighters > 0 && SurviveSeconds >= KillSeconds * RequiredRatio;
	public float Confidence => KillSeconds <= 0 ? 0f : Math.Min(1f, SurviveSeconds / (KillSeconds * RequiredRatio));
}

public static class BossReadiness
{
	const float MeleeHitRate = 0.5f;
	const float RangedHitRate = 0.75f;
	const float BossHitsPerSecond = 0.5f;
	const float CautiousRatio = 2f;
	const float BraveRatio = 1f;
	const float ExpertScalingPerExtraPlayer = 0.35f;
	const float DefenseHalving = 0.5f;
	const float MaxConsideredFightSeconds = 600f;

	public static ReadinessReport Evaluate(Faction faction, BossChallenge challenge, Bot summoner)
	{
		var fighters = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id).ToList();
		var sample = ContentSamples.NpcsByNetId[challenge.BossType];
		var mode = Main.GameModeInfo;
		float bossLife = (challenge.TotalLife > 0 ? challenge.TotalLife : sample.lifeMax) * mode.EnemyMaxLifeMultiplier;
		if (Main.expertMode)
			bossLife *= 1f + ExpertScalingPerExtraPlayer * Math.Max(0, fighters.Count - 1);
		float bossDamage = sample.damage * mode.EnemyDamageMultiplier * challenge.ThreatMultiplier;
		float teamDps = fighters.Sum(bot => DamagePerSecond(bot, sample.defense)) * challenge.Exposure;
		float killSeconds = teamDps > 0 ? bossLife / teamDps : float.MaxValue;
		float requiredRatio = MathHelper.Lerp(CautiousRatio, BraveRatio, summoner.Personality.Courage);
		float fightSeconds = Math.Min(killSeconds * requiredRatio, MaxConsideredFightSeconds);
		float surviveSeconds = fighters.Sum(bot => SurvivalSeconds(bot, bossDamage, fightSeconds));
		return new ReadinessReport(fighters.Count, teamDps, bossLife, killSeconds, surviveSeconds, requiredRatio);
	}

	static float DamagePerSecond(Bot bot, int bossDefense)
	{
		var weapon = bot.Hands.BestWeapon();
		if (weapon == null)
			return 0f;
		var avatar = bot.Avatar;
		float perHit = Math.Max(1f, avatar.GetWeaponDamage(weapon) * (1f + avatar.GetWeaponCrit(weapon) / 100f) - bossDefense * DefenseHalving);
		float hitsPerSecond = 60f / Math.Max(1, CombinedHooks.TotalAnimationTime(weapon.useAnimation, avatar, weapon));
		float hitRate = BotHands.IsProjectileWeapon(weapon) ? RangedHitRate : MeleeHitRate;
		return perHit * hitsPerSecond * hitRate;
	}

	static float SurvivalSeconds(Bot bot, float bossDamage, float fightSeconds)
	{
		var avatar = bot.Avatar;
		float perHit = Math.Max(1f, bossDamage - avatar.statDefense * avatar.DefenseEffectiveness.Value);
		int drinkable = 1 + (int)(fightSeconds * 60f / Math.Max(1, avatar.potionDelayTime));
		var potions = Inventory.Items(avatar).Where(item => item.potion && item.healLife > 0).OrderByDescending(item => item.healLife);
		float healing = potions.SelectMany(item => Enumerable.Repeat(item.healLife, item.stack)).Take(drinkable).Sum();
		return (avatar.statLifeMax2 + healing) / (perHit * BossHitsPerSecond);
	}
}
