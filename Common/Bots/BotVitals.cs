using System;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class BotVitals
{
	const int NormalImmunityTicks = 40;
	const int LongImmunityTicks = 80;
	const float HurtKnockbackX = 4.5f;
	const float HurtKnockbackY = -3.5f;
	const int HostileProjectileDamageMultiplier = 2;
	const int SafeFallTiles = 25;
	const int FallDamagePerTile = 10;
	const int LavaContactDamage = 80;
	const int LavaBurnTicks = 420;
	const int BiomeScanInterval = 60;
	const int DrowningDamagePerTick = 2;
	const int BreathRecoveryPerTick = 3;
	const int SuffocationGraceTicks = 5;
	const int BurningTicks = 20;
	const int BaseRespawnTicks = 600;
	const int BossNearbyExtraRespawnTicks = 600;
	const float BossNearbyDistance = 4000f;

	readonly Bot bot;
	readonly SceneMetrics scene = new();
	int biomeScanTimer;
	int fallStartTileY;
	int suffocateDelay;
	bool statsFailureLogged;
	bool biomeFailureLogged;

	public BotVitals(Bot bot)
	{
		this.bot = bot;
		biomeScanTimer = bot.Id % BiomeScanInterval;
	}

	public int PotionSicknessTicks { get; set; }
	public int LastHitDirection { get; private set; } = 1;

	const int AttackerMemoryTicks = 180;

	int lastAttacker = -1;
	uint lastAttackedAt;

	public NPC RecentAttacker =>
		lastAttacker >= 0 && Main.GameUpdateCount - lastAttackedAt < AttackerMemoryTicks && Main.npc[lastAttacker].active ? Main.npc[lastAttacker] : null;
	public float BreathFraction => Avatar.breathMax > 0 ? (float)Avatar.breath / Avatar.breathMax : 1f;

	Player Avatar => bot.Avatar;

	public void Tick(NPC npc)
	{
		SyncAvatar(npc, Avatar);
		RefreshVanillaStats();
		if (PotionSicknessTicks > 0)
			PotionSicknessTicks--;
		TickImmunity();
		if (!Avatar.immune)
			TakeContactDamage(npc);
		if (!Avatar.immune)
			TakeProjectileDamage(npc);
		TakeLavaDamage(npc);
		TakeFallDamage(npc);
		TakeTouchDamage(npc);
		Breathe(npc);
		ScanBiomes();
		MirrorHealthOnBody(npc);
		if (Avatar.statLife <= 0)
			bot.Die();
	}

	public static void SyncAvatar(NPC npc, Player avatar)
	{
		avatar.position = npc.position;
		avatar.oldPosition = npc.oldPosition;
		avatar.velocity = npc.velocity;
		avatar.direction = npc.direction;
		avatar.gfxOffY = npc.gfxOffY;
		avatar.wet = npc.wet;
		avatar.lavaWet = npc.lavaWet;
		avatar.honeyWet = npc.honeyWet;
	}

	const int MinimumRespawnLife = 100;

	public void OnDeath()
	{
		for (int i = 0; i < Player.MaxBuffs; i++)
		{
			if (Avatar.buffType[i] <= 0 || !Main.persistentBuff[Avatar.buffType[i]])
			{
				Avatar.buffTime[i] = 0;
				Avatar.buffType[i] = 0;
			}
		}
		Avatar.statLife = 0;
		PotionSicknessTicks = 0;
		suffocateDelay = 0;
	}

	public void Teleported(NPC npc) => fallStartTileY = (int)(npc.position.Y / 16f);

	public void OnSpawned(NPC npc)
	{
		bot.Watchdog.Reset();
		fallStartTileY = (int)(npc.position.Y / 16f);
		Avatar.immune = true;
		Avatar.immuneTime = LongImmunityTicks;
		RefreshVanillaStats();
		if (Avatar.statLife <= 0)
		{
			Avatar.statLife = System.Math.Max(MinimumRespawnLife, Avatar.statLifeMax2 / 2);
			Avatar.breath = Avatar.breathMax;
		}
	}

	void RefreshVanillaStats()
	{
		try
		{
			Avatar.ResetEffects();
			Avatar.UpdateBuffs(Avatar.whoAmI);
			Avatar.UpdateEquips(Avatar.whoAmI);
			Avatar.UpdateArmorSets(Avatar.whoAmI);
			Avatar.UpdateLifeRegen();
			Avatar.UpdateManaRegen();
		}
		catch (Exception exception)
		{
			if (statsFailureLogged)
				return;
			statsFailureLogged = true;
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"Vanilla stat refresh failed for {bot.Name}", exception);
		}
	}

	void TickImmunity()
	{
		if (!Avatar.immune)
			return;
		if (--Avatar.immuneTime <= 0)
			Avatar.immune = false;
	}

	void TakeContactDamage(NPC body)
	{
		var hitbox = body.Hitbox;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var attacker = Main.npc[i];
			if (!attacker.active || attacker.friendly || attacker.damage <= 0 || attacker.ModNPC is BotNPC)
				continue;
			if (!hitbox.Intersects(attacker.Hitbox))
				continue;
			int cooldownSlot = -1;
			if (!NPCLoader.CanHitPlayer(attacker, Avatar, ref cooldownSlot))
				continue;
			Hurt(Main.DamageVar(attacker.damage, -Avatar.luck), DirectionAwayFrom(body, attacker.Center));
			lastAttacker = attacker.whoAmI;
			lastAttackedAt = Main.GameUpdateCount;
			return;
		}
	}

	void TakeProjectileDamage(NPC body)
	{
		var hitbox = body.Hitbox;
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			var projectile = Main.projectile[i];
			if (!projectile.active || !projectile.hostile || projectile.damage <= 0)
				continue;
			if (!projectile.Colliding(projectile.Hitbox, hitbox))
				continue;
			Hurt(Main.DamageVar(projectile.damage, -Avatar.luck) * HostileProjectileDamageMultiplier, DirectionAwayFrom(body, projectile.Center));
			return;
		}
	}

	void TakeLavaDamage(NPC body)
	{
		if (!body.lavaWet || Avatar.lavaImmune || Avatar.immune)
			return;
		Hurt(LavaContactDamage, 0);
		Avatar.AddBuff(BuffID.OnFire, LavaBurnTicks);
	}

	void TakeFallDamage(NPC body)
	{
		int tileY = (int)(body.position.Y / 16f);
		if (body.velocity.Y < 0f || body.wet || Avatar.noFallDmg)
		{
			fallStartTileY = tileY;
			return;
		}
		if (body.velocity.Y != 0f)
			return;
		int fallen = tileY - fallStartTileY;
		fallStartTileY = tileY;
		int safeTiles = SafeFallTiles + Avatar.extraFall;
		if (fallen > safeTiles && Avatar.equippedWings == null)
			Hurt((fallen - safeTiles) * FallDamagePerTile, 0, ignoreImmunity: true);
	}

	void TakeTouchDamage(NPC body)
	{
		var touched = Collision.HurtTiles(body.position, body.width, body.height, Avatar);
		if (touched.type < 0)
		{
			suffocateDelay = 0;
			return;
		}
		int tileType = touched.type;
		if (TileID.Sets.TouchDamageHot[tileType])
			Avatar.AddBuff(BuffID.Burning, BurningTicks);
		if (TileID.Sets.Suffocate[tileType])
		{
			if (suffocateDelay < SuffocationGraceTicks)
				suffocateDelay++;
			else
				Avatar.AddBuff(BuffID.Suffocation, 1);
		}
		else
		{
			suffocateDelay = 0;
		}
		if (TileID.Sets.TouchDamageBleeding[tileType])
			Avatar.AddBuff(BuffID.Bleeding, Main.rand.Next(240, 600));
		int damage = TileID.Sets.TouchDamageImmediate[tileType];
		if (damage > 0)
			Hurt(Main.DamageVar(damage, -Avatar.luck), 0);
		if (TileID.Sets.TouchDamageDestroyTile[tileType])
			WorldGen.KillTile(touched.x, touched.y);
	}

	void Breathe(NPC body)
	{
		bool submerged = Collision.DrownCollision(body.position, body.width, body.height, 1f) && !Avatar.gills && !Avatar.accMerman;
		if (!submerged)
		{
			Avatar.breath = Math.Min(Avatar.breath + BreathRecoveryPerTick, Avatar.breathMax);
			Avatar.breathCD = 0;
			return;
		}
		if (++Avatar.breathCD < Avatar.breathCDMax)
			return;
		Avatar.breathCD = 0;
		if (--Avatar.breath == 0)
			SoundEngine.PlaySound(SoundID.Drown, body.Center);
		if (Avatar.breath > 0)
			return;
		Avatar.breath = 0;
		Avatar.lifeRegenTime = 0f;
		Avatar.statLife -= DrowningDamagePerTick;
	}

	public void Hurt(int damage, int hitDirection, bool ignoreImmunity = false)
	{
		if (Avatar.immune && !ignoreImmunity)
			return;
		if (hitDirection != 0)
			LastHitDirection = hitDirection;
		var body = bot.Npc;
		var modifiers = new Player.HurtModifiers { HitDirection = hitDirection };
		var info = modifiers.ToHurtInfo(damage, Avatar.statDefense, Avatar.DefenseEffectiveness.Value, HurtKnockbackX, Avatar.noKnockback);
		Avatar.statLife -= info.Damage;
		Avatar.lifeRegenTime = 0f;
		Avatar.immune = true;
		Avatar.immuneTime = Avatar.longInvince ? LongImmunityTicks : NormalImmunityTicks;
		if (!Avatar.noKnockback && hitDirection != 0)
		{
			body.velocity.X = HurtKnockbackX * hitDirection;
			body.velocity.Y = HurtKnockbackY;
		}
		CombatText.NewText(body.Hitbox, CombatText.DamagedFriendly, info.Damage);
		SoundEngine.PlaySound(SoundID.PlayerHit, body.Center);
	}

	static int DirectionAwayFrom(NPC body, Vector2 source) => body.Center.X >= source.X ? 1 : -1;

	void ScanBiomes()
	{
		if (--biomeScanTimer > 0)
			return;
		biomeScanTimer = BiomeScanInterval;
		var sharedScene = Main.SceneMetrics;
		try
		{
			Main.SceneMetrics = scene;
			scene.ScanAndExportToMain(new SceneMetricsScanSettings
			{
				BiomeScanCenterPositionInWorld = Avatar.Center,
				ScanOreFinderData = false
			});
			Avatar.UpdateBiomes();
		}
		catch (Exception exception)
		{
			if (!biomeFailureLogged)
			{
				biomeFailureLogged = true;
				ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"Biome scan failed for {bot.Name}", exception);
			}
		}
		finally
		{
			Main.SceneMetrics = sharedScene;
		}
	}

	void MirrorHealthOnBody(NPC body)
	{
		body.lifeMax = Math.Max(1, Avatar.statLifeMax2);
		body.life = Utils.Clamp(Avatar.statLife, 1, body.lifeMax);
	}

	public static int RespawnDelay(Vector2 deathPosition)
	{
		int ticks = BaseRespawnTicks;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var npc = Main.npc[i];
			bool isBossLike = npc.boss || npc.type is NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail;
			if (npc.active && isBossLike && Math.Abs(deathPosition.X - npc.Center.X) + Math.Abs(deathPosition.Y - npc.Center.Y) < BossNearbyDistance)
			{
				ticks += BossNearbyExtraRespawnTicks;
				break;
			}
		}
		return Main.expertMode ? (int)(ticks * 1.5) : ticks;
	}
}
