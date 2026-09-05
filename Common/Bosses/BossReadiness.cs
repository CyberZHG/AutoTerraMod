using System.Collections.Generic;
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

	readonly record struct Setup(List<Bot> Fighters, float RawDps, float BossLife, float BossDamage, float RequiredRatio);

	static Setup Prepare(Faction faction, BossChallenge challenge, Bot summoner, IEnumerable<Bot> present)
	{
		var fighters = (present ?? BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id)).ToList();
		var sample = ContentSamples.NpcsByNetId[challenge.BossType];
		var mode = Main.GameModeInfo;
		float bossLife = (challenge.TotalLife > 0 ? challenge.TotalLife : sample.lifeMax) * mode.EnemyMaxLifeMultiplier;
		if (Main.expertMode)
			bossLife *= 1f + ExpertScalingPerExtraPlayer * Math.Max(0, fighters.Count - 1);
		float bossDamage = Math.Max(sample.damage, BaseThreatDamage(challenge.BossType)) * mode.EnemyDamageMultiplier * challenge.ThreatMultiplier;
		float requiredRatio = MathHelper.Lerp(CautiousRatio, BraveRatio, summoner.Personality.Courage);
		float burstDps = fighters.Sum(bot => DamagePerSecond(bot, sample.defense)) * challenge.Exposure;
		float expectedSeconds = burstDps > 0 ? Math.Min(bossLife / burstDps, MaxConsideredFightSeconds) : MaxConsideredFightSeconds;
		float rawDps = fighters.Sum(bot => SustainedDamagePerSecond(bot, sample.defense, expectedSeconds)) * challenge.Exposure;
		return new Setup(fighters, rawDps, bossLife, bossDamage, requiredRatio);
	}

	const int MoonLordThreatDamage = 160;
	const int CultistThreatDamage = 100;

	static int BaseThreatDamage(int bossType) => bossType switch
	{
		Terraria.ID.NPCID.MoonLordCore => MoonLordThreatDamage,
		Terraria.ID.NPCID.CultistBoss => CultistThreatDamage,
		_ => 0
	};

	public static ReadinessReport Evaluate(Faction faction, BossChallenge challenge, Bot summoner, IEnumerable<Bot> present = null)
	{
		var setup = Prepare(faction, challenge, summoner, present);
		var raw = Report(setup, 1f);
		var calibrated = Report(setup, Calibration(faction, challenge.BossType));
		if (present == null)
			NotePlateau(faction, challenge.BossType, raw.KillSeconds <= 0 ? 0f : raw.SurviveSeconds / (raw.KillSeconds * raw.RequiredRatio));
		if (present == null && !calibrated.IsReady && raw.IsReady && GearPlateaued(faction, challenge.BossType))
			return raw;
		return calibrated;
	}

	public static (float rawDps, float factor, float bossLife) Expectation(Faction faction, BossChallenge challenge, Bot summoner)
	{
		var setup = Prepare(faction, challenge, summoner, null);
		float calibration = Calibration(faction, challenge.BossType);
		bool usesRaw = !Report(setup, calibration).IsReady && Report(setup, 1f).IsReady && GearPlateaued(faction, challenge.BossType);
		return (setup.RawDps, usesRaw ? 1f : calibration, setup.BossLife);
	}

	public static void RestartPlateau(Faction faction, int bossType)
	{
		faction.GearPlateauBosses.Remove(bossType);
		plateaus.Remove((faction.Id, bossType));
	}

	const int ThresholdSteps = 12;
	const float LowestCalibration = 0.01f;

	public static float ReadyThreshold(Faction faction, BossChallenge challenge)
	{
		if (BotWorldSystem.Bots.FirstOrDefault(bot => bot.FactionId == faction.Id) is not { } member)
			return 0f;
		var setup = Prepare(faction, challenge, member, null);
		if (!Report(setup, 1f).IsReady)
			return 0f;
		float low = LowestCalibration, high = 1f;
		for (int step = 0; step < ThresholdSteps; step++)
		{
			float middle = (low + high) / 2f;
			if (Report(setup, middle).IsReady)
				high = middle;
			else
				low = middle;
		}
		return high;
	}

	static ReadinessReport Report(Setup setup, float calibration) =>
		Report(setup.Fighters, setup.RawDps * calibration, setup.BossLife, setup.BossDamage, setup.RequiredRatio);

	static ReadinessReport Report(List<Bot> fighters, float teamDps, float bossLife, float bossDamage, float requiredRatio)
	{
		float killSeconds = teamDps > 0 ? bossLife / teamDps : float.MaxValue;
		float fightSeconds = Math.Min(killSeconds * requiredRatio, MaxConsideredFightSeconds);
		float surviveSeconds = fighters.Sum(bot => SurvivalSeconds(bot, bossDamage, fightSeconds));
		return new ReadinessReport(fighters.Count, teamDps, bossLife, killSeconds, surviveSeconds, requiredRatio);
	}

	const float UnfoughtPrior = 0.6f;

	public static float Calibration(Faction faction, int bossType)
	{
		if (faction.BossDpsCalibration.TryGetValue(bossType, out float learned))
			return learned;
		if (faction.BossDpsCalibration.Count == 0)
			return UnfoughtPrior;
		return (float)Math.Exp(faction.BossDpsCalibration.Values.Average(value => Math.Log(Math.Max(value, 0.01f))));
	}

	const uint PlateauTicks = 20 * 3600;
	const float PlateauGain = 0.02f;
	static readonly Dictionary<(int faction, int boss), (float best, uint improvedAt)> plateaus = new();

	static void NotePlateau(Faction faction, int bossType, float confidence)
	{
		var key = (faction.Id, bossType);
		if (!plateaus.TryGetValue(key, out var entry) || confidence > entry.best * (1f + PlateauGain))
			plateaus[key] = (Math.Max(confidence, entry.best), Main.GameUpdateCount);
	}

	static bool GearPlateaued(Faction faction, int bossType)
	{
		if (faction.GearPlateauBosses.Contains(bossType))
			return true;
		if (!plateaus.TryGetValue((faction.Id, bossType), out var entry) || Main.GameUpdateCount - entry.improvedAt <= PlateauTicks)
			return false;
		faction.GearPlateauBosses.Add(bossType);
		return true;
	}

	const float ManaRegenShare = 0.08f;

	public static float SustainedDamagePerSecond(Bot bot, int bossDefense, float fightSeconds)
	{
		var weapon = bot.Hands.BestWeapon();
		if (weapon == null)
			return 0f;
		float burst = DamagePerSecond(bot, bossDefense, weapon);
		float usesPerSecond = 60f / Math.Max(1, CombinedHooks.TotalAnimationTime(weapon.useAnimation, bot.Avatar, weapon));
		float lasting = fightSeconds;
		if (BotHands.IsRangedWeapon(weapon))
			lasting = Math.Min(fightSeconds, AmmoFor(bot.Avatar, weapon) / usesPerSecond);
		else if (BotHands.IsMagicWeapon(weapon))
		{
			var avatar = bot.Avatar;
			float cost = Math.Max(1, avatar.GetManaCost(weapon));
			float potions = Inventory.Items(avatar).Where(item => item.healMana > 0).Sum(item => (float)item.healMana * item.stack);
			float mana = avatar.statMana + potions + avatar.statManaMax2 * ManaRegenShare * fightSeconds;
			lasting = Math.Min(fightSeconds, mana / cost / usesPerSecond);
		}
		if (lasting >= fightSeconds)
			return burst;
		var fallback = bot.Hands.BestFallbackWeapon();
		float fallbackDps = fallback == null ? 0f : DamagePerSecond(bot, bossDefense, fallback);
		return (burst * lasting + fallbackDps * (fightSeconds - lasting)) / fightSeconds;
	}

	static float AmmoFor(Player avatar, Item weapon) =>
		Inventory.Items(avatar).Where(item => item.ammo == weapon.useAmmo).Sum(item => item.consumable ? (float)item.stack : float.MaxValue / 4f);

	public static float DamagePerSecond(Bot bot, int bossDefense) => bot.Hands.BestWeapon() is Item weapon ? DamagePerSecond(bot, bossDefense, weapon) : 0f;

	static float DamagePerSecond(Bot bot, int bossDefense, Item weapon)
	{
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
