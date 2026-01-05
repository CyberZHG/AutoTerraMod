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
	public Vector2 AimDirection { get; private set; } = Vector2.UnitX;
	public bool Ready => cooldown <= 0;
	public Item BestPickaxe => Avatar.GetBestPickaxe();
	public Item BestAxe => Inventory.Items(Avatar).Where(item => item.axe > 0).MaxBy(item => item.axe);
	public Item BestHammer => Inventory.Items(Avatar).Where(item => item.hammer > 0).MaxBy(item => item.hammer);

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
		Avatar.PickTile(tile.X, tile.Y, pickaxe.pick);
		if (!Main.tile[tile.X, tile.Y].HasTile)
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

	public ToolResult Cut(Point tile)
	{
		if (!TileQuery.HasTile(tile))
			return ToolResult.Done;
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
		if (existing.HasTile && (Main.tileCut[existing.TileType] || TileID.Sets.BreakableWhenPlacing[existing.TileType]))
			WorldGen.KillTile(tile.X, tile.Y);
	}

	static bool TryPlaceTile(Item item, Point tile)
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

	public bool HasRope => FindPlaceable(tileType => Main.tileRope[tileType]) != null;

	public bool PlaceRope(Point tile) => Place(FindPlaceable(tileType => Main.tileRope[tileType]), tile);

	public bool PlaceScaffold(Point tile)
	{
		var platform = FindPlaceable(tileType => TileID.Sets.Platforms[tileType]);
		var block = Inventory.Items(Avatar).Where(IsExpendableBlock).OrderBy(item => item.type == ItemID.Wood ? 1 : 0).FirstOrDefault();
		if (!Place(platform, tile) && !Place(block, tile))
			return false;
		bot.Faction.Scaffolding.Add(tile);
		return true;
	}

	public bool HasBridgeMaterial => BridgeMaterial() != null;

	public bool PlaceBridge(Point tile) => Place(BridgeMaterial(), tile);

	Item BridgeMaterial() =>
		FindPlaceable(tileType => TileID.Sets.Platforms[tileType])
		?? Inventory.Items(Avatar).Where(IsExpendableBlock).OrderBy(item => item.type == ItemID.Wood ? 1 : 0).FirstOrDefault();

	Item FindPlaceable(System.Func<int, bool> tileFilter) =>
		Inventory.Items(Avatar).FirstOrDefault(item => item.createTile >= 0 && tileFilter(item.createTile));

	public static bool IsExpendableBlock(Item item) =>
		item.createTile >= 0
		&& Main.tileSolid[item.createTile]
		&& !Main.tileSolidTop[item.createTile]
		&& !Main.tileFrameImportant[item.createTile]
		&& !TileID.Sets.Ore[item.createTile]
		&& item.createTile != TileID.Sand;

	public Item BestWeapon() => Inventory.Items(Avatar).Where(IsUsableWeapon).MaxBy(PreferenceScore);

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

	bool IsUsableWeapon(Item item)
	{
		if (item.damage <= 0 || item.channel)
			return false;
		if (IsMeleeSwing(item))
			return true;
		if (IsRangedWeapon(item))
			return FindAmmo(item.useAmmo) != null;
		return IsMagicWeapon(item) && Avatar.statMana >= Avatar.GetManaCost(item);
	}

	static bool IsMeleeSwing(Item item) =>
		item.DamageType.CountsAsClass(DamageClass.Melee)
		&& (item.useStyle == ItemUseStyleID.Swing || item.useStyle == ItemUseStyleID.Rapier);

	float PreferenceScore(Item weapon)
	{
		float score = Avatar.GetWeaponDamage(weapon) * 60f / Math.Max(1, CombinedHooks.TotalAnimationTime(weapon.useAnimation, Avatar, weapon));
		if (weapon.pick > 0 || weapon.axe > 0 || weapon.hammer > 0)
			score *= 0.7f;
		if (IsProjectileWeapon(weapon))
			score *= 1.2f;
		if (StyleOf(weapon) == bot.Personality.Style)
			score *= 1.6f;
		return score;
	}

	public static float MeleeReach(Item weapon) => 10f + Math.Max(weapon.width, weapon.height) * weapon.scale;

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
		var hit = target.CalculateHitInfo(Avatar.GetWeaponDamage(weapon), direction, crit, Avatar.GetWeaponKnockback(weapon), weapon.DamageType, true, Avatar.luck);
		target.StrikeNPC(hit);
		bot.Watchdog.RecordProgress();
		return true;
	}

	public bool Fire(NPC target, Item weapon)
	{
		if (!Ready)
			return false;
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

		var origin = bot.Npc.Center;
		int index = Projectile.NewProjectile(new EntitySource_Misc("AutoTerraMod:BotShot"), origin, Aim(origin, target, speed, projectileType), projectileType, damage, knockback, Main.myPlayer);
		var projectile = Main.projectile[index];
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

	public bool DrinkHealingPotion()
	{
		if (bot.Vitals.PotionSicknessTicks > 0)
			return false;
		var potion = Inventory.Items(Avatar).Where(item => item.potion && item.healLife > 0).MaxBy(item => item.healLife);
		if (potion == null)
			return false;
		int healed = Math.Min(potion.healLife, Avatar.statLifeMax2 - Avatar.statLife);
		Avatar.statLife += healed;
		bot.Vitals.PotionSicknessTicks = Avatar.potionDelayTime;
		CombatText.NewText(bot.Npc.Hitbox, CombatText.HealLife, healed);
		if (potion.UseSound.HasValue)
			SoundEngine.PlaySound(potion.UseSound.Value, bot.Center);
		Inventory.ConsumeOne(Avatar, potion);
		return true;
	}

	public void CollectNearbyItems()
	{
		var npc = bot.Npc;
		var grabArea = npc.Hitbox;
		grabArea.Inflate(Player.defaultItemGrabRange, Player.defaultItemGrabRange);
		for (int i = 0; i < Main.maxItems; i++)
		{
			var item = Main.item[i];
			if (!item.active || item.noGrabDelay > 0 || !grabArea.Intersects(item.Hitbox) || ItemReservations.IsReservedForOther(i, bot))
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

	void Animate()
	{
		if (Avatar.itemAnimation <= 0)
			return;
		Avatar.itemAnimation--;
		if (Avatar.itemTime > 0)
			Avatar.itemTime--;
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
