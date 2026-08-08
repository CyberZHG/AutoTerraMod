using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Planning;

public enum GearTrack
{
	None,
	Tools,
	Armor,
	Weapons,
	Accessories
}

public abstract class ProgressionGoal
{
	public virtual GearTrack Track => GearTrack.None;
	public virtual bool AppliesTo(Bot bot) => true;
	public virtual bool IsUrgent(Bot bot) => false;
	public virtual string Describe() => GetType().Name;
	public abstract bool IsMet(Bot bot);
	public abstract PlanStep Plan(Bot bot);
	public virtual bool IsExpedition => false;
	public virtual bool NeedsSurface => false;
	public virtual bool MayUseWood => false;

	string probeLabel;
	public string ProbeLabel => probeLabel ??= "g:" + ProgressionGoals.Unwrap(this).GetType().Name;
}

public sealed class StationGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	readonly int tileType;
	readonly int[] stationItems;

	public StationGoal(int tileType, params int[] stationItems)
	{
		this.tileType = tileType;
		this.stationItems = stationItems;
	}

	public override bool IsMet(Bot bot) => Stations.BaseHas(bot.Faction, tileType) || Placeable().Length == 0 || Stations.TeammateFetching(bot, stationItems);
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.PlanStation(bot, Placeable());

	int[] Placeable() => tileType == TileID.TeleportationPylon
		? stationItems.Where(item => !Terraria.Main.PylonSystem.Pylons.Any(pylon => (int)pylon.TypeOfPylon == Terraria.ID.ContentSamples.ItemsByType[item].placeStyle)).ToArray()
		: stationItems;
}

public sealed class GearGoal : ProgressionGoal
{
	readonly GearKind kind;
	readonly int[] options;
	readonly int requiredPower;

	public GearKind Kind => kind;
	public override string Describe() => $"Gear({kind})";

	public override GearTrack Track => kind switch
	{
		GearKind.Pickaxe or GearKind.Hook => GearTrack.Tools,
		GearKind.Head or GearKind.Body or GearKind.Legs => GearTrack.Armor,
		_ => GearTrack.Weapons
	};

	public GearGoal(GearKind kind, params int[] options)
	{
		this.kind = kind;
		this.options = options;
		requiredPower = Math.Max(1, options.Min(option => Equipment.SamplePower(option, kind)));
	}

	public System.Func<Factions.Faction, bool> LeapWhen { get; set; }

	public override bool IsMet(Bot bot) => Equipment.BestPower(bot.Avatar, kind) >= requiredPower || LeapWhen?.Invoke(bot.Faction) == true || Outgrown(bot);
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainGear(bot, options);

	public List<GearGoal> Successors { get; } = new();

	const int OutgrownCacheTicks = 600;

	readonly Dictionary<int, (uint at, bool outgrown)> outgrownCache = new();

	bool Outgrown(Bot bot)
	{
		if (Successors.Count == 0)
			return false;
		int lifetime = OutgrownCacheTicks + (bot.Id * 131 + requiredPower * 17) % OutgrownCacheTicks;
		if (outgrownCache.TryGetValue(bot.Id, out var cached) && Terraria.Main.GameUpdateCount - cached.at < lifetime)
			return cached.outgrown;
		bool outgrown = Successors.Any(successor => successor.ReadyToMake(bot));
		outgrownCache[bot.Id] = (Terraria.Main.GameUpdateCount, outgrown);
		return outgrown;
	}

	bool ReadyToMake(Bot bot) =>
		Equipment.BestPower(bot.Avatar, kind) < requiredPower && ProgressionPlanner.ObtainGear(bot, options) is CraftStep or WithdrawStep or PlaceStationStep;
}

public sealed class ReserveGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	readonly int itemType;
	readonly int minimum;
	readonly int restockTo;

	readonly int onlyWhenHolding;

	public ReserveGoal(int itemType, int minimum, int restockTo, int onlyWhenHolding = ItemID.None)
	{
		this.itemType = itemType;
		this.minimum = minimum;
		this.restockTo = restockTo;
		this.onlyWhenHolding = onlyWhenHolding;
	}

	public override bool IsMet(Bot bot) => Inventory.Count(bot.Avatar, itemType) >= minimum
		|| (onlyWhenHolding != ItemID.None && Inventory.Count(bot.Avatar, onlyWhenHolding) == 0 && !ProgressionPlanner.CanAffordEasily(bot, itemType, restockTo));
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.Obtain(bot, itemType, restockTo);
}

public sealed class HealingPotionGoal : ProgressionGoal
{
	static readonly int[] Potions = { ItemID.HealingPotion, ItemID.LesserHealingPotion };
	const int Minimum = 4;
	const int RestockTo = 10;

	public override string Describe() => "Potions";

	const int RealPotionHeal = 50;

	static int Held(Bot bot) => Inventory.Items(bot.Avatar).Where(item => item.potion && item.healLife >= RealPotionHeal).Sum(item => item.stack);

	public override bool IsMet(Bot bot) => Held(bot) >= Minimum;

	public override PlanStep Plan(Bot bot)
	{
		int missing = RestockTo - Held(bot);
		foreach (int potion in Potions)
		{
			if (ProgressionPlanner.PlanPurchase(bot, potion, missing) is BuyStep buy)
				return buy;
		}
		return PlanStep.Idle;
	}
}

public sealed class CraftableUpgradeGoal : ProgressionGoal
{
	const uint CacheTicks = 600;
	static List<Terraria.Recipe> gearRecipes;
	readonly Dictionary<int, (uint at, PlanStep step)> cache = new();

	public override string Describe() => "CraftableUpgrade";

	public override bool IsMet(Bot bot) => Upgrade(bot) == null;

	public override PlanStep Plan(Bot bot) => Upgrade(bot) ?? PlanStep.Idle;

	public static IEnumerable<GearKind> KindsFor(Bot bot)
	{
		yield return GearKind.Head;
		yield return GearKind.Body;
		yield return GearKind.Legs;
		yield return GearKind.Pickaxe;
		switch (bot.Personality.Style)
		{
			case CombatStyle.Melee: yield return GearKind.MeleeWeapon; break;
			case CombatStyle.Ranged: yield return GearKind.RangedWeapon; break;
			case CombatStyle.Magic: yield return GearKind.MagicWeapon; break;
		}
	}

	public static bool Improves(Bot bot, Item item) => KindsFor(bot).Any(kind => Equipment.PowerOf(item, kind) > Equipment.BestPower(bot.Avatar, kind));

	public static List<Terraria.Recipe> GearRecipes() => gearRecipes ??= Terraria.Main.recipe.Take(Terraria.Recipe.numRecipes)
		.Where(recipe => recipe.createItem.headSlot >= 0 || recipe.createItem.bodySlot >= 0 || recipe.createItem.legSlot >= 0 || recipe.createItem.pick > 0 || recipe.createItem.damage > 0)
		.ToList();

	static PlanStep StoredUpgrade(Bot bot)
	{
		if (!Shops.WithinShoppingTrip(bot, bot.Faction.BaseCenter))
			return null;
		foreach (var chest in BaseStorage.Chests(bot.Faction))
		{
			foreach (var item in chest.item)
			{
				if (item != null && !item.IsAir && Improves(bot, item))
					return new WithdrawStep(item.type, 1);
			}
		}
		return null;
	}

	public static PlanStep CraftedUpgrade(Bot bot)
	{
		foreach (var recipe in GearRecipes())
		{
			if (Crafting.HasIngredients(bot.Avatar, recipe) && Improves(bot, recipe.createItem) && HasStations(bot, recipe))
				return new CraftStep(recipe);
		}
		return null;
	}

	static bool HasStations(Bot bot, Recipe recipe) => recipe.requiredTile.All(tile => tile < 0
		|| Stations.IsReachableStation(bot.Faction, tile, bot.CenterTile)
		|| Bots.Tasks.CraftTask.IsPortableStationTile(tile) && Stations.ItemsPlacing(tile).Any(item => Inventory.Count(bot.Avatar, item) > 0));

	static bool StillValid(Bot bot, PlanStep step) => step switch
	{
		CraftStep craft => Crafting.HasIngredients(bot.Avatar, craft.Recipe) && Improves(bot, craft.Recipe.createItem) && HasStations(bot, craft.Recipe),
		WithdrawStep withdraw => BaseStorage.Count(bot.Faction, withdraw.ItemType) > 0 && Improves(bot, ContentSamples.ItemsByType[withdraw.ItemType]),
		_ => true
	};

	PlanStep Upgrade(Bot bot)
	{
		if (cache.TryGetValue(bot.Id, out var cached) && Terraria.Main.GameUpdateCount - cached.at < CacheTicks && StillValid(bot, cached.step))
			return cached.step;
		var step = StoredUpgrade(bot) ?? CraftedUpgrade(bot);
		cache[bot.Id] = (Terraria.Main.GameUpdateCount, step);
		return step;
	}
}

public sealed class WaterproofTorchGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public static readonly int[] Torches = { ItemID.CursedTorch, ItemID.IchorTorch };
	static readonly int[] Flames = { ItemID.CursedFlame, ItemID.Ichor };
	const int Minimum = 20;
	const int RestockTo = 50;

	public override bool AppliesTo(Bot bot) => Flames.Any(flame => Inventory.Count(bot.Avatar, flame) > 0 || BaseStorage.Count(bot.Faction, flame) > 0);

	public override bool IsMet(Bot bot) => Inventory.CountAny(bot.Avatar, Torches) >= Minimum;

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAnyAmount(bot, Torches, RestockTo);
}

public sealed class AmmoGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	const int Minimum = 100;
	const int RestockTo = 300;

	internal static readonly int[] Arrows =
	{
		ItemID.ChlorophyteArrow, ItemID.IchorArrow, ItemID.CursedArrow, ItemID.HolyArrow, ItemID.HellfireArrow,
		ItemID.JestersArrow, ItemID.UnholyArrow, ItemID.FrostburnArrow, ItemID.FlamingArrow, ItemID.WoodenArrow
	};

	internal static readonly int[] Bullets =
	{
		ItemID.ChlorophyteBullet, ItemID.CrystalBullet, ItemID.IchorBullet, ItemID.CursedBullet, ItemID.PartyBullet,
		ItemID.MeteorShot, ItemID.SilverBullet, ItemID.TungstenBullet, ItemID.MusketBall
	};

	public override string Describe() => "Ammo";

	internal static int AmmoClass(Bot bot)
	{
		Item best = null;
		foreach (var item in Inventory.Items(bot.Avatar))
		{
			if (item.damage > 0 && (item.useAmmo == AmmoID.Arrow || item.useAmmo == AmmoID.Bullet) && item.shoot > ProjectileID.None && (best == null || item.damage > best.damage))
				best = item;
		}
		return best?.useAmmo ?? 0;
	}

	static int[] OptionsFor(int ammoClass) => ammoClass == AmmoID.Bullet ? Bullets : Arrows;

	public override bool AppliesTo(Bot bot) => AmmoClass(bot) != 0;

	internal const int Critical = 30;
	internal const int Keep = RestockTo;

	public override bool IsMet(Bot bot) => Supply(bot) >= Minimum;

	public override bool IsUrgent(Bot bot) => AppliesTo(bot) && Supply(bot) < Critical;

	internal static int Supply(Bot bot)
	{
		int ammoClass = AmmoClass(bot);
		int supply = 0;
		foreach (var item in Inventory.Items(bot.Avatar))
		{
			if (item.ammo != ammoClass)
				continue;
			if (!item.consumable)
				return int.MaxValue;
			supply += item.stack;
		}
		return supply;
	}

	public override PlanStep Plan(Bot bot)
	{
		int ammoClass = AmmoClass(bot);
		int missing = System.Math.Max(1, RestockTo - System.Math.Min(RestockTo, Supply(bot)));
		if (ammoClass == AmmoID.Arrow && ProgressionPlanner.PlanPurchase(bot, ItemID.WoodenArrow, missing) is BuyStep buy)
			return buy;
		return ProgressionPlanner.ObtainAnyAmount(bot, OptionsFor(ammoClass), RestockTo);
	}
}

public sealed class RopeGoal : ProgressionGoal
{
	const int Minimum = 10;
	const int RestockTo = 30;

	public override bool IsMet(Bot bot) => RopeCount(bot.Avatar) >= Minimum;

	static readonly int[] Ropes = { ItemID.Rope, ItemID.VineRope, ItemID.SilkRope };
	static readonly HashSet<int> restocking = new();

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAnyAmount(bot, Ropes, RestockTo);

	static int RopeCount(Terraria.Player avatar) =>
		Inventory.Items(avatar).Where(item => item.createTile >= 0 && Terraria.Main.tileRope[item.createTile]).Sum(item => item.stack);
}

public sealed class StorageGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool NeedsSurface => true;

	const int MinimumChests = 2;

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && HouseInspector.IsComplete(bot.Faction, bot.Faction.Settlement.Storage);

	const int SpareSlots = 20;

	public override bool IsMet(Bot bot)
	{
		int chests = BaseStorage.ChestIndices(bot.Faction).Count;
		if (chests < MinimumChests)
			return false;
		return BaseStorage.FreeSlots(bot.Faction) >= SpareSlots || chests >= bot.Faction.Settlement.StorageRooms
			.Where(room => HouseInspector.IsComplete(bot.Faction, room)).Sum(room => room.ChestSlots().Count());
	}

	public override PlanStep Plan(Bot bot)
	{
		var held = Inventory.Items(bot.Avatar).FirstOrDefault(BaseStorage.IsStorageItem);
		return held != null ? new PlaceStationStep(held.type) : ProgressionPlanner.Obtain(bot, ItemID.Chest, 1);
	}
}

public sealed class ClaimBoostersGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	public override bool IsMet(Bot bot) => ClaimableBooster(bot) < 0;

	public override PlanStep Plan(Bot bot) => new WithdrawStep(ClaimableBooster(bot), 1);

	static int ClaimableBooster(Bot bot) => StatBoosters.BoosterTypes
		.Where(type => type != ItemID.FallenStar && StatBoosters.KeepsForSelf(bot, type) && BaseStorage.Count(bot.Faction, type) > 0)
		.DefaultIfEmpty(-1)
		.First();
}

public sealed class ManaCrystalGoal : ProgressionGoal
{
	const int StarsPerCrystal = 5;

	public override bool IsMet(Bot bot) =>
		!StatBoosters.KeepsForSelf(bot, ItemID.ManaCrystal)
		|| Inventory.Count(bot.Avatar, ItemID.FallenStar) + BaseStorage.Count(bot.Faction, ItemID.FallenStar) < StarsPerCrystal;

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.Obtain(bot, ItemID.ManaCrystal, 1);
}

public sealed class StructureGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool NeedsSurface => true;

	readonly System.Func<Settlement, IEnumerable<HouseBlueprint>> select;

	const int MaterialBatch = 60;

	public StructureGoal(System.Func<Settlement, IEnumerable<HouseBlueprint>> select) => this.select = select;

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && HouseInspector.IsDesignatedBuilder(bot) && !ProgressionGoals.OnGearBreak(bot);

	public override bool IsMet(Bot bot) => NextUnfinished(bot) == null;

	HouseBlueprint NextUnfinished(Bot bot) =>
		select(bot.Faction.Settlement).FirstOrDefault(structure => !HouseInspector.IsFinished(bot.Faction, structure));

	public override PlanStep Plan(Bot bot)
	{
		HouseBlueprint structure = null;
		BuildOperation operation = default;
		foreach (var candidate in select(bot.Faction.Settlement))
		{
			if (HouseInspector.IsFinished(bot.Faction, candidate) || HouseInspector.NextOperation(bot.Faction, candidate) is not BuildOperation next)
				continue;
			structure = candidate;
			operation = next;
			break;
		}
		if (structure == null)
		{
			if (NextUnfinished(bot) is HouseBlueprint stalled && HouseInspector.IsComplete(bot.Faction, stalled) == false && HouseInspector.NextOperation(bot.Faction, stalled) == null)
				HouseInspector.ReportUnfinishable(bot, stalled);
			return PlanStep.Idle;
		}
		var materials = HouseInspector.MaterialsFor(bot.Faction);
		if (operation.Action == BuildAction.RenovateWall && bot.Hands.BestHammer == null && !Terraria.Main.wallHouse[Terraria.Main.tile[operation.Tile.X, operation.Tile.Y].WallType])
			return ProgressionPlanner.ObtainAny(bot, Bosses.BossRituals.Hammers);
		var placement = HouseMaterials.ReplacementFor(operation.Action);
		if (!HouseMaterials.IsNeeded(placement))
			return new BuildHouseStep(operation);
		if (ConstructionBill.For(structure, materials, bot).Where(entry => entry.Placement == placement).Select(entry => (BillLine?)entry).FirstOrDefault() is not BillLine line)
			return HouseMaterials.Find(bot.Avatar, placement, materials) == null
				? ProgressionPlanner.ObtainAny(bot, HouseMaterials.Options(placement, materials))
				: new BuildHouseStep(operation);
		int batch = Math.Min(line.Amount, MaterialBatch);
		if (HouseMaterials.Find(bot.Avatar, placement, materials) == null)
			return placement is BuildAction.PlaceDoor or BuildAction.PlaceTable or BuildAction.PlaceChair or BuildAction.PlaceBed
				? ProgressionPlanner.ObtainAny(bot, HouseMaterials.Options(placement, materials))
				: ProgressionPlanner.Obtain(bot, line.ItemType, batch);
		if (HouseMaterials.CountAvailable(bot.Avatar, placement, materials) < batch && ProgressionPlanner.Obtain(bot, line.ItemType, batch) is CraftStep craft)
			return craft;
		return new BuildHouseStep(operation);
	}
}

public sealed class SupportConstructionGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool NeedsSurface => true;

	const float MinimumCooperation = 0.4f;
	const int BatchSize = 40;

	public override bool AppliesTo(Bot bot) =>
		bot.Faction.Settlement != null && bot.Personality.Cooperation >= MinimumCooperation && !HouseInspector.IsDesignatedBuilder(bot) && !ProgressionGoals.OnGearBreak(bot);

	public override bool IsMet(Bot bot) => Need(bot, out _) == null;

	public override PlanStep Plan(Bot bot)
	{
		if (Need(bot, out var builder) is not BillLine need)
			return PlanStep.Idle;
		bool canHandOver = Inventory.UsedSlots(builder.Avatar) < Inventory.MainSlotCount
			&& Microsoft.Xna.Framework.Vector2.Distance(builder.Center, bot.Faction.BaseCenter) < BuilderAtHome;
		if (GiftOnHand(bot, need) is int gift && canHandOver)
			return new DeliverStep(gift, builder.Id, HandOver: true);
		int batch = System.Math.Min(need.Amount, BatchSize);
		if (Inventory.Count(bot.Avatar, need.ItemType) < batch)
			return ProgressionPlanner.Obtain(bot, need.ItemType, batch);
		return canHandOver || BaseStorage.HasRoom(bot.Faction) ? new DeliverStep(need.ItemType, builder.Id, HandOver: canHandOver) : PlanStep.Idle;
	}

	const int MinimumGift = 20;
	const float BuilderAtHome = 16f * 60f;

	static int? GiftOnHand(Bot bot, BillLine need)
	{
		var materials = HouseInspector.MaterialsFor(bot.Faction);
		int minimum = need.Placement is BuildAction.PlaceBlock or BuildAction.PlaceWall ? MinimumGift : 1;
		foreach (int type in HouseMaterials.Options(need.Placement, materials))
		{
			if (Inventory.Count(bot.Avatar, type) >= minimum)
				return type;
		}
		return null;
	}

	static BillLine? Need(Bot bot, out Bot builder)
	{
		builder = BotWorldSystem.Bots
			.Where(member => member.FactionId == bot.FactionId && member != bot && member.IsAlive)
			.MaxBy(member => member.Personality.Building);
		var settlement = bot.Faction.Settlement;
		if (builder == null || settlement == null)
			return null;
		var structure = settlement.Structures.FirstOrDefault(candidate => !HouseInspector.IsFinished(bot.Faction, candidate));
		if (structure == null)
			return null;
		var materials = HouseInspector.MaterialsFor(bot.Faction);
		return ConstructionBill.FirstShortfall(builder, ConstructionBill.For(structure, materials, builder), materials);
	}
}

public sealed class BossGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool IsExpedition => true;

	const int ReadinessLogIntervalTicks = 1800;

	public override bool AppliesTo(Bot bot) => Bosses.BossCredit.NextChallengeFor(bot.Faction) != null && IsSummoner(bot);

	public override bool IsMet(Bot bot) => Bosses.BossCredit.NextChallengeFor(bot.Faction) == null;

	public override string Describe() => "Boss";

	public static bool IsSummoner(Bot bot) =>
		BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive).MaxBy(member => member.Personality.Combat) == bot;

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.WithGearPriority(() => PlanBoss(bot));

	PlanStep PlanBoss(Bot bot)
	{
		if (Terraria.Main.GameUpdateCount < bot.Faction.RallyCooldownUntil)
			return PlanStep.Idle;
		if (Bosses.BossRituals.LeftoverPillars(bot) is { } pillars)
			return pillars;
		PlanStep preparation = null;
		foreach (var challenge in Bosses.BossCredit.OpenChallengesFor(bot.Faction))
		{
			if (Bosses.BossCatalog.IsBossAlive(challenge))
				return PlanStep.Idle;
			if (bot.Faction.Rally != null && bot.Faction.RallyBossType != challenge.BossType)
				continue;
			var report = Bosses.BossReadiness.Evaluate(bot.Faction, challenge, bot);
			LogReadiness(bot, challenge, report);
			if (!report.IsReady)
			{
				preparation ??= Prepare(bot, challenge);
				continue;
			}
			foreach (var trigger in Bosses.BossRituals.InPreferredOrder(bot, challenge))
			{
				var step = Bosses.BossRituals.Plan(bot, challenge, trigger);
				if (step is SummonBossStep summon && Bosses.BossRituals.PillarInTheWay(bot, summon.Place) is { } pillar)
					return pillar;
				if (step is not BlockedStep and not IdleStep)
					return step;
			}
		}
		if (preparation != null && GearRefight.AppliesTo(bot) && !GearRefight.IsMet(bot) && GearRefight.Plan(bot) is not (BlockedStep or IdleStep))
			return PlanStep.Idle;
		return preparation ?? PlanStep.Idle;
	}

	static readonly RefightGoal GearRefight = new(gearOnly: true);

	static PlanStep Prepare(Bot bot, Bosses.BossChallenge challenge)
	{
		foreach (var trigger in Bosses.BossRituals.InPreferredOrder(bot, challenge))
		{
			var step = Bosses.BossRituals.Plan(bot, challenge, trigger);
			if (step is not BlockedStep and not IdleStep and not SummonBossStep)
				return step;
			if (step is SummonBossStep)
				return null;
		}
		return null;
	}

	readonly Dictionary<int, uint> lastReadinessLog = new();

	void LogReadiness(Bot bot, Bosses.BossChallenge challenge, Bosses.ReadinessReport report)
	{
		if (lastReadinessLog.TryGetValue(challenge.BossType, out uint loggedAt) && Terraria.Main.GameUpdateCount - loggedAt < ReadinessLogIntervalTicks)
			return;
		lastReadinessLog[challenge.BossType] = Terraria.Main.GameUpdateCount;
		BotDiagnostics.Log(bot, $"boss readiness for {challenge.Name}: fighters={report.Fighters} teamDps={report.TeamDps:0} bossLife={report.BossLife:0} kill={report.KillSeconds:0}s survive={report.SurviveSeconds:0}s needRatio={report.RequiredRatio:0.0} ready={report.IsReady}");
	}
}

public sealed class BossSupportGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	const float HelpRange = 16f * 400f;
	const float HandOverRange = 16f * 30f;

	public override string Describe() => "BossSupport";

	public override bool AppliesTo(Bot bot) => !BossGoal.IsSummoner(bot) && Need(bot) != null;

	public override bool IsMet(Bot bot) => Need(bot) == null;

	static readonly Dictionary<int, (GatherStep gather, uint at)> recentNeeds = new();
	const uint NeedMemoryTicks = 3600;

	static (Bot summoner, GatherStep gather)? Need(Bot bot)
	{
		var summoner = BotWorldSystem.Bots.FirstOrDefault(member => member.FactionId == bot.FactionId && member.IsAlive && BossGoal.IsSummoner(member));
		if (summoner == null || summoner == bot)
			return null;
		if (summoner.Brain.WorksOnBoss && summoner.Brain.Plan is GatherStep current)
			recentNeeds[bot.FactionId] = (current, Main.GameUpdateCount);
		if (!recentNeeds.TryGetValue(bot.FactionId, out var need) || Main.GameUpdateCount - need.at > NeedMemoryTicks)
			return null;
		if (summoner.Brain.Plan is not GatherStep && Inventory.Count(summoner.Avatar, need.gather.ItemType) >= need.gather.Amount)
			return null;
		bool committed = bot.Brain.Plan is GatherStep mine && mine.ItemType == need.gather.ItemType;
		if (!committed && Microsoft.Xna.Framework.Vector2.Distance(bot.Center, summoner.Center) > HelpRange)
			return null;
		return (summoner, need.gather);
	}

	public override PlanStep Plan(Bot bot)
	{
		if (Need(bot) is not var (summoner, gather))
			return PlanStep.Idle;
		int held = Inventory.Count(bot.Avatar, gather.ItemType);
		bool close = Microsoft.Xna.Framework.Vector2.Distance(bot.Center, summoner.Center) < HandOverRange;
		if (held >= gather.Amount || held > 0 && close)
			return new DeliverStep(gather.ItemType, summoner.Id, 0, HandOver: true);
		return new GatherStep(gather.ItemType, gather.Amount - held, gather.Source);
	}
}

public sealed class RefightGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	readonly bool gearOnly;

	public RefightGoal(bool gearOnly) => this.gearOnly = gearOnly;

	public override bool IsExpedition => true;

	public override string Describe() => gearOnly ? "Refight(gear)" : "Refight";

	public override bool AppliesTo(Bot bot) => bot.Faction.RefightWishes.Count > 0 && BossGoal.IsSummoner(bot);

	public override bool IsMet(Bot bot) => !Wanted(bot).Any();

	IEnumerable<Bosses.BossChallenge> Wanted(Bot bot) =>
		Bosses.BossCatalog.Challenges.Where(challenge => bot.Faction.WantsRefight(challenge.BossType, gearOnly) && Bosses.BossCredit.HasDefeated(bot.Faction, challenge)
			&& (challenge.BossType != Terraria.ID.NPCID.CultistBoss || Bosses.BossRituals.PillarsWorthRaising(bot.Faction)));

	public override PlanStep Plan(Bot bot)
	{
		if (Terraria.Main.GameUpdateCount < bot.Faction.RallyCooldownUntil)
			return PlanStep.Idle;
		foreach (var challenge in Wanted(bot))
		{
			if (bot.Faction.Rally != null && bot.Faction.RallyBossType != challenge.BossType)
				continue;
			if (Bosses.BossCatalog.IsBossAlive(challenge) || !Bosses.BossReadiness.Evaluate(bot.Faction, challenge, bot).IsReady)
				continue;
			foreach (var trigger in Bosses.BossRituals.InPreferredOrder(bot, challenge))
			{
				var step = Bosses.BossRituals.Plan(bot, challenge, trigger);
				if (step is not BlockedStep and not IdleStep)
					return step;
			}
		}
		return PlanStep.Idle;
	}
}

public sealed class HellevatorGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool IsExpedition => true;

	public override bool AppliesTo(Bot bot) =>
		(Bosses.BossRituals.PreparingForWall(bot.Faction) || bot.Faction.Hellevator is { IsComplete: true, IsFinished: false }) && !HouseInspector.BuildingDuty(bot);

	public override bool IsMet(Bot bot) => Bosses.BossRituals.ShaftFor(bot.Faction).IsFinished;

	public override PlanStep Plan(Bot bot) => Bosses.BossRituals.PlanShaftWork(bot);
}

public sealed class BridgeKitGoal : ProgressionGoal
{
	const int KitStone = 200;
	const int KitPlatforms = 40;

	public override bool NeedsSurface => true;

	public override string Describe() => "BridgeKit";

	public override bool AppliesTo(Bot bot) =>
		Bosses.BossRituals.PreparingForWall(bot.Faction) && bot.Faction.Hellevator?.IsComplete != true && bot.Faction.Hellbridge?.IsComplete != true;

	public override bool IsMet(Bot bot) => Inventory.Count(bot.Avatar, ItemID.StoneBlock) >= KitStone && Platforms(bot) >= KitPlatforms;

	static readonly BridgeKitGoal Instance = new();

	public static bool Gathering(Bot bot) => Instance.AppliesTo(bot);

	public static bool IsKitItem(Item item) => item.type == ItemID.StoneBlock || item.createTile >= 0 && TileID.Sets.Platforms[item.createTile];

	static int Platforms(Bot bot) => Inventory.Items(bot.Avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack);

	public override PlanStep Plan(Bot bot) =>
		Platforms(bot) < KitPlatforms ? ProgressionPlanner.ObtainPlatforms(bot, KitPlatforms) : ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, KitStone);
}

public sealed class ShaftRopeGoal : ProgressionGoal
{
	const int RopeTrip = 200;
	const int UndergroundMargin = 30;

	public override bool IsExpedition => true;

	public override string Describe() => "ShaftRope";

	const int RopeAfterRows = 20;

	public override bool AppliesTo(Bot bot) =>
		bot.Faction.Hellevator is { } shaft && shaft.FirstImpassableRow() - shaft.TopY >= RopeAfterRows && IsRoper(bot);

	static bool IsRoper(Bot bot) =>
		BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive && !HouseInspector.BuildingDuty(member)).MaxBy(member => member.Personality.Building) == bot;

	public override bool IsMet(Bot bot)
	{
		var shaft = Bosses.BossRituals.ShaftFor(bot.Faction);
		return Bots.Tasks.RopeColumnTask.MissingRow(shaft.RopeX, shaft.TopY, shaft.RopeBottom()) == null;
	}

	public override PlanStep Plan(Bot bot)
	{
		var shaft = Bosses.BossRituals.ShaftFor(bot.Faction);
		var step = HighwayGoal.Hang(bot, shaft.RopeX, shaft.TopY, shaft.RopeBottom(), RopeTrip);
		if (step == null && !bot.Hands.HasRope && bot.CenterTile.Y > Main.worldSurface + UndergroundMargin)
			return new TravelStep(bot.Faction.BaseFeet);
		return step ?? PlanStep.Idle;
	}
}

public sealed class HellbridgeGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool IsExpedition => true;

	public override bool AppliesTo(Bot bot) => Bosses.BossRituals.PreparingForWall(bot.Faction) && !HouseInspector.BuildingDuty(bot);

	public override bool IsMet(Bot bot) => bot.Faction.Hellbridge?.IsComplete == true;

	public override PlanStep Plan(Bot bot) => Bosses.BossRituals.PlanBridgeHelp(bot);
}

public sealed class StyleGoal : ProgressionGoal
{
	readonly ProgressionGoal inner;
	readonly HashSet<CombatStyle> styles;

	public StyleGoal(ProgressionGoal inner, params CombatStyle[] styles)
	{
		this.inner = inner;
		this.styles = styles.ToHashSet();
	}

	public override GearTrack Track => inner.Track;
	public override bool AppliesTo(Bot bot) => (styles.Contains(bot.Personality.Style) || styles.Contains(EffectiveStyle(bot.Personality.Style))) && inner.AppliesTo(bot);
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);

	public ProgressionGoal Inner => inner;

	public HashSet<CombatStyle> Styles => styles;

	public static CombatStyle EffectiveStyle(CombatStyle style) => style == CombatStyle.Summon ? CombatStyle.Melee : style;
}

public sealed class OwnGoal : ProgressionGoal
{
	readonly int[] options;

	public OwnGoal(params int[] options) => this.options = options;

	public override GearTrack Track => options.All(option => ContentSamples.ItemsByType[option].accessory) ? GearTrack.Accessories : GearTrack.None;

	public override string Describe() => $"Own({Terraria.Lang.GetItemNameValue(options[0])})";

	public override bool IsMet(Bot bot) =>
		options.Any(option => Inventory.Count(bot.Avatar, option) > 0 || bot.Avatar.armor.Any(worn => worn.type == option));

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAny(bot, options);
}

public sealed class CageGoal : ProgressionGoal
{
	static Dictionary<int, int[]> cagesByCritter;

	public static IReadOnlyList<int> CagesFor(int critterItem) => CagesByCritter().TryGetValue(critterItem, out var cages) ? cages : System.Array.Empty<int>();

	static Dictionary<int, int[]> CagesByCritter() => cagesByCritter ??= Terraria.Main.recipe.Take(Terraria.Recipe.numRecipes)
		.Where(recipe => recipe.createItem.createTile >= 0 && TileID.Sets.CritterCageLidStyle[recipe.createItem.createTile] != -1)
		.SelectMany(recipe => recipe.requiredItem.Where(ingredient => !ingredient.IsAir && ingredient.makeNPC > 0).Select(ingredient => (critter: ingredient.type, cage: recipe.createItem.type)))
		.GroupBy(pair => pair.critter)
		.ToDictionary(group => group.Key, group => group.Select(pair => pair.cage).Distinct().ToArray());

	IEnumerable<int[]> PendingCages(Bot bot) =>
		Inventory.Items(bot.Avatar).Select(item => item.type).Distinct()
			.Where(type => CagesByCritter().ContainsKey(type))
			.Select(type => CagesByCritter()[type]);

	public override bool IsMet(Bot bot) => !PendingCages(bot).Any();

	public override PlanStep Plan(Bot bot)
	{
		foreach (var cages in PendingCages(bot))
		{
			var step = ProgressionPlanner.ObtainAny(bot, cages);
			if (step is not BlockedStep and not IdleStep)
				return step;
		}
		return PlanStep.Blocked;
	}
}

public sealed class AspirationGoal : ProgressionGoal
{
	readonly ProgressionGoal inner;
	readonly Aspiration aspiration;
	readonly bool fallback;

	public AspirationGoal(Aspiration aspiration, ProgressionGoal inner, bool fallback = false)
	{
		this.aspiration = aspiration;
		this.inner = inner;
		this.fallback = fallback;
	}

	public ProgressionGoal Inner => inner;

	public AspirationGoal AsFallback() => new(aspiration, inner, true);

	public override bool AppliesTo(Bot bot) =>
		bot.Personality.Aspiration == aspiration && (fallback || Bosses.BossCredit.AllBossesDone(bot.Faction)) && inner.AppliesTo(bot);
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);
}

public sealed class FurnishBedroomsGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool NeedsSurface => true;

	public override string Describe() => "FurnishBedrooms";

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && HouseInspector.IsDesignatedBuilder(bot);

	public override bool IsMet(Bot bot) =>
		HouseInspector.HasSpareBed(bot.Faction) || !bot.Faction.Settlement.Bedrooms.Any(HouseInspector.MissesBed);

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAny(bot, HouseDesign.Beds);
}

public sealed class OutpostKitGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override bool NeedsSurface => true;

	public override string Describe() => "OutpostKit";

	public override bool AppliesTo(Bot bot) => !Outpost.IsAfield(bot) && Shops.WithinShoppingTrip(bot, bot.Faction.BaseCenter);

	public override bool IsMet(Bot bot) => Outpost.CarriesKit(bot.Avatar);

	public override PlanStep Plan(Bot bot) => PlanKit(bot);

	public static PlanStep PlanKit(Bot bot)
	{
		var avatar = bot.Avatar;
		if (Outpost.Count(avatar, Outpost.IsBed) == 0)
			return ProgressionPlanner.ObtainAny(bot, HouseDesign.Beds);
		if (Outpost.Count(avatar, Outpost.IsDoor) == 0)
			return ProgressionPlanner.ObtainAny(bot, HouseDesign.Doors);
		int walls = Outpost.Count(avatar, Outpost.IsHouseWall);
		return ProgressionPlanner.Obtain(bot, ItemID.StoneWall, Inventory.Count(avatar, ItemID.StoneWall) + Outpost.WallsNeeded - walls);
	}
}

public sealed class RoadFirstGoal : ProgressionGoal
{
	public override string Describe() => "RoadFirst";

	public override bool AppliesTo(Bot bot) => (BossGoal.IsSummoner(bot) || HouseInspector.IsDesignatedBuilder(bot)) && PendingRoad(bot).Any();

	public override bool IsMet(Bot bot) => false;

	public override PlanStep Plan(Bot bot) => HighwayGoal.PlanHighways(bot, PendingRoad(bot)) ?? PlanStep.Idle;

	public static IEnumerable<World.Highway> PendingRoad(Bot bot)
	{
		if (Bosses.BossCredit.NextChallengeFor(bot.Faction) is not { BossType: NPCID.DukeFishron } || bot.Faction.Rally != null || Bosses.BossRituals.Shoreline(bot) is not Microsoft.Xna.Framework.Vector2 shore)
			return System.Array.Empty<World.Highway>();
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
		return World.Highway.Toward(bot.Faction.BaseTile.X, (int)(shore.X / 16f))
			.Where(highway => !highway.IsComplete && !highway.BlockedAt(highway.Frontier(highway.Direction), pick));
	}
}

public sealed class HighwayGoal : ProgressionGoal
{
	const int BlockRestock = 500;
	const int BlockMinimumTrip = 60;
	const int BlockLow = 10;
	const int PlatformRestock = 200;
	const int PlatformLow = 20;
	const int RopeRestock = 50;
	static readonly int[] Ropes = { ItemID.Rope, ItemID.VineRope, ItemID.SilkRope };
	static readonly HashSet<int> restocking = new();
	static readonly HashSet<int> restockingStone = new();

	public override string Describe() => "Highway";

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && Building.HouseInspector.IsDesignatedBuilder(bot);

	public override bool IsMet(Bot bot) => false;

	public override PlanStep Plan(Bot bot)
	{
		if (PlanHighways(bot, World.Highway.For(bot.Faction)) is { } road)
			return road;
		var shaft = Bosses.BossRituals.ShaftFor(bot.Faction);
		if (!shaft.IsFinished && (Bosses.BossRituals.PreparingForWall(bot.Faction) || bot.Hands.Explosives.Carries) && Bosses.BossRituals.ShaftWork(bot, shaft) is var dig and not IdleStep and not BlockedStep)
			return dig;
		return Hang(bot, shaft.RopeX, shaft.TopY, shaft.RopeBottom()) ?? PlanStep.Idle;
	}

	internal static PlanStep PlanHighways(Bot bot, IEnumerable<World.Highway> highways)
	{
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
		foreach (var highway in highways)
		{
			if (highway.IsComplete)
			{
				if (highway.LinkX is int linkX && highway.LinkRows is var (top, bottom) && highway.MissingLinkRow() != null
					&& Hang(bot, linkX, top, bottom) is { } link)
					return link;
				continue;
			}
			int frontier = highway.WorkFrontier();
			if (highway.BlockedAt(frontier, pick))
				continue;
			int stone = Inventory.Count(bot.Avatar, ItemID.StoneBlock);
			bool nearHome = Shops.WithinShoppingTrip(bot, bot.Faction.BaseCenter);
			if ((stone < BlockLow || nearHome && stone < BlockRestock / 2) && (!bot.Hands.HasBridgeMaterial || nearHome))
				restockingStone.Add(bot.Id);
			int stoneTarget = nearHome ? BlockRestock : BlockMinimumTrip;
			if (stone >= stoneTarget)
				restockingStone.Remove(bot.Id);
			if (restockingStone.Contains(bot.Id))
			{
				var blocks = ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, stoneTarget);
				if (blocks is not (BlockedStep or IdleStep))
					return blocks;
				restockingStone.Remove(bot.Id);
				if (!bot.Hands.HasBridgeMaterial)
					continue;
			}
			int carried = Inventory.Items(bot.Avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack);
			if (carried == 0 && highway.RequiresPlatform(frontier) || nearHome && carried < PlatformLow)
				restocking.Add(bot.Id);
			if (carried >= PlatformRestock)
				restocking.Remove(bot.Id);
			if (restocking.Contains(bot.Id))
			{
				var platforms = ProgressionPlanner.ObtainPlatforms(bot, PlatformRestock);
				if (platforms is not BlockedStep)
					return platforms;
				restocking.Remove(bot.Id);
				if (carried == 0)
					continue;
			}
			return new BuildBridgeStep(highway, highway.Direction);
		}
		return null;
	}

	internal static PlanStep Hang(Bot bot, int x, int top, int bottom, int restock = RopeRestock)
	{
		if (Bots.Tasks.RopeColumnTask.MissingRow(x, top, bottom) == null)
			return null;
		if (bot.Hands.HasRope)
			return new HangRopeStep(x, top, bottom);
		var ropes = ProgressionPlanner.ObtainAnyAmount(bot, Ropes, restock);
		return ropes is BlockedStep or IdleStep ? null : ropes;
	}
}

public sealed class SkyArenaGoal : ProgressionGoal
{
	const int PlatformBatch = 60;

	public override bool MayUseWood => true;

	public override string Describe() => "SkyArena";

	public override bool AppliesTo(Bot bot) => World.SkyArena.Wanted && HouseInspector.IsDesignatedBuilder(bot) && World.SkyArena.Safe(bot.Faction) != null;

	public override bool IsMet(Bot bot) => World.SkyArena.For(bot.Faction).IsComplete;

	public override PlanStep Plan(Bot bot)
	{
		var arena = World.SkyArena.For(bot.Faction);
		if (!arena.RopeReady)
			return HighwayGoal.Hang(bot, arena.CenterX, arena.DeckRow, arena.Surface - 1) ?? PlanStep.Blocked;
		if (!bot.Hands.HasPlatform)
			return ProgressionPlanner.ObtainPlatforms(bot, PlatformBatch);
		return new BuildSkyArenaStep();
	}
}

public sealed class ArenaKitGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override string Describe() => "ArenaKit";

	public override bool AppliesTo(Bot bot) => Reserves(bot);

	public static bool Reserves(Bot bot) => BossGoal.IsSummoner(bot) && Outpost.ExpectsDistantArena(bot);

	public override bool IsMet(Bot bot) => Outpost.CarriesKit(bot.Avatar) && Outpost.StoneBlocks(bot.Avatar) >= Outpost.BlocksNeeded;

	public override PlanStep Plan(Bot bot)
	{
		var avatar = bot.Avatar;
		if (Outpost.CarriesKit(avatar))
			return ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, Inventory.Count(avatar, ItemID.StoneBlock) + Outpost.BlocksNeeded - Outpost.StoneBlocks(avatar));
		return OutpostKitGoal.PlanKit(bot);
	}
}

public sealed class ArenaOutpostGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override string Describe() => "ArenaOutpost";

	public override bool IsUrgent(Bot bot) => Outpost.WantsArenaOutpost(bot);

	public override bool IsMet(Bot bot) => !IsUrgent(bot);

	public override PlanStep Plan(Bot bot) => IsUrgent(bot) ? new BuildOutpostStep() : PlanStep.Idle;
}

public sealed class ClaimOutpostGoal : ProgressionGoal
{
	public override string Describe() => "ClaimOutpost";

	public override bool AppliesTo(Bot bot) => bot.Faction.Rally == null && Outpost.ClaimableBed(bot) != null;

	public override bool IsMet(Bot bot) => false;

	public override PlanStep Plan(Bot bot) => Outpost.ClaimableBed(bot) is Microsoft.Xna.Framework.Point bed ? new ClaimOutpostStep(bed) : PlanStep.Idle;
}

public sealed class OutpostGoal : ProgressionGoal
{
	public override bool MayUseWood => true;

	public override string Describe() => "Outpost";

	public override bool AppliesTo(Bot bot) => Outpost.CarriesKit(bot.Avatar) && Outpost.WantsNew(bot) && bot.Faction.Rally == null && !ArenaKitGoal.Reserves(bot);

	public override bool IsMet(Bot bot) => false;

	public override PlanStep Plan(Bot bot)
	{
		var avatar = bot.Avatar;
		int blocks = Outpost.Count(avatar, Outpost.IsBlock);
		if (Outpost.StoneBlocks(avatar) < Outpost.BlocksNeeded && ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, Inventory.Count(avatar, ItemID.StoneBlock) + Outpost.BlocksNeeded - Outpost.StoneBlocks(avatar)) is var stone
			and not BlockedStep and not IdleStep && (blocks < Outpost.BlocksNeeded || stone is not GatherStep))
			return stone;
		if (blocks < Outpost.BlocksNeeded)
			return ProgressionPlanner.Obtain(bot, ItemID.StoneBlock, Inventory.Count(avatar, ItemID.StoneBlock) + Outpost.BlocksNeeded - blocks);
		return new BuildOutpostStep();
	}
}

public sealed class GatedGoal : ProgressionGoal
{
	readonly ProgressionGoal inner;
	readonly Func<Bot, bool> open;

	public GatedGoal(ProgressionGoal inner, Func<bool> open) : this(inner, _ => open()) { }

	public GatedGoal(ProgressionGoal inner, Func<Bot, bool> open)
	{
		this.inner = inner;
		this.open = open;
	}

	public ProgressionGoal Inner => inner;
	public override GearTrack Track => inner.Track;
	public override bool AppliesTo(Bot bot) => open(bot) && inner.AppliesTo(bot);
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);
	public override bool IsExpedition => inner.IsExpedition;
}

public sealed class SurveyGoal : ProgressionGoal
{
	static World.Landmark[] Surveyed => new[] { World.Landmarks.WorldEvil, World.Landmark.Dungeon, World.Landmark.Jungle, World.Landmark.Snow, World.Landmark.Ocean };

	const int Surveyors = 2;

	static int SurveyorRank(Bot bot) => BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive)
		.OrderByDescending(member => member.Personality.Exploring).ThenBy(member => member.Id).ToList().IndexOf(bot);

	public override bool IsExpedition => true;

	public override string Describe() => "Survey";

	public override bool AppliesTo(Bot bot) => SurveyorRank(bot) is >= 0 and < Surveyors;

	public override bool IsMet(Bot bot) => Unknown(bot) == null && UnexploredCave(bot, SurveyRingCells) == null;

	const int SurveyRingCells = 6;
	const int NearbyCaveRings = 2;
	const int OpenSearchTiles = 6;

	static readonly Dictionary<int, int> headings = new();

	static Microsoft.Xna.Framework.Point? UnexploredCave(Bot bot, int rings)
	{
		int heading = headings.GetValueOrDefault(bot.Id, bot.Id % 2 == 0 ? 1 : -1);
		if (UnexploredCave(bot, rings, heading) is Microsoft.Xna.Framework.Point ahead)
			return ahead;
		if (UnexploredCave(bot, rings, -heading) is not Microsoft.Xna.Framework.Point behind)
			return null;
		headings[bot.Id] = -heading;
		return behind;
	}

	static Microsoft.Xna.Framework.Point? UnexploredCave(Bot bot, int rings, int heading)
	{
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		int cell = Factions.FactionKnowledge.ExploreCell;
		var here = bot.CenterTile;
		int topCell = (int)Terraria.Main.worldSurface / cell + 1;
		int bottomCell = (Terraria.Main.UnderworldLayer - cell) / cell;
		int originCellY = System.Math.Max(here.Y / cell, topCell);
		for (int ring = 0; ring <= rings; ring++)
		{
			for (int dx = -ring; dx <= ring; dx++)
			{
				for (int dy = -ring; dy <= ring; dy++)
				{
					if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring || dx * heading < 0)
						continue;
					int cellX = here.X / cell + dx, cellY = originCellY + dy;
					if (cellY < topCell || cellY > bottomCell || cellX < 2 || cellX > Terraria.Main.maxTilesX / cell - 2 || knowledge.IsExplored(cellX, cellY))
						continue;
					if (OpenTileNear(cellX * cell + cell / 2, cellY * cell + cell / 2) is Microsoft.Xna.Framework.Point open && !World.TileQuery.InOcean(open.X, open.Y))
						return open;
				}
			}
		}
		return null;
	}

	static Microsoft.Xna.Framework.Point? OpenTileNear(int x, int y)
	{
		for (int dx = -OpenSearchTiles; dx <= OpenSearchTiles; dx += 2)
		{
			for (int dy = -OpenSearchTiles; dy <= OpenSearchTiles; dy += 2)
			{
				if (!World.TileQuery.IsSolid(x + dx, y + dy) && !World.TileQuery.IsSolid(x + dx, y + dy - 1) && Terraria.Main.tile[x + dx, y + dy].LiquidAmount == 0)
					return new Microsoft.Xna.Framework.Point(x + dx, y + dy);
			}
		}
		return null;
	}

	static World.Landmark? Unknown(Bot bot)
	{
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		var unknown = Surveyed.Where(landmark => knowledge.LandmarkOf(landmark) == null).ToList();
		return unknown.Count == 0 ? null : unknown[System.Math.Max(0, SurveyorRank(bot)) % unknown.Count];
	}

	public override PlanStep Plan(Bot bot) =>
		UnexploredCave(bot, NearbyCaveRings) is Microsoft.Xna.Framework.Point near ? new SurveyStep(near)
		: Unknown(bot) is World.Landmark landmark ? new ScoutStep(landmark, false)
		: UnexploredCave(bot, SurveyRingCells) is Microsoft.Xna.Framework.Point cave ? new SurveyStep(cave)
		: PlanStep.Idle;
}

public sealed class SmashAltarsGoal : ProgressionGoal
{
	const int AltarsForAllOreTiers = 3;
	const int AltarSearchRadius = 3000;

	static readonly HashSet<int> AltarTiles = new() { TileID.DemonAltar };

	static readonly int[] Pwnhammers = { ItemID.Pwnhammer, ItemID.Hammush };

	public override bool AppliesTo(Bot bot) => Terraria.Main.hardMode;

	public override bool IsMet(Bot bot) => Terraria.WorldGen.altarCount >= AltarsForAllOreTiers;

	public override PlanStep Plan(Bot bot)
	{
		if (bot.Hands.BestHammer is not { } hammer || !BotHands.CanSmashAltars(hammer))
			return ProgressionPlanner.ObtainAny(bot, Pwnhammers);
		var knowledge = BotWorldSystem.KnowledgeOf(bot.Faction);
		return knowledge.NearestKnown(bot.Faction.BaseTile, AltarTiles, new HashSet<Microsoft.Xna.Framework.Point>(World.SharedAltar.Tiles()), AltarSearchRadius) is Microsoft.Xna.Framework.Point altar
			? new SmashShrineStep(altar)
			: new ScoutStep(World.Landmarks.WorldEvil, true);
	}
}

public sealed class StockpileGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	readonly string name;
	readonly int floor;
	readonly int target;
	readonly System.Func<Bot, IReadOnlyList<int>> options;

	public override bool MayUseWood => name == "Torch";

	public StockpileGoal(string name, int floor, int target, System.Func<Bot, IReadOnlyList<int>> options)
	{
		this.name = name;
		this.floor = floor;
		this.target = target;
		this.options = options;
	}

	public override string Describe() => $"Stockpile({name})";

	public override bool AppliesTo(Bot bot) =>
		bot.Faction.Settlement != null && HouseInspector.IsComplete(bot.Faction, bot.Faction.Settlement.Storage)
		&& IsQuartermaster(bot) && options(bot).Count > 0;

	public override bool IsMet(Bot bot) => Stored(bot, options(bot)) >= floor;

	public override PlanStep Plan(Bot bot)
	{
		var types = options(bot);
		int want = target - Stored(bot, types);
		int spareType = 0, spare = 0, spareKeep = 0;
		foreach (int type in types)
		{
			int keep = KeepFor(bot, type);
			int extra = Inventory.Count(bot.Avatar, type) - keep;
			if (extra > spare)
				(spareType, spare, spareKeep) = (type, extra, keep);
		}
		var deliver = spare > 0 && BaseStorage.HasRoom(bot.Faction) ? new DeliverStep(spareType, bot.Id, spareKeep) : null;
		if (deliver != null && spare >= want)
			return deliver;
		var step = ProgressionPlanner.ObtainAnyAmount(bot, types, KeepFor(bot, types[0]) + want, fresh: true);
		return step is BlockedStep or IdleStep && deliver != null ? deliver : step;
	}

	static int Stored(Bot bot, IReadOnlyList<int> types)
	{
		int stored = 0;
		foreach (int type in types)
			stored += BaseStorage.Count(bot.Faction, type);
		return stored;
	}

	static int KeepFor(Bot bot, int type)
	{
		if (type == ItemID.Torch)
			return Kit.TorchReserve;
		var sample = ContentSamples.ItemsByType[type];
		return sample.ammo > 0 && sample.ammo == AmmoGoal.AmmoClass(bot) ? AmmoGoal.Keep : 0;
	}

	public static bool IsQuartermaster(Bot bot)
	{
		Bot chosen = null;
		foreach (var member in BotWorldSystem.Bots)
		{
			if (member.FactionId == bot.FactionId && member.IsAlive && (chosen == null || member.Personality.Cooperation > chosen.Personality.Cooperation))
				chosen = member;
		}
		return chosen == bot;
	}

	public static IReadOnlyList<int> FactionAmmo(Bot bot, int ammoClass)
	{
		foreach (var member in BotWorldSystem.Bots)
		{
			if (member.FactionId == bot.FactionId && AmmoGoal.AmmoClass(member) == ammoClass)
				return ammoClass == AmmoID.Bullet ? AmmoGoal.Bullets : AmmoGoal.Arrows;
		}
		return System.Array.Empty<int>();
	}
}

public sealed class RallyRouteGoal : ProgressionGoal
{
	const uint RallyKeepTicks = 2 * 3600;
	const float CampCheckInPixels = 16f * 40f;

	public override string Describe() => "RallyRoute";

	public override bool AppliesTo(Bot bot) => bot.Faction.RallyBuilding && bot.Faction.Rally != null;

	public override bool IsUrgent(Bot bot) => AppliesTo(bot);

	public override bool IsMet(Bot bot) => !bot.Faction.RallyBuilding;

	public override PlanStep Plan(Bot bot)
	{
		var faction = bot.Faction;
		if (faction.Rally is { } rally)
			faction.Rally = (rally.point, System.Math.Max(rally.until, Terraria.Main.GameUpdateCount + RallyKeepTicks));
		if (!faction.RallyCheckedIn.Contains(bot.Id))
		{
			if (Microsoft.Xna.Framework.Vector2.Distance(bot.Center, faction.BaseCenter) > CampCheckInPixels)
				return new TravelStep(faction.BaseFeet - new Microsoft.Xna.Framework.Vector2(0f, 32f));
			faction.RallyCheckedIn.Add(bot.Id);
		}
		var open = faction.RallyRoute.Where(highway => !highway.IsBuilt(highway.EndX)).ToList();
		int bestPick = BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive).Max(member => member.Hands.BestPickaxe?.pick ?? 0);
		bool blocked = open.Any(highway => highway.BlockedAt(highway.WorkFrontier(), bestPick))
			|| faction.RallyShaft is { IsFinished: false } dug && !Bosses.BossRituals.CanDigRowWith(bestPick, dug, dug.WorkRow());
		if (!blocked)
		{
			if (HighwayGoal.PlanHighways(bot, open) is { } road)
				return road;
			if (open.Count == 0 && faction.RallyShaft is { IsFinished: false } shaft && Bosses.BossRituals.ShaftWork(bot, shaft) is var dig and not (IdleStep or BlockedStep))
				return dig;
			if (open.Count > 0 || faction.RallyShaft is { IsFinished: false })
				return PlanStep.Idle;
		}
		BotDiagnostics.Log(bot, blocked ? "rally route is blocked by tiles nobody can dig, heading straight there" : "rally route is built, setting out");
		faction.RallyRoute = null;
		faction.RallyShaft = null;
		return PlanStep.Idle;
	}
}

public sealed class HandSummonGoal : ProgressionGoal
{
	const float HandOverTrip = 16f * 30f;

	public override string Describe() => "HandSummon";

	public override bool AppliesTo(Bot bot) => !BossGoal.IsSummoner(bot) && HeldSummon(bot) > 0;

	public override bool IsUrgent(Bot bot) => AppliesTo(bot) && Plan(bot) is DeliverStep;

	public override bool IsMet(Bot bot) => HeldSummon(bot) == 0;

	public override PlanStep Plan(Bot bot)
	{
		int item = HeldSummon(bot);
		if (item == 0 || BotWorldSystem.Bots.FirstOrDefault(member => member.FactionId == bot.FactionId && member.IsAlive && BossGoal.IsSummoner(member)) is not { } summoner)
			return PlanStep.Idle;
		if (Microsoft.Xna.Framework.Vector2.Distance(summoner.Center, bot.Center) <= HandOverTrip && Inventory.HasRoomForType(summoner.Avatar, item))
			return new DeliverStep(item, summoner.Id, HandOver: true);
		return BaseStorage.HasRoom(bot.Faction) ? new DeliverStep(item, summoner.Id) : PlanStep.Idle;
	}

	static int HeldSummon(Bot bot) =>
		Bosses.BossCredit.NextChallengeFor(bot.Faction) is { } challenge
			? challenge.Triggers.OfType<Bosses.SummonItemTrigger>().Select(trigger => trigger.Item).FirstOrDefault(item => Inventory.Count(bot.Avatar, item) > 0)
			: 0;
}

public sealed class ShareSuppliesGoal : ProgressionGoal
{
	const float MinimumCooperation = 0.4f;
	const int MinimumGift = 50;
	const float MaxTripDistance = 16f * 200f;

	public override string Describe() => "ShareAmmo";

	public override bool AppliesTo(Bot bot) => bot.Personality.Cooperation >= MinimumCooperation;

	public override bool IsUrgent(Bot bot) => AppliesTo(bot) && Need(bot, out _, out _, out _) != null;

	public override bool IsMet(Bot bot) => Need(bot, out _, out _, out _) == null;

	public override PlanStep Plan(Bot bot) =>
		Need(bot, out int itemType, out int keep, out var teammate) != null ? new DeliverStep(itemType, teammate.Id, keep, HandOver: true) : PlanStep.Idle;

	static Bot Need(Bot bot, out int itemType, out int keep, out Bot teammate)
	{
		itemType = 0;
		keep = 0;
		teammate = null;
		foreach (var member in BotWorldSystem.Bots)
		{
			if (member == bot || member.FactionId != bot.FactionId || !member.IsAlive)
				continue;
			int ammoClass = AmmoGoal.AmmoClass(member);
			if (ammoClass == 0 || AmmoGoal.Supply(member) >= AmmoGoal.Critical)
				continue;
			if (Microsoft.Xna.Framework.Vector2.Distance(member.Center, bot.Center) > MaxTripDistance)
				continue;
			int ownKeep = AmmoGoal.AmmoClass(bot) == ammoClass ? AmmoGoal.Keep : 0;
			foreach (var item in Inventory.Items(bot.Avatar))
			{
				if (item.ammo != ammoClass || !item.consumable || Inventory.Count(bot.Avatar, item.type) - ownKeep < MinimumGift)
					continue;
				itemType = item.type;
				keep = ownKeep;
				teammate = member;
				return member;
			}
		}
		return null;
	}
}

public sealed class GardenGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	public override string Describe() => "Garden";

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && Shops.TradesWith(bot, NPCID.Dryad) && HerbGarden.HasSeeds(bot);

	public override bool IsMet(Bot bot) => HerbGarden.Boxes(bot.Faction).Count + HerbGarden.BoxesOnHand(bot) >= HerbGarden.Size;

	public override PlanStep Plan(Bot bot) =>
		ProgressionPlanner.PlanPurchase(bot, HerbGarden.Box, HerbGarden.Size - HerbGarden.Boxes(bot.Faction).Count - HerbGarden.BoxesOnHand(bot));
}

public sealed class GardenSeedGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	public override string Describe() => "GardenSeed";

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && Terraria.Main.dayTime && Terraria.Main.hardMode && !HerbGarden.HasSeeds(bot)
		&& BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive).MaxBy(member => member.Personality.Exploring) == bot;

	public override bool IsMet(Bot bot) => HerbGarden.HasSeeds(bot);

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.Obtain(bot, ItemID.Daybloom, Inventory.Count(bot.Avatar, ItemID.Daybloom) + 1);
}

public sealed class DyeGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	public override string Describe() => "Dye";

	public override bool AppliesTo(Bot bot) => DyeTaste.MissingDyes(bot.Avatar) > 0 && !DyeTaste.CarriesFavorite(bot);

	public override bool IsMet(Bot bot)
	{
		int dye = DyeTaste.FavoriteOnSale(bot);
		return dye == ItemID.None || !ProgressionPlanner.CanAffordEasily(bot, dye, 1);
	}

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.PlanPurchase(bot, DyeTaste.FavoriteOnSale(bot), 1);
}

public sealed class ShoppingGoal : ProgressionGoal
{
	public override bool NeedsSurface => true;

	const long CoinReserve = 5000;
	const int PotionStock = 10;
	const int PotionRestockBelow = 4;
	const float UpgradeRatio = 1.2f;
	const int ChoiceCacheTicks = 600;

	readonly Dictionary<int, (uint at, BuyStep choice)> choices = new();

	public override string Describe() => "Shopping";

	public override bool AppliesTo(Bot bot) => Coins(bot) > CoinReserve && (bot.Brain.Plan is BuyStep || Shops.WithinShoppingTrip(bot, bot.Faction.BaseCenter));

	public override bool IsMet(Bot bot) => Choice(bot) == null;

	public override PlanStep Plan(Bot bot) => (PlanStep)Choice(bot) ?? PlanStep.Idle;

	static long Coins(Bot bot) => Terraria.Utils.CoinsCount(out _, bot.Avatar.inventory);

	BuyStep Choice(Bot bot)
	{
		if (choices.TryGetValue(bot.Id, out var cached) && Terraria.Main.GameUpdateCount - cached.at < ChoiceCacheTicks
			&& (cached.choice == null || Gain(bot, ContentSamples.ItemsByType[cached.choice.ItemType], out _) > 0f))
			return cached.choice;
		var choice = Best(bot);
		choices[bot.Id] = (Terraria.Main.GameUpdateCount, choice);
		if (choice != null && choice != cached.choice)
			BotDiagnostics.Log(bot, $"wants to buy {Terraria.Lang.GetItemNameValue(choice.ItemType)} x{choice.Amount}");
		return choice;
	}

	static BuyStep Best(Bot bot)
	{
		long budget = Coins(bot) - CoinReserve;
		if (budget <= 0)
			return null;
		BuyStep best = null;
		float bestGain = 0f;
		foreach (var (itemType, sellerType, price) in Shops.CurrentOffers(bot))
		{
			if (!ContentSamples.ItemsByType.TryGetValue(itemType, out var sample))
				continue;
			float gain = Gain(bot, sample, out int amount);
			if (gain <= bestGain || (long)price * amount > budget || !Shops.TradesWith(bot, sellerType))
				continue;
			best = new BuyStep(itemType, amount, sellerType);
			bestGain = gain;
		}
		return best;
	}

	static float Gain(Bot bot, Item sample, out int amount)
	{
		amount = 1;
		if (sample.potion && sample.healLife > 0)
		{
			int held = 0;
			foreach (var item in Inventory.Items(bot.Avatar))
			{
				if (item.potion && item.healLife > 0)
					held += item.stack;
			}
			amount = PotionStock - held;
			return held < PotionRestockBelow ? 2f : 0f;
		}
		if (sample.accessory && !sample.vanity)
		{
			if (Inventory.Owns(bot.Avatar, sample.type))
				return 0f;
			int weakest = int.MaxValue;
			for (int slot = Equipment.FirstAccessorySlot; slot <= Equipment.LastAccessorySlot; slot++)
			{
				if (bot.Avatar.IsItemSlotUnlockedAndUsable(slot))
					weakest = System.Math.Min(weakest, Equipment.AccessoryScore(bot.Avatar.armor[slot]));
			}
			int score = Equipment.AccessoryScore(sample);
			return weakest == int.MaxValue || score <= weakest ? 0f : (score - weakest) / 20f;
		}
		var kind = KindFor(bot, sample);
		if (kind is not GearKind gearKind)
			return 0f;
		int power = Equipment.PowerOf(sample, gearKind);
		int current = Equipment.BestPower(bot.Avatar, gearKind);
		return power > current * UpgradeRatio && power > current + 2 ? (power - current) / (float)System.Math.Max(1, current) : 0f;
	}

	static GearKind? KindFor(Bot bot, Item sample)
	{
		if (sample.pick > 0)
			return GearKind.Pickaxe;
		if (sample.damage <= 0 || sample.axe > 0 || sample.hammer > 0)
			return null;
		if (BotHands.IsRangedWeapon(sample))
			return bot.Personality.Style == CombatStyle.Ranged && (sample.useAmmo == AmmoID.Arrow || sample.useAmmo == AmmoID.Bullet) ? GearKind.RangedWeapon : null;
		var style = BotHands.StyleOf(sample);
		if (style != bot.Personality.Style)
			return null;
		return style switch
		{
			CombatStyle.Melee => GearKind.MeleeWeapon,
			CombatStyle.Magic => GearKind.MagicWeapon,
			CombatStyle.Summon => GearKind.SummonWeapon,
			_ => null
		};
	}
}

public static class ProgressionGoals
{
	const uint GearBreakTicks = 7200;
	const uint GearBreakEvery = 3;

	static ProgressionGoal CactusGear(GearKind kind, int item, int cactus) =>
		new GatedGoal(new GearGoal(kind, item), bot => Inventory.Count(bot.Avatar, ItemID.Cactus) + Building.BaseStorage.Count(bot.Faction, ItemID.Cactus) >= cactus);

	public static bool OnGearBreak(Bot bot) => (Main.GameUpdateCount / GearBreakTicks + (uint)bot.Id) % GearBreakEvery == 0 && !CoreUnbuilt(bot);

	static bool CoreUnbuilt(Bot bot) =>
		HouseInspector.IsDesignatedBuilder(bot) && bot.Faction.Settlement is { } settlement
		&& !(HouseInspector.IsFinished(bot.Faction, settlement.Hall) && HouseInspector.IsFinished(bot.Faction, settlement.Storage));

	static readonly int[] Stone = { ItemID.StoneBlock };
	static readonly int[] Torches = { ItemID.Torch };

	const int CrowdedFreeSlots = 20;

	static bool StorageCrowded(Settlement settlement) =>
		BotWorldSystem.Factions.FirstOrDefault(faction => faction.Settlement == settlement) is { } owner
		&& HouseInspector.IsComplete(owner, settlement.Storage) && BaseStorage.FreeSlots(owner) < CrowdedFreeSlots;

	static readonly int[] ObsidianSkulls = { ItemID.ObsidianSkull, ItemID.ObsidianHorseshoe, ItemID.ObsidianShield, ItemID.AnkhShield, ItemID.ObsidianSkullRose, ItemID.MoltenSkullRose, ItemID.LavaSkull, ItemID.MoltenCharm };
	const int SkullObsidian = 20;

	static bool HasObsidianForSkull(Bot bot) =>
		Inventory.Count(bot.Avatar, ItemID.Obsidian) + BaseStorage.Count(bot.Faction, ItemID.Obsidian) >= SkullObsidian
		|| ObsidianSkulls.Any(skull => BaseStorage.Count(bot.Faction, skull) > 0);

	public static ProgressionGoal[] Build()
	{
		var goals = new List<ProgressionGoal>
		{
			new ClaimBoostersGoal(),
			new ManaCrystalGoal(),
			new StationGoal(TileID.WorkBenches, HouseDesign.WorkBenches),
			new ReserveGoal(ItemID.Torch, 3, 10),
			new HealingPotionGoal(),
			new GatedGoal(new ReserveGoal(ItemID.IronskinPotion, 2, 5), () => Terraria.Main.hardMode),
			new GatedGoal(new ReserveGoal(ItemID.RegenerationPotion, 2, 5), () => Terraria.Main.hardMode),
			new GatedGoal(new ReserveGoal(ItemID.InfernoPotion, 2, 5), () => Terraria.NPC.downedGolemBoss),
			new StructureGoal(settlement => new[] { settlement.Hall }),
			new ReserveGoal(ItemID.Torch, 5, 15, onlyWhenHolding: ItemID.Gel),
			new SupportConstructionGoal(),
			new ClaimOutpostGoal(),
			new OutpostGoal(),
			new ArenaKitGoal(),
			new SkyArenaGoal(),
			new RoadFirstGoal(),
			new RallyRouteGoal(),
			new BossGoal(),
			new BossSupportGoal(),
			new ArenaOutpostGoal(),
			new RefightGoal(gearOnly: true),
			new HellevatorGoal(),
			new ShaftRopeGoal(),
			new HellbridgeGoal(),
			new StyleGoal(new GearGoal(GearKind.RangedWeapon, ItemID.WoodenBow), CombatStyle.Ranged),
			new AmmoGoal(),
			new HandSummonGoal(),
			new ShareSuppliesGoal(),
			new ChampionCraftGoal(),
			new ChampionPoolGoal(),
			new HandDownGoal(),
			new ShoppingGoal(),
			new GearGoal(GearKind.Head, ItemID.WoodHelmet),
			new GearGoal(GearKind.Body, ItemID.WoodBreastplate),
			CactusGear(GearKind.Head, ItemID.CactusHelmet, 20),
			CactusGear(GearKind.Body, ItemID.CactusBreastplate, 30),
			CactusGear(GearKind.Legs, ItemID.CactusLeggings, 25),
			new StationGoal(TileID.Furnaces, ItemID.Furnace),
			new StationGoal(TileID.Anvils, ItemID.IronAnvil, ItemID.LeadAnvil),
			new StationGoal(TileID.TinkerersWorkbench, ItemID.TinkerersWorkshop),
			new StationGoal(TileID.TeleportationPylon, ItemID.TeleportationPylonPurity, ItemID.TeleportationPylonSnow, ItemID.TeleportationPylonDesert,
				ItemID.TeleportationPylonJungle, ItemID.TeleportationPylonOcean, ItemID.TeleportationPylonHallow, ItemID.TeleportationPylonUnderground, ItemID.TeleportationPylonMushroom)
		};
		goals.Add(new StructureGoal(settlement => new[] { settlement.Storage }));
		goals.Add(new StructureGoal(settlement => new[] { settlement.Workshop }));
		goals.Add(new OutpostKitGoal());
		goals.Add(new StorageGoal());
		goals.Add(new StructureGoal(settlement => StorageCrowded(settlement) ? settlement.StorageAnnexes : System.Array.Empty<HouseBlueprint>()));
		goals.Add(new RopeGoal());
		goals.Add(new GardenSeedGoal());
		goals.Add(new GardenGoal());
		goals.Add(new DyeGoal());
		goals.Add(new StockpileGoal("Stone", 200, 500, bot => Stone));
		goals.Add(new StockpileGoal("Torch", 40, 100, bot => Torches));
		goals.Add(new StockpileGoal("Arrows", 200, 500, bot => StockpileGoal.FactionAmmo(bot, AmmoID.Arrow)));
		goals.Add(new StockpileGoal("Bullets", 200, 500, bot => StockpileGoal.FactionAmmo(bot, AmmoID.Bullet)));
		goals.Add(new StructureGoal(settlement => settlement.Bedrooms));
		goals.Add(new StructureGoal(settlement => settlement.GuestRooms));
		goals.Add(new FurnishBedroomsGoal());
		goals.Add(new StructureGoal(settlement => settlement.StorageAnnexes.Append(settlement.Gallery)));
		goals.Add(new CraftableUpgradeGoal());
		goals.Add(new StyleGoal(new GearGoal(GearKind.MeleeWeapon, ItemID.WoodenSword, ItemID.CactusSword, ItemID.CopperShortsword, ItemID.TinShortsword, ItemID.CopperBroadsword, ItemID.TinBroadsword), CombatStyle.Ranged, CombatStyle.Magic));
		goals.Add(new StyleGoal(new GearGoal(GearKind.RangedWeapon, ItemID.WoodenBow), CombatStyle.Melee, CombatStyle.Magic));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.IronPickaxe, ItemID.LeadPickaxe },
			swords: new int[] { ItemID.IronBroadsword, ItemID.LeadBroadsword },
			bows: new int[] { ItemID.IronBow, ItemID.LeadBow },
			staffs: new int[] { ItemID.AmethystStaff, ItemID.TopazStaff },
			armor: new (int, int, int)[] { (ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves), (ItemID.LeadHelmet, ItemID.LeadChainmail, ItemID.LeadGreaves) });
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.SilverPickaxe, ItemID.TungstenPickaxe },
			swords: new int[] { ItemID.SilverBroadsword, ItemID.TungstenBroadsword },
			bows: new int[] { ItemID.SilverBow, ItemID.TungstenBow },
			staffs: new int[] { ItemID.SapphireStaff, ItemID.EmeraldStaff },
			armor: new (int, int, int)[] { (ItemID.SilverHelmet, ItemID.SilverChainmail, ItemID.SilverGreaves), (ItemID.TungstenHelmet, ItemID.TungstenChainmail, ItemID.TungstenGreaves) },
			leapWhen: BeatenEvilBoss);
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.GoldPickaxe, ItemID.PlatinumPickaxe },
			swords: new int[] { ItemID.GoldBroadsword, ItemID.PlatinumBroadsword },
			bows: new int[] { ItemID.GoldBow, ItemID.PlatinumBow },
			staffs: new int[] { ItemID.RubyStaff, ItemID.DiamondStaff },
			armor: new (int, int, int)[] { (ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves), (ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves) },
			leapWhen: BeatenEvilBoss);
		goals.Add(new SurveyGoal());
		goals.Add(new OwnGoal(ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.NightmarePickaxe, ItemID.DeathbringerPickaxe },
			swords: new int[] { ItemID.LightsBane, ItemID.BloodButcherer },
			bows: new int[] { ItemID.DemonBow, ItemID.TendonBow },
			staffs: new int[] { ItemID.Vilethorn, ItemID.CrimsonRod },
			armor: new (int, int, int)[] { (ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves), (ItemID.CrimsonHelmet, ItemID.CrimsonScalemail, ItemID.CrimsonGreaves) });
		goals.Add(new GatedGoal(new OwnGoal(ObsidianSkulls), HasObsidianForSkull));
		goals.Add(new StationGoal(TileID.Hellforge, ItemID.Hellforge));
		int moltenStart = goals.Count;
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.MoltenPickaxe },
			swords: new int[] { ItemID.FieryGreatsword },
			bows: new int[] { ItemID.MoltenFury },
			staffs: new int[] { ItemID.Flamelash },
			armor: new (int, int, int)[] { (ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves) },
			leapWhen: ReadyForWall);
		foreach (var pickaxe in goals.Skip(moltenStart).Select(Unwrap).OfType<GearGoal>().Where(gear => gear.Kind == GearKind.Pickaxe))
			pickaxe.LeapWhen = faction => !Main.hardMode && ReadyForWall(faction);
		goals.Add(new AspirationGoal(Aspiration.Collector, new OwnGoal(ItemID.Mannequin)));
		goals.Add(new AspirationGoal(Aspiration.Naturalist, new OwnGoal(ItemID.BugNet, ItemID.GoldenBugNet)));
		goals.Add(new AspirationGoal(Aspiration.Naturalist, new CageGoal()));
		goals.Add(new AspirationGoal(Aspiration.Angler, new OwnGoal(ItemID.ReinforcedFishingPole, ItemID.FisherofSouls, ItemID.Fleshcatcher, ItemID.WoodFishingPole)));
		goals.Add(new RefightGoal(gearOnly: false));
		int hardmodeStart = goals.Count;
		goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, ItemID.ImpStaff), CombatStyle.Summon));
		goals.Add(new SmashAltarsGoal());
		goals.Add(new WaterproofTorchGoal());
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.CobaltPickaxe, ItemID.PalladiumPickaxe },
			swords: new int[] { ItemID.CobaltSword, ItemID.PalladiumSword },
			bows: new int[] { ItemID.CobaltRepeater, ItemID.PalladiumRepeater },
			staffs: new int[] { ItemID.CrystalStorm },
			armor: new (int, int, int)[]
			{
				(ItemID.CobaltHelmet, ItemID.CobaltBreastplate, ItemID.CobaltLeggings), (ItemID.CobaltMask, ItemID.CobaltBreastplate, ItemID.CobaltLeggings), (ItemID.CobaltHat, ItemID.CobaltBreastplate, ItemID.CobaltLeggings),
				(ItemID.PalladiumMask, ItemID.PalladiumBreastplate, ItemID.PalladiumLeggings), (ItemID.PalladiumHelmet, ItemID.PalladiumBreastplate, ItemID.PalladiumLeggings), (ItemID.PalladiumHeadgear, ItemID.PalladiumBreastplate, ItemID.PalladiumLeggings)
			});
		goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, ItemID.SpiderStaff), CombatStyle.Summon));
		goals.Add(new StationGoal(TileID.MythrilAnvil, ItemID.MythrilAnvil, ItemID.OrichalcumAnvil));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.MythrilPickaxe, ItemID.OrichalcumPickaxe },
			swords: new int[] { ItemID.MythrilSword, ItemID.OrichalcumSword },
			bows: new int[] { ItemID.MythrilRepeater, ItemID.OrichalcumRepeater },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[]
			{
				(ItemID.MythrilHelmet, ItemID.MythrilChainmail, ItemID.MythrilGreaves), (ItemID.MythrilHat, ItemID.MythrilChainmail, ItemID.MythrilGreaves), (ItemID.MythrilHood, ItemID.MythrilChainmail, ItemID.MythrilGreaves),
				(ItemID.OrichalcumMask, ItemID.OrichalcumBreastplate, ItemID.OrichalcumLeggings), (ItemID.OrichalcumHelmet, ItemID.OrichalcumBreastplate, ItemID.OrichalcumLeggings), (ItemID.OrichalcumHeadgear, ItemID.OrichalcumBreastplate, ItemID.OrichalcumLeggings)
			}, leapWhen: BeatenMechBoss);
		goals.Add(new StationGoal(TileID.AdamantiteForge, ItemID.AdamantiteForge, ItemID.TitaniumForge));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.AdamantitePickaxe, ItemID.TitaniumPickaxe },
			swords: new int[] { ItemID.AdamantiteSword, ItemID.TitaniumSword },
			bows: new int[] { ItemID.AdamantiteRepeater, ItemID.TitaniumRepeater },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[]
			{
				(ItemID.AdamantiteHelmet, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings), (ItemID.AdamantiteMask, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings), (ItemID.AdamantiteHeadgear, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings),
				(ItemID.TitaniumMask, ItemID.TitaniumBreastplate, ItemID.TitaniumLeggings), (ItemID.TitaniumHelmet, ItemID.TitaniumBreastplate, ItemID.TitaniumLeggings), (ItemID.TitaniumHeadgear, ItemID.TitaniumBreastplate, ItemID.TitaniumLeggings)
			}, leapWhen: BeatenMechBoss);
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.PickaxeAxe, ItemID.Drax },
			swords: new int[] { ItemID.Excalibur },
			bows: new int[] { ItemID.HallowedRepeater },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[] { (ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHelmet, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHeadgear, ItemID.HallowedPlateMail, ItemID.HallowedGreaves) });
		goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, ItemID.OpticStaff), CombatStyle.Summon));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.ChlorophytePickaxe },
			swords: new int[] { ItemID.ChlorophyteClaymore, ItemID.ChlorophyteSaber },
			bows: new int[] { ItemID.ChlorophyteShotbow },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[] { (ItemID.ChlorophyteMask, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHelmet, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHeadgear, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves) });
		goals.Add(new OwnGoal(ItemID.AngelWings, ItemID.DemonWings));
		goals.Add(new GearGoal(GearKind.Body, ItemID.SolarFlareBreastplate));
		goals.Add(new GearGoal(GearKind.Head, ItemID.SolarFlareHelmet));
		goals.Add(new GearGoal(GearKind.Legs, ItemID.SolarFlareLeggings));
		goals.Add(new OwnGoal(ItemID.TerrasparkBoots));
		goals.Add(new OwnGoal(ItemID.AnkhShield));
		goals.Add(new OwnGoal(ItemID.CellPhone));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.BerserkerGlove), CombatStyle.Melee));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.FireGauntlet), CombatStyle.Melee));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.SniperScope), CombatStyle.Ranged));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.MoltenQuiver), CombatStyle.Ranged));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.CelestialEmblem), CombatStyle.Magic));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.CelestialShell), CombatStyle.Magic));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.PapyrusScarab), CombatStyle.Summon));
		goals.Add(new StyleGoal(new OwnGoal(ItemID.NecromanticScroll), CombatStyle.Summon));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MeleeWeapon, ItemID.Zenith), CombatStyle.Melee));
		goals.Add(new StyleGoal(new GearGoal(GearKind.RangedWeapon, ItemID.VortexBeater), CombatStyle.Ranged));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MagicWeapon, ItemID.LastPrism), CombatStyle.Magic));
		for (int i = hardmodeStart; i < goals.Count; i++)
			goals[i] = new GatedGoal(goals[i], () => Terraria.Main.hardMode);
		LinkSuccessors(goals);
		if (UpgradeScenarios.GearFirst)
		{
			var gear = goals.Where(goal => Unwrap(goal) is StationGoal or GearGoal or SmashAltarsGoal).ToList();
			goals.RemoveAll(gear.Contains);
			goals.InsertRange(0, gear);
		}
		if (UpgradeScenarios.Focus is int[] focus)
			goals.Insert(0, new OwnGoal(focus));
		goals.Add(new HighwayGoal());
		goals.Add(new BridgeKitGoal());
		foreach (int staff in new[] { ItemID.ImpStaff, ItemID.SpiderStaff, ItemID.OpticStaff })
			goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, staff), CombatStyle.Melee, CombatStyle.Ranged, CombatStyle.Magic));
		goals.AddRange(goals.OfType<AspirationGoal>().Select(goal => goal.AsFallback()).ToList());
		if (HeadlessTestMode.BossFirst)
		{
			var boss = goals.OfType<BossGoal>().First();
			var shaft = goals.OfType<HellevatorGoal>().First();
			var bridge = goals.OfType<HellbridgeGoal>().First();
			goals.Remove(boss);
			goals.Remove(shaft);
			goals.Remove(bridge);
			goals.InsertRange(0, new ProgressionGoal[] { boss, shaft, bridge });
		}
		return goals.ToArray();
	}

	internal static ProgressionGoal Unwrap(ProgressionGoal goal) => goal switch
	{
		GatedGoal gated => Unwrap(gated.Inner),
		AspirationGoal aspiration => Unwrap(aspiration.Inner),
		StyleGoal style => Unwrap(style.Inner),
		_ => goal
	};

	static HashSet<CombatStyle> StylesOf(ProgressionGoal goal) => goal switch
	{
		GatedGoal gated => StylesOf(gated.Inner),
		StyleGoal style => style.Styles,
		_ => null
	};

	static void LinkSuccessors(List<ProgressionGoal> goals)
	{
		var gear = goals.Select(goal => (gear: Unwrap(goal) as GearGoal, styles: StylesOf(goal))).Where(entry => entry.gear != null).ToList();
		for (int i = 0; i < gear.Count; i++)
		{
			var (goal, styles) = gear[i];
			goal.Successors.AddRange(gear.Skip(i + 1)
				.Where(later => later.gear.Kind == goal.Kind && (later.styles == null || styles == null || later.styles.SetEquals(styles)))
				.Select(later => later.gear));
		}
	}

	static readonly CombatStyle[] HeadStyles = { CombatStyle.Melee, CombatStyle.Ranged, CombatStyle.Magic };

	static readonly int[] Hooks =
	{
		ItemID.GrapplingHook, ItemID.AmethystHook, ItemID.TopazHook, ItemID.SapphireHook, ItemID.EmeraldHook, ItemID.RubyHook, ItemID.DiamondHook
	};

	static bool Beaten(Factions.Faction faction, int npcType) =>
		Bosses.BossCatalog.ChallengeOf(npcType) is { } challenge && Bosses.BossCredit.HasDefeated(faction, challenge);

	static bool BeatenEvilBoss(Factions.Faction faction) =>
		Beaten(faction, NPCID.EyeofCthulhu) && (Beaten(faction, NPCID.EaterofWorldsHead) || Beaten(faction, NPCID.BrainofCthulhu));

	static bool ReadyForWall(Factions.Faction faction) => Beaten(faction, NPCID.WallofFlesh) || Bosses.BossRituals.ReadyForWall(faction);

	static bool BeatenMechBoss(Factions.Faction faction) =>
		Beaten(faction, NPCID.TheDestroyer) || Beaten(faction, NPCID.Retinazer) || Beaten(faction, NPCID.SkeletronPrime);

	static void AddOreTier(List<ProgressionGoal> goals, int[] pickaxes, int[] swords, int[] bows, int[] staffs, (int head, int body, int legs)[] armor,
		System.Func<Factions.Faction, bool> leapWhen = null)
	{
		int first = goals.Count;
		AddOreTierGoals(goals, pickaxes, swords, bows, staffs, armor);
		foreach (var gear in goals.Skip(first).Select(Unwrap).OfType<GearGoal>().Where(gear => gear.Kind != GearKind.Hook))
			gear.LeapWhen = leapWhen;
	}

	static void AddOreTierGoals(List<ProgressionGoal> goals, int[] pickaxes, int[] swords, int[] bows, int[] staffs, (int head, int body, int legs)[] armor)
	{
		goals.Add(new GearGoal(GearKind.Pickaxe, pickaxes));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MeleeWeapon, swords), CombatStyle.Melee, CombatStyle.Ranged, CombatStyle.Magic));
		goals.Add(new StyleGoal(new GearGoal(GearKind.RangedWeapon, bows), CombatStyle.Ranged, CombatStyle.Melee, CombatStyle.Magic));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MagicWeapon, staffs), CombatStyle.Magic, CombatStyle.Melee, CombatStyle.Ranged));
		goals.Add(new GearGoal(GearKind.Body, armor.Select(set => set.body).ToArray()));
		if (armor.Length % HeadStyles.Length == 0 && armor.Length > HeadStyles.Length - 1)
		{
			for (int style = 0; style < HeadStyles.Length; style++)
			{
				var heads = armor.Where((set, index) => index % HeadStyles.Length == style).Select(set => set.head).ToArray();
				goals.Add(new StyleGoal(new GearGoal(GearKind.Head, heads), HeadStyles[style]));
			}
		}
		else
		{
			goals.Add(new GearGoal(GearKind.Head, armor.Select(set => set.head).ToArray()));
		}
		goals.Add(new GearGoal(GearKind.Legs, armor.Select(set => set.legs).ToArray()));
		bool hookPlanned = goals.OfType<GearGoal>().Any(goal => goal.Kind == GearKind.Hook);
		if (!hookPlanned && goals.Select(Unwrap).OfType<GearGoal>().Count(goal => goal.Kind == GearKind.Legs) >= HookAfterArmorTiers)
			goals.Add(new GearGoal(GearKind.Hook, Hooks));
	}

	const int HookAfterArmorTiers = 2;
}
