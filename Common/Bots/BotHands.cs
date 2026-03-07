using System;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AutoTerraMod.Common.Bots;

public enum ToolResult
{
	Done,
	Working,
	OutOfReach,
	Unbreakable,
	NoTool
}

public sealed class BotHands
{
	const int WorkingWindowTicks = 30;
	const int ProjectileHitCooldown = 10;

	readonly Bot bot;
	int cooldown;
	uint lastWorkTick;
	bool animationFailureLogged;

	public BotHands(Bot bot) => this.bot = bot;

	Player Avatar => bot.Avatar;

	public bool IsWorking => Main.GameUpdateCount - lastWorkTick < WorkingWindowTicks;
	public uint TicksSinceWork => Main.GameUpdateCount - lastWorkTick;
	public Vector2 AimDirection { get; private set; } = Vector2.UnitX;
	public bool Ready => cooldown <= 0;
	public Item BestPickaxe => Avatar.GetBestPickaxe();
	public Item BestAxe => BestTool(Avatar, AxePower);
	public Item BestHammer => BestTool(Avatar, HammerPower);
	public bool HasAxe => BestAxe != null;

	static readonly System.Func<Item, int> AxePower = item => item.axe;
	static readonly System.Func<Item, int> HammerPower = item => item.hammer;

	static Item BestTool(Player avatar, System.Func<Item, int> power)
	{
		Item best = null;
		foreach (var item in Inventory.Items(avatar))
		{
			if (power(item) > 0 && (best == null || power(item) > power(best)))
				best = item;
		}
		return best;
	}

	public void Tick()
	{
		if (cooldown > 0)
			cooldown--;
		Animate();
	}

	public bool CanReach(Item tool, Point tile)
	{
		var npc = bot.Npc;
		int boost = tool?.tileBoost ?? 0;
		int range = Avatar.blockRange;
		return npc.position.X / 16f - Player.tileRangeX - boost - range <= tile.X
			&& (npc.position.X + npc.width) / 16f + Player.tileRangeX + boost - 1f + range >= tile.X
			&& npc.position.Y / 16f - Player.tileRangeY - boost - range <= tile.Y
			&& (npc.position.Y + npc.height) / 16f + Player.tileRangeY + boost - 2f + range >= tile.Y;
	}

	public ToolResult Dig(Point tile)
	{
		if (!TileQuery.HasTile(tile))
			return ToolResult.Done;
		if (InOtherBase(tile) || IsGrowingSapling(tile))
			return ToolResult.Unbreakable;
		var pickaxe = BestPickaxe;
		if (pickaxe == null)
			return ToolResult.NoTool;
		if (!CanReach(pickaxe, tile))
			return ToolResult.OutOfReach;
		if (!WorldGen.CanKillTile(tile.X, tile.Y) || !Avatar.HasEnoughPickPowerToHurtTile(tile.X, tile.Y))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		Swing(pickaxe, (int)Math.Max(1f, CombinedHooks.TotalUseTime(pickaxe.useTime, Avatar, pickaxe) * Avatar.pickSpeed), tile.ToWorldCoordinates());
		using (Systems.AvatarPresence.Enter())
			Avatar.PickTile(tile.X, tile.Y, pickaxe.pick);
		bot.Watchdog.RecordProgress();
		return ToolResult.Working;
	}

	public ToolResult Chop(Point tile)
	{
		if (!TileQuery.HasTile(tile))
			return ToolResult.Done;
		var axe = BestAxe;
		if (axe == null)
			return ToolResult.NoTool;
		if (!CanReach(axe, tile))
			return ToolResult.OutOfReach;
		if (!WorldGen.CanKillTile(tile.X, tile.Y))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		Swing(axe, CombinedHooks.TotalUseTime(axe.useTime, Avatar, axe), tile.ToWorldCoordinates());
		HitWithAxe(axe, tile);
		if (!Main.tile[tile.X, tile.Y].HasTile)
			bot.Watchdog.RecordProgress();
		return ToolResult.Working;
	}

	public ToolResult HammerWall(Point tile)
	{
		if (Main.tile[tile.X, tile.Y].WallType == WallID.None)
			return ToolResult.Done;
		var hammer = BestHammer;
		if (hammer == null)
			return ToolResult.NoTool;
		if (!CanReach(hammer, tile))
			return ToolResult.OutOfReach;
		if (!Player.CanPlayerSmashWall(tile.X, tile.Y))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		Swing(hammer, CombinedHooks.TotalUseTime(hammer.useTime, Avatar, hammer), tile.ToWorldCoordinates());
		Avatar.PickWall(tile.X, tile.Y, (int)(hammer.hammer * 1.5f));
		return ToolResult.Working;
	}

	public const int AltarHammerPower = 80;

	public static bool CanSmashAltars(Item hammer) => Main.hardMode && hammer.hammer >= AltarHammerPower;

	public ToolResult Smash(Point tile)
	{
		if (!TileQuery.HasTile(tile))
			return ToolResult.Done;
		if (InOtherBase(tile))
			return ToolResult.Unbreakable;
		var hammer = BestHammer;
		if (hammer == null)
			return ToolResult.NoTool;
		if (!CanReach(hammer, tile))
			return ToolResult.OutOfReach;
		var target = Main.tile[tile.X, tile.Y];
		if (!Main.tileHammer[target.TileType] || (target.TileType == TileID.DemonAltar && !CanSmashAltars(hammer)) || !WorldGen.CanKillTile(tile.X, tile.Y))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		Swing(hammer, CombinedHooks.TotalUseTime(hammer.useTime, Avatar, hammer), tile.ToWorldCoordinates());
		int hitId = Avatar.hitTile.HitObject(tile.X, tile.Y, 1);
		using (Systems.AvatarPresence.Enter())
		{
			if (Avatar.hitTile.AddDamage(hitId, hammer.hammer) >= 100)
			{
				Avatar.hitTile.Clear(hitId);
				WorldGen.KillTile(tile.X, tile.Y);
				bot.Watchdog.RecordProgress();
				return ToolResult.Done;
			}
			WorldGen.KillTile(tile.X, tile.Y, fail: true);
		}
		return ToolResult.Working;
	}

	static readonly System.Func<Player, int, int, int, int, Tile, int> VanillaPickaxeDamage =
		(System.Func<Player, int, int, int, int, Tile, int>)System.Delegate.CreateDelegate(
			typeof(System.Func<Player, int, int, int, int, Tile, int>),
			typeof(Player).GetMethod("GetPickaxeDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));

	public ToolResult ReplaceBlock(Item block, Point tile)
	{
		var pickaxe = BestPickaxe;
		if (pickaxe == null || block == null)
			return ToolResult.NoTool;
		if (!CanReach(block, tile) || !CanReach(pickaxe, tile))
			return ToolResult.OutOfReach;
		if (!WorldGen.IsTileReplacable(tile.X, tile.Y) || !WorldGen.WouldTileReplacementWork((ushort)block.createTile, tile.X, tile.Y))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		int hitIndex = Avatar.hitReplace.HitObject(tile.X, tile.Y, 1);
		int damage = VanillaPickaxeDamage(Avatar, tile.X, tile.Y, pickaxe.pick, hitIndex, Main.tile[tile.X, tile.Y]);
		if (damage == 0)
			return ToolResult.Unbreakable;
		Swing(block, (int)Math.Max(1f, CombinedHooks.TotalUseTime(pickaxe.useTime, Avatar, pickaxe) * Avatar.pickSpeed), tile.ToWorldCoordinates());
		if (Avatar.hitReplace.AddDamage(hitIndex, damage) < 100)
		{
			WorldGen.KillTile(tile.X, tile.Y, fail: true);
			return ToolResult.Working;
		}
		Avatar.hitReplace.TryClearingAndPruning(tile.X, tile.Y, 1);
		if (WorldGen.ReplaceTile(tile.X, tile.Y, (ushort)block.createTile, block.placeStyle))
		{
			Inventory.ConsumeOne(Avatar, block);
			bot.Watchdog.RecordProgress();
		}
		return ToolResult.Working;
	}

	public bool ReplaceWall(Item wall, Point tile)
	{
		if (wall == null || wall.createWall <= WallID.None || !Ready || !CanReach(wall, tile))
			return false;
		if (!WorldGen.NearFriendlyWall(tile.X, tile.Y) || !WorldGen.ReplaceWall(tile.X, tile.Y, (ushort)wall.createWall))
			return false;
		Swing(wall, CombinedHooks.TotalUseTime(wall.useTime, Avatar, wall), tile.ToWorldCoordinates());
		Inventory.ConsumeOne(Avatar, wall);
		bot.Watchdog.RecordProgress();
		return true;
	}

	public static bool IsGrowingSapling(Point tile) =>
		TileID.Sets.TreeSapling[Math.Max(0, TileQuery.TypeAt(tile))] && TileQuery.HasTile(tile)
		&& !Systems.BotWorldSystem.Factions.Any(faction => faction.Settlement?.Contains(tile.X, tile.Y) == true);

	public ToolResult Cut(Point tile)
	{
		if (!TileQuery.HasTile(tile))
			return ToolResult.Done;
		if (InOtherBase(tile) || IsGrowingSapling(tile))
			return ToolResult.Unbreakable;
		var tool = BestWeapon() ?? BestPickaxe;
		if (tool == null)
			return ToolResult.NoTool;
		if (!CanReach(tool, tile))
			return ToolResult.OutOfReach;
		if (!WorldGen.CanCutTile(tile.X, tile.Y, TileCuttingContext.AttackMelee))
			return ToolResult.Unbreakable;
		if (!Ready)
			return ToolResult.Working;
		Swing(tool, CombinedHooks.TotalAnimationTime(tool.useAnimation, Avatar, tool), tile.ToWorldCoordinates());
		using (Systems.AvatarPresence.Enter())
			WorldGen.KillTile(tile.X, tile.Y);
		bot.Watchdog.RecordProgress();
		return ToolResult.Working;
	}

	void HitWithAxe(Item axe, Point tile)
	{
		int hitIndex = Avatar.hitTile.HitObject(tile.X, tile.Y, 1);
		int damage = AxeDamage(axe, Main.tile[tile.X, tile.Y].TileType);
		if (Avatar.hitTile.AddDamage(hitIndex, damage) >= 100)
		{
			Avatar.hitTile.TryClearingAndPruning(tile.X, tile.Y, 1);
			WorldGen.KillTile(tile.X, tile.Y);
		}
		else
		{
			WorldGen.KillTile(tile.X, tile.Y, fail: true);
		}
		if (damage != 0)
			Avatar.hitTile.Prune();
	}

	static int AxeDamage(Item axe, int tileType)
	{
		var modTile = TileLoader.GetTile(tileType);
		if (modTile != null)
			return (int)(axe.axe / modTile.MineResist);
		int damage = tileType == TileID.Cactus ? (int)(axe.axe * 3 * 1.2f) : (int)(axe.axe * 1.2f);
		return Main.getGoodWorld ? (int)(damage * 1.3) : damage;
	}

	public bool Place(Item item, Point tile)
	{
		if (item == null || item.IsAir || item.createTile < 0 || !Ready || !CanReach(item, tile))
			return false;
		ClearReplaceableTile(tile);
		if (!TryPlaceTile(item, tile))
			return false;
		Swing(item, CombinedHooks.TotalUseTime(item.useTime, Avatar, item), tile.ToWorldCoordinates());
		Inventory.ConsumeOne(Avatar, item);
		if (item.IsAir)
			bot.Motor.RefreshGear();
		bot.Watchdog.RecordProgress();
		return true;
	}

	public bool PlaceWall(Item item, Point tile)
	{
		if (item == null || item.IsAir || item.createWall <= WallID.None || !Ready || !CanReach(item, tile))
			return false;
		WorldGen.PlaceWall(tile.X, tile.Y, item.createWall);
		if (Main.tile[tile.X, tile.Y].WallType != item.createWall)
			return false;
		Swing(item, CombinedHooks.TotalUseTime(item.useTime, Avatar, item), tile.ToWorldCoordinates());
		Inventory.ConsumeOne(Avatar, item);
		bot.Watchdog.RecordProgress();
		return true;
	}

	public bool CanPlaceAt(Item item, Point tile)
	{
		if (item == null || item.createTile < 0)
			return false;
		if (TileObjectData.GetTileData(item.createTile, item.placeStyle) == null)
			return !Main.tile[tile.X, tile.Y].HasTile;
		return TileObject.CanPlace(tile.X, tile.Y, item.createTile, item.placeStyle, 1, out _, onlyCheck: true);
	}

	static void ClearReplaceableTile(Point tile)
	{
		var existing = Main.tile[tile.X, tile.Y];
		if (existing.HasTile && !IsGrowingSapling(tile) && (Main.tileCut[existing.TileType] || TileID.Sets.BreakableWhenPlacing[existing.TileType]))
			WorldGen.KillTile(tile.X, tile.Y);
	}

	internal static bool TryPlaceTile(Item item, Point tile)
	{
		if (TileObjectData.GetTileData(item.createTile, item.placeStyle) != null)
		{
			if (!TileObject.CanPlace(tile.X, tile.Y, item.createTile, item.placeStyle, 1, out var objectData))
				return false;
			if (!TileObject.Place(objectData))
				return false;
			WorldGen.SquareTileFrame(tile.X, tile.Y);
			TileObjectData.CallPostPlacementPlayerHook(tile.X, tile.Y, item.createTile, item.placeStyle, 1, objectData.alternate, objectData);
			return true;
		}
		WorldGen.PlaceTile(tile.X, tile.Y, item.createTile, style: item.placeStyle);
		var placedTile = Main.tile[tile.X, tile.Y];
		return placedTile.HasTile && placedTile.TileType == item.createTile;
	}

	public bool HasRope => FindPlaceable(IsRopeTile) != null;

	public bool PlaceRope(Point tile) => Place(FindPlaceable(IsRopeTile), tile);

	public bool PlaceScaffold(Point tile)
	{
		var platform = FindPlaceable(IsPlatformTile);
		var block = ExpendableBlock();
		if (!Place(platform, tile) && !Place(block, tile))
			return false;
		bot.Faction.Scaffolding.Add(tile);
		return true;
	}

	public bool HasBridgeMaterial => BridgeMaterial() != null;

	public int BridgeMaterialCount()
	{
		int count = 0;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (item.createTile >= 0 && (IsPlatformTile(item.createTile) || IsExpendableBlock(item)))
				count += item.stack;
		}
		return count;
	}

	public bool PlaceBridge(Point tile)
	{
		var material = BridgeMaterial();
		int type = material?.type ?? 0;
		if (!Place(material, tile))
			return false;
		RememberPlaced(tile, type);
		return true;
	}

	const int PlacedMemory = 64;
	readonly System.Collections.Generic.Dictionary<Point, int> placedBlocks = new();

	void RememberPlaced(Point tile, int itemType)
	{
		if (placedBlocks.Count >= PlacedMemory)
			placedBlocks.Clear();
		placedBlocks[tile] = itemType;
	}

	public System.Collections.Generic.IEnumerable<Point> PlacedBlocks => placedBlocks.Keys;

	public bool IsReclaimable(Point tile) =>
		placedBlocks.TryGetValue(tile, out int itemType) && TileQuery.HasTile(tile) && TileQuery.TypeAt(tile) == ContentSamples.ItemsByType[itemType].createTile;

	public bool Reclaim(Point tile)
	{
		var pickaxe = BestPickaxe;
		if (!IsReclaimable(tile) || pickaxe == null || !Ready || !CanReach(pickaxe, tile))
			return false;
		int itemType = placedBlocks[tile];
		WorldGen.KillTile(tile.X, tile.Y, noItem: true);
		if (TileQuery.HasTile(tile))
			return false;
		placedBlocks.Remove(tile);
		Inventory.Add(Avatar, new Item(itemType));
		Swing(pickaxe, CombinedHooks.TotalUseTime(pickaxe.useTime, Avatar, pickaxe), tile.ToWorldCoordinates());
		bot.Motor.RefreshGear();
		bot.Watchdog.RecordProgress();
		return true;
	}

	public string DescribeBridgePlacement(Point tile)
	{
		var item = BridgeMaterial();
		return item == null ? "no material" : $"{item.Name} reach={CanReach(item, tile)} canPlace={CanPlaceAt(item, tile)} ready={Ready}";
	}

	Item BridgeMaterial() => FindPlaceable(IsPlatformTile) ?? ExpendableBlock();

	Item ExpendableBlock()
	{
		Item wood = null;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (!IsExpendableBlock(item))
				continue;
			if (item.type != ItemID.Wood)
				return item;
			wood ??= item;
		}
		return wood;
	}

	static readonly System.Func<int, bool> IsPlatformTile = tileType => TileID.Sets.Platforms[tileType];
	static readonly System.Func<int, bool> IsRopeTile = tileType => Main.tileRope[tileType];

	Item FindPlaceable(System.Func<int, bool> tileFilter)
	{
		foreach (var item in Inventory.Items(Avatar))
		{
			if (item.createTile >= 0 && tileFilter(item.createTile))
				return item;
		}
		return null;
	}

	public static bool IsExpendableBlock(Item item) =>
		item.createTile >= 0
		&& Main.tileSolid[item.createTile]
		&& !Main.tileSolidTop[item.createTile]
		&& !Main.tileFrameImportant[item.createTile]
		&& !TileID.Sets.Ore[item.createTile]
		&& item.createTile != TileID.Sand;

	public Item BestWeapon()
	{
		Item best = null;
		float bestScore = float.MinValue;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (!IsUsableWeapon(item))
				continue;
			float score = PreferenceScore(item);
			if (best == null || score > bestScore)
			{
				best = item;
				bestScore = score;
			}
		}
		return best;
	}

	public static bool IsRangedWeapon(Item weapon) => weapon.useAmmo > 0 && weapon.shoot > ProjectileID.None;

	public static bool IsMagicWeapon(Item weapon) =>
		weapon.mana > 0 && weapon.shoot > ProjectileID.None && weapon.useStyle == ItemUseStyleID.Shoot && weapon.DamageType.CountsAsClass(DamageClass.Magic);

	public static bool IsProjectileWeapon(Item weapon) => IsRangedWeapon(weapon) || IsMagicWeapon(weapon);

	public static CombatStyle StyleOf(Item weapon)
	{
		if (weapon.DamageType.CountsAsClass(DamageClass.Summon) || weapon.DamageType.CountsAsClass(DamageClass.SummonMeleeSpeed))
			return CombatStyle.Summon;
		if (weapon.DamageType.CountsAsClass(DamageClass.Magic))
			return CombatStyle.Magic;
		if (weapon.DamageType.CountsAsClass(DamageClass.Ranged))
			return CombatStyle.Ranged;
		return CombatStyle.Melee;
	}

	public static bool IsSummonWeapon(Item item) =>
		item.damage > 0 && item.shoot > ProjectileID.None && item.buffType > 0 && ContentSamples.ProjectilesByType.TryGetValue(item.shoot, out var minion) && minion.minion;

	public Item BestSummonWeapon()
	{
		Item best = null;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (IsSummonWeapon(item) && (best == null || item.damage > best.damage))
				best = item;
		}
		return best;
	}

	public bool Summon(Item weapon)
	{
		if (!Ready)
			return false;
		float used = 0f;
		foreach (var projectile in World.EntityIndex.AvatarProjectiles)
		{
			if (projectile.active && projectile.minion && projectile.owner == Avatar.whoAmI)
				used += projectile.minionSlots;
		}
		if (used + ContentSamples.ProjectilesByType[weapon.shoot].minionSlots > Avatar.maxMinions)
			return false;
		int manaCost = Avatar.GetManaCost(weapon);
		if (Avatar.statMana < manaCost)
			return false;
		Avatar.statMana -= manaCost;
		Avatar.AddBuff(weapon.buffType, MinionBuffTicks);
		var origin = bot.Npc.Center;
		int index;
		using (Systems.AvatarPresence.Enter())
			index = Projectile.NewProjectile(new EntitySource_ItemUse(Avatar, weapon), origin, Vector2.Zero, weapon.shoot, Avatar.GetWeaponDamage(weapon), Avatar.GetWeaponKnockback(weapon), Avatar.whoAmI);
		var minion = Main.projectile[index];
		minion.originalDamage = weapon.damage;
		Bosses.BossCredit.RegisterShot(minion, bot);
		Swing(weapon, CombinedHooks.TotalUseTime(weapon.useTime, Avatar, weapon), origin);
		BotDiagnostics.Log(bot, $"summoned {minion.Name} ({used + minion.minionSlots}/{Avatar.maxMinions} slots)");
		return true;
	}

	const int MinionBuffTicks = 18000;

	public Item BestProjectileWeapon() => Inventory.Items(Avatar).Where(item => IsUsableWeapon(item) && IsProjectileWeapon(item)).MaxBy(item => item.damage);

	bool IsUsableWeapon(Item item)
	{
		if (item.damage <= 0 || IsSummonWeapon(item))
			return false;
		if (item.channel && !IsRangedWeapon(item) && !IsMagicWeapon(item) && !IsYoyo(item))
			return false;
		if (IsMeleeSwing(item))
			return true;
		if (IsRangedWeapon(item))
			return FindAmmo(item.useAmmo) != null;
		return IsMagicWeapon(item) && Avatar.statMana >= Avatar.GetManaCost(item);
	}

	static bool IsMeleeSwing(Item item) =>
		(item.DamageType.CountsAsClass(DamageClass.Melee) && (item.useStyle == ItemUseStyleID.Swing || item.useStyle == ItemUseStyleID.Rapier || IsYoyo(item))) || IsWhip(item);

	public static bool IsWhip(Item item) => item.shoot > ProjectileID.None && ProjectileID.Sets.IsAWhip[item.shoot];

	const float WhipReach = 16f * 8f;

	public static bool IsYoyo(Item item) => ItemID.Sets.Yoyo[item.type];

	const float YoyoReach = 16f * 12f;

	const float StylePreference = 5f;

	float PreferenceScore(Item weapon)
	{
		int cadence = IsProjectileWeapon(weapon) ? CombinedHooks.TotalUseTime(weapon.useTime, Avatar, weapon) : CombinedHooks.TotalAnimationTime(weapon.useAnimation, Avatar, weapon);
		float score = Avatar.GetWeaponDamage(weapon) * 60f / Math.Max(1, cadence);
		if (weapon.pick > 0 || weapon.axe > 0 || weapon.hammer > 0)
			score *= 0.7f;
		if (IsProjectileWeapon(weapon))
			score *= 1.2f;
		if (StyleOf(weapon) == bot.Personality.Style)
			score *= StylePreference;
		return score;
	}

	public static float MeleeReach(Item weapon) => IsYoyo(weapon) ? YoyoReach : IsWhip(weapon) ? WhipReach : 10f + Math.Max(weapon.width, weapon.height) * weapon.scale;

	public bool InMeleeReach(NPC target, Item weapon)
	{
		var npc = bot.Npc;
		float reach = MeleeReach(weapon);
		int facing = target.Center.X >= npc.Center.X ? 1 : -1;
		var swingArea = new Rectangle(
			(int)(facing > 0 ? npc.Center.X : npc.Center.X - reach),
			(int)(npc.Center.Y - reach * 0.75f),
			(int)reach,
			(int)(reach * 1.5f));
		return swingArea.Intersects(target.Hitbox);
	}

	public bool Strike(NPC target, Item weapon)
	{
		if (!Ready || !InMeleeReach(target, weapon))
			return false;
		int direction = target.Center.X >= bot.Center.X ? 1 : -1;
		Swing(weapon, CombinedHooks.TotalAnimationTime(weapon.useAnimation, Avatar, weapon), target.Center);
		bool crit = Main.rand.Next(100) < Avatar.GetWeaponCrit(weapon);
		if (Tasks.Engagement.EnemyBotOf(bot, target) is Bot enemy)
		{
			int damage = Avatar.GetWeaponDamage(weapon) * (crit ? 2 : 1);
			enemy.Vitals.Hurt(Main.DamageVar(damage, Avatar.luck), direction, source: $"{bot.Faction.Name}/{bot.Name}'s {weapon.Name}");
			enemy.Vitals.RememberAttacker(bot.Npc);
			bot.Watchdog.RecordProgress();
			return true;
		}
		var hit = target.CalculateHitInfo(Avatar.GetWeaponDamage(weapon), direction, crit, Avatar.GetWeaponKnockback(weapon), weapon.DamageType, true, Avatar.luck);
		BannerCredit.RecordHit(target, bot);
		Bosses.BossCredit.RecordDamage(target, bot.FactionId, target.StrikeNPC(hit));
		bot.Watchdog.RecordProgress();
		return true;
	}

	public int ShotsFired { get; set; }

	const int ChannelledBeam = ProjectileID.ShadowBeamFriendly;

	public bool Fire(NPC target, Item weapon)
	{
		if (!Ready)
			return false;
		ShotsFired++;
		int projectileType = weapon.shoot;
		float speed = weapon.shootSpeed;
		int damage = Avatar.GetWeaponDamage(weapon);
		float knockback = Avatar.GetWeaponKnockback(weapon);
		if (IsRangedWeapon(weapon))
		{
			var ammo = FindAmmo(weapon.useAmmo);
			if (ammo == null)
				return false;
			if (ammo.shoot > ProjectileID.None)
				projectileType = ammo.shoot;
			speed += ammo.shootSpeed;
			damage += ammo.damage;
			knockback += ammo.knockBack;
			if (ammo.consumable)
				Inventory.ConsumeOne(Avatar, ammo);
		}
		else
		{
			int manaCost = Avatar.GetManaCost(weapon);
			if (Avatar.statMana < manaCost)
				return false;
			Avatar.statMana -= manaCost;
			Avatar.manaRegenDelay = (int)Avatar.maxRegenDelay;
		}

		if (weapon.channel && IsMagicWeapon(weapon))
			projectileType = ChannelledBeam;
		var origin = bot.Npc.Center;
		int index = Projectile.NewProjectile(new EntitySource_Misc("AutoTerraMod:BotShot"), origin, Aim(origin, target, speed, projectileType), projectileType, damage, knockback, Main.myPlayer);
		var projectile = Main.projectile[index];
		Bosses.BossCredit.RegisterShot(projectile, bot);
		projectile.usesLocalNPCImmunity = true;
		projectile.localNPCHitCooldown = ProjectileHitCooldown;
		projectile.CritChance = Avatar.GetWeaponCrit(weapon);
		bot.Watchdog.RecordProgress();
		Swing(weapon, CombinedHooks.TotalUseTime(weapon.useTime, Avatar, weapon), target.Center);
		return true;
	}

	static Vector2 Aim(Vector2 origin, NPC target, float speed, int projectileType)
	{
		float distance = Vector2.Distance(origin, target.Center);
		var predicted = target.Center + target.velocity * (distance / Math.Max(speed, 1f));
		var direction = predicted - origin;
		if (ContentSamples.ProjectilesByType.TryGetValue(projectileType, out var sample) && sample.aiStyle == ProjAIStyleID.Arrow)
			direction.Y -= distance * 0.08f;
		return Vector2.Normalize(direction) * speed;
	}

	Item FindAmmo(int ammoType) => Inventory.Items(Avatar).FirstOrDefault(item => item.ammo == ammoType && item.ammo > 0);

	public void DrinkObsidianSkinIfInHell()
	{
		if (bot.CenterTile.Y < Main.UnderworldLayer || Avatar.HasBuff(BuffID.ObsidianSkin))
			return;
		var potion = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.ObsidianSkinPotion);
		if (potion == null)
			return;
		Avatar.AddBuff(BuffID.ObsidianSkin, potion.buffTime);
		Inventory.ConsumeOne(Avatar, potion);
		BotDiagnostics.Log(bot, "drank an Obsidian Skin Potion");
	}

	static readonly int[] BattlePotions =
	{
		ItemID.IronskinPotion, ItemID.RegenerationPotion, ItemID.EndurancePotion, ItemID.LifeforcePotion,
		ItemID.SwiftnessPotion, ItemID.ArcheryPotion, ItemID.WrathPotion, ItemID.RagePotion, ItemID.ThornsPotion, ItemID.InfernoPotion
	};

	public void DrinkBattlePotions()
	{
		foreach (int type in BattlePotions)
		{
			var potion = Inventory.Items(Avatar).FirstOrDefault(item => item.type == type);
			if (potion == null || potion.buffType <= 0 || Avatar.HasBuff(potion.buffType))
				continue;
			Avatar.AddBuff(potion.buffType, potion.buffTime);
			BotDiagnostics.Log(bot, $"drank {potion.Name} for the fight");
			Inventory.ConsumeOne(Avatar, potion);
		}
	}

	public bool DrinkHealingPotion()
	{
		if (bot.Vitals.PotionSicknessTicks > 0)
			return false;
		Item potion = null;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (item.potion && item.healLife > 0 && (potion == null || item.healLife > potion.healLife))
				potion = item;
		}
		if (potion == null)
			return false;
		int healed = Math.Min(potion.healLife, Avatar.statLifeMax2 - Avatar.statLife);
		Avatar.statLife += healed;
		bot.Vitals.PotionSicknessTicks = Avatar.potionDelayTime;
		CombatText.NewText(bot.Npc.Hitbox, CombatText.HealLife, healed);
		if (potion.UseSound.HasValue)
			SoundEngine.PlaySound(potion.UseSound.Value, bot.Center);
		BotDiagnostics.Log(bot, $"drank {potion.Name}: life {Avatar.statLife - healed} -> {Avatar.statLife}/{Avatar.statLifeMax2}");
		Inventory.ConsumeOne(Avatar, potion);
		return true;
	}

	bool InOtherBase(Vector2 position) => InOtherBase(position.ToTileCoordinates());

	public bool InOtherBase(Point tile) =>
		Systems.BotWorldSystem.Factions.Any(faction => faction.Id != bot.FactionId && faction.Settlement?.Contains(tile.X, tile.Y) == true);

	public void CollectNearbyItems()
	{
		var npc = bot.Npc;
		var grabArea = npc.Hitbox;
		grabArea.Inflate(Player.defaultItemGrabRange, Player.defaultItemGrabRange);
		for (int i = 0; i < Main.maxItems; i++)
		{
			var item = Main.item[i];
			if (!item.active || item.noGrabDelay > 0 || !grabArea.Intersects(item.Hitbox) || ItemReservations.IsReservedForOther(i, bot) || InOtherBase(item.Center))
				continue;
			if (ItemID.Sets.IsAPickup[item.type])
			{
				ConsumePickup(item);
				Main.item[i] = new Item();
				continue;
			}
			if (!ItemLoader.CanPickup(item, Avatar) || Junk.IsUnwanted(bot, item))
				continue;
			if (!Inventory.HasRoomFor(Avatar, item))
			{
				Junk.TrashExcess(bot);
				if (!Inventory.HasRoomFor(Avatar, item))
					continue;
			}
			var leftover = Inventory.Add(Avatar, item.Clone());
			if (leftover.IsAir)
				Main.item[i] = new Item();
			else
				item.stack = leftover.stack;
			bot.Watchdog.RecordProgress();
			SoundEngine.PlaySound(SoundID.Grab, npc.Center);
		}
	}

	void ConsumePickup(Item pickup)
	{
		if (pickup.type == ItemID.Heart || pickup.type == ItemID.CandyApple || pickup.type == ItemID.CandyCane)
			Avatar.statLife = Math.Min(Avatar.statLife + 20, Avatar.statLifeMax2);
		else
			Avatar.statMana = Math.Min(Avatar.statMana + 100, Avatar.statManaMax2);
		SoundEngine.PlaySound(SoundID.Grab, bot.Center);
	}

	void Swing(Item item, int useTime, Vector2 toward)
	{
		int slot = Inventory.FindSlot(Avatar, item);
		if (slot >= 0)
			Avatar.selectedItem = slot;
		if (Avatar.lastVisualizedSelectedItem is not { } shown || shown.type != item.type || shown.prefix != item.prefix)
			Avatar.lastVisualizedSelectedItem = item.Clone();
		var npc = bot.Npc;
		npc.direction = toward.X >= npc.Center.X ? 1 : -1;
		Avatar.direction = npc.direction;
		AimDirection = toward == npc.Center ? new Vector2(npc.direction, 0f) : Vector2.Normalize(toward - npc.Center);
		Avatar.itemAnimation = Avatar.itemAnimationMax = Math.Max(useTime, CombinedHooks.TotalAnimationTime(item.useAnimation, Avatar, item));
		Avatar.itemTime = Avatar.itemTimeMax = useTime;
		cooldown = Math.Max(1, useTime);
		lastWorkTick = Main.GameUpdateCount;
		if (item.UseSound.HasValue)
			SoundEngine.PlaySound(item.UseSound.Value, npc.Center);
	}

	void PutAwayHeldItem()
	{
		if (Avatar.HeldItem.holdStyle == ItemHoldStyleID.None)
			return;
		Avatar.selectedItem = Math.Max(0, Inventory.FindSlot(Avatar, BestPickaxe));
		Avatar.lastVisualizedSelectedItem = Avatar.HeldItem.Clone();
	}

	void Animate()
	{
		if (Avatar.itemAnimation <= 0)
			return;
		Avatar.itemAnimation--;
		if (Avatar.itemTime > 0)
			Avatar.itemTime--;
		if (Avatar.itemAnimation == 0)
		{
			PutAwayHeldItem();
			return;
		}
		var item = Avatar.lastVisualizedSelectedItem;
		if (item == null || item.IsAir)
			return;
		try
		{
			Avatar.ItemCheck_ApplyUseStyle(0f, item, Item.GetDrawHitbox(item.type, Avatar));
			if (item.useStyle == ItemUseStyleID.Shoot)
				Avatar.itemRotation = (float)Math.Atan2(AimDirection.Y * Avatar.direction, AimDirection.X * Avatar.direction);
		}
		catch (Exception exception)
		{
			if (animationFailureLogged)
				return;
			animationFailureLogged = true;
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"Held item animation failed for {bot.Name}", exception);
		}
	}
}
