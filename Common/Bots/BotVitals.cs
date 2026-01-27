using System;
using System.Linq;
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
		FeelWallHorror();
		RefreshVanillaStats();
		BurnInfernoRing();
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

	const float InfernoRadius = 200f;
	const int InfernoDamage = 20;
	const int InfernoDebuffTicks = 120;
	const int InfernoPeriodTicks = 60;

	void BurnInfernoRing()
	{
		if (!Avatar.inferno)
			return;
		bool strike = (Main.GameUpdateCount + bot.Id) % InfernoPeriodTicks == 0;
		foreach (var npc in Main.npc)
		{
			if (!npc.active || npc.friendly || npc.damage <= 0 || npc.dontTakeDamage || npc.buffImmune[BuffID.OnFire3] || Vector2.Distance(Avatar.Center, npc.Center) > InfernoRadius)
				continue;
			if (npc.FindBuffIndex(BuffID.OnFire3) == -1)
				npc.AddBuff(BuffID.OnFire3, InfernoDebuffTicks);
			if (strike)
				npc.SimpleStrikeNPC(InfernoDamage, 0);
		}
	}

	const float HorrorReach = 1920f;

	void FeelWallHorror()
	{
		if (Main.wofNPCIndex < 0 || !Main.npc[Main.wofNPCIndex].active || Avatar.gross)
			return;
		float mouthX = Main.npc[Main.wofNPCIndex].position.X + 40f - (Main.npc[Main.wofNPCIndex].direction > 0 ? 96f : 0f);
		if (Avatar.position.Y > (Main.maxTilesY - 250) * 16f && Math.Abs(Avatar.position.X - mouthX) < HorrorReach)
			Avatar.AddBuff(BuffID.Horrified, 10);
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

	void RecountOwnedProjectiles()
	{
		System.Array.Clear(Avatar.ownedProjectileCounts);
		Avatar.slotsMinions = 0f;
		Avatar.numMinions = 0;
		foreach (var projectile in Main.projectile)
		{
			if (projectile.active && projectile.owner == Avatar.whoAmI)
				Avatar.ownedProjectileCounts[projectile.type]++;
		}
	}

	void RefreshVanillaStats()
	{
		try
		{
			Avatar.ResetEffects();
			RecountOwnedProjectiles();
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
			Hurt(Main.DamageVar(attacker.damage, -Avatar.luck), DirectionAwayFrom(body, attacker.Center), source: attacker.GivenOrTypeName);
			lastAttacker = attacker.whoAmI;
			lastAttackedAt = Main.GameUpdateCount;
			return;
		}
	}

	public void RememberAttacker(NPC attacker)
	{
		lastAttacker = attacker.whoAmI;
		lastAttackedAt = Main.GameUpdateCount;
	}

	void TakeEnemyShot(Projectile projectile)
	{
		var shooter = Bosses.BossCredit.ShooterBot(projectile);
		Hurt(Main.DamageVar(projectile.damage, -Avatar.luck), DirectionAwayFrom(bot.Npc, projectile.Center), source: shooter != null ? $"{shooter.Faction.Name}/{shooter.Name}'s {projectile.Name}" : projectile.Name);
		if (shooter?.Npc is NPC shooterBody)
			RememberAttacker(shooterBody);
		if (projectile.penetrate > 0 && --projectile.penetrate == 0)
			projectile.Kill();
	}

	void TakeProjectileDamage(NPC body)
	{
		var hitbox = body.Hitbox;
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			var projectile = Main.projectile[i];
			if (projectile.active && projectile.friendly && projectile.damage > 0 && Bosses.BossCredit.ShooterFaction(projectile) is int faction
				&& Tasks.Engagement.AtWar(faction, bot.FactionId) && projectile.Colliding(projectile.Hitbox, hitbox))
			{
				TakeEnemyShot(projectile);
				return;
			}
			if (!projectile.active || !projectile.hostile || projectile.damage <= 0)
				continue;
			if (!projectile.Colliding(projectile.Hitbox, hitbox))
				continue;
			Hurt(Main.DamageVar(projectile.damage, -Avatar.luck) * HostileProjectileDamageMultiplier, DirectionAwayFrom(body, projectile.Center), source: projectile.Name);
			return;
		}
	}

	void TakeLavaDamage(NPC body)
	{
		if (!body.lavaWet || Avatar.lavaImmune || Avatar.immune)
			return;
		Hurt(LavaContactDamage, 0, source: "lava");
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
			Hurt((fallen - safeTiles) * FallDamagePerTile, 0, ignoreImmunity: true, source: $"fall of {fallen} tiles");
	}

	bool WalksOnFire => Avatar.fireWalk || Avatar.buffImmune[BuffID.Burning] || Avatar.HasBuff(BuffID.ObsidianSkin);

	void TakeTouchDamage(NPC body)
	{
		if (WalksOnFire && Avatar.HasBuff(BuffID.Burning))
			Avatar.ClearBuff(BuffID.Burning);
		var touched = Collision.HurtTiles(body.position, body.width, body.height, Avatar);
		if (touched.type < 0)
		{
			suffocateDelay = 0;
			return;
		}
		int tileType = touched.type;
		if (TileID.Sets.TouchDamageHot[tileType] && !WalksOnFire)
		{
			Avatar.AddBuff(BuffID.Burning, BurningTicks);
			LastDamageSource = $"burning on {TileID.Search.GetName(tileType)}";
		}
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
			Hurt(Main.DamageVar(damage, -Avatar.luck), 0, source: "hazard tile");
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

	public string LastDamageSource { get; private set; } = "unknown";

	public void Hurt(int damage, int hitDirection, bool ignoreImmunity = false, string source = "unknown")
	{
		if (Avatar.immune && !ignoreImmunity)
			return;
		LastDamageSource = source;
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
			if (!Main.dedServ)
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
			ApplyBiomeZones();
		}
	}

	const int DungeonTileThreshold = 250;
	const double SkyFraction = 0.35;

	void ApplyBiomeZones()
	{
		float tileY = Avatar.Center.Y / 16f;
		Avatar.ZoneCorrupt = scene.EnoughTilesForCorruption;
		Avatar.ZoneCrimson = scene.EnoughTilesForCrimson;
		Avatar.ZoneHallow = scene.EnoughTilesForHallow;
		Avatar.ZoneJungle = scene.EnoughTilesForJungle;
		Avatar.ZoneSnow = scene.EnoughTilesForSnow;
		Avatar.ZoneDesert = scene.EnoughTilesForDesert;
		Avatar.ZoneGlowshroom = scene.EnoughTilesForGlowingMushroom;
		Avatar.ZoneMeteor = scene.EnoughTilesForMeteor;
		var center = Avatar.Center.ToTileCoordinates();
		Avatar.ZoneDungeon = scene.DungeonTileCount >= DungeonTileThreshold && tileY > Main.worldSurface && Main.wallDungeon[Main.tile[center.X, center.Y].WallType];
		Avatar.ZoneUnderworldHeight = tileY > Main.UnderworldLayer;
		Avatar.ZoneRockLayerHeight = tileY <= Main.UnderworldLayer && tileY > Main.rockLayer;
		Avatar.ZoneDirtLayerHeight = tileY <= Main.rockLayer && tileY > Main.worldSurface;
		Avatar.ZoneOverworldHeight = tileY <= Main.worldSurface && tileY > Main.worldSurface * SkyFraction;
		Avatar.ZoneSkyHeight = tileY <= Main.worldSurface * SkyFraction;
		Avatar.ZoneBeach = WorldGen.oceanDepths(center.X, center.Y);
		Avatar.ZoneLihzhardTemple = Main.tile[center.X, center.Y].WallType == WallID.LihzahrdBrickUnsafe;
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
