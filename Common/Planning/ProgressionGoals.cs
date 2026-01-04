using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using Terraria.ID;

namespace AutoTerraMod.Common.Planning;

public abstract class ProgressionGoal
{
	public virtual bool AppliesTo(Bot bot) => true;
	public virtual string Describe() => GetType().Name;
	public abstract bool IsMet(Bot bot);
	public abstract PlanStep Plan(Bot bot);
}

public sealed class StationGoal : ProgressionGoal
{
	readonly int tileType;
	readonly int[] stationItems;

	public StationGoal(int tileType, params int[] stationItems)
	{
		this.tileType = tileType;
		this.stationItems = stationItems;
	}

	public override bool IsMet(Bot bot) => Stations.BaseHas(bot.Faction, tileType);
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.PlanStation(bot, stationItems);
}

public sealed class GearGoal : ProgressionGoal
{
	readonly GearKind kind;
	readonly int[] options;
	readonly int requiredPower;

	public GearKind Kind => kind;
	public override string Describe() => $"Gear({kind})";

	public GearGoal(GearKind kind, params int[] options)
	{
		this.kind = kind;
		this.options = options;
		requiredPower = Math.Max(1, options.Min(option => Equipment.SamplePower(option, kind)));
	}

	public override bool IsMet(Bot bot) => Equipment.BestPower(bot.Avatar, kind) >= requiredPower;
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAny(bot, options);
}

public sealed class ReserveGoal : ProgressionGoal
{
	readonly int itemType;
	readonly int minimum;
	readonly int restockTo;

	public ReserveGoal(int itemType, int minimum, int restockTo)
	{
		this.itemType = itemType;
		this.minimum = minimum;
		this.restockTo = restockTo;
	}

	public override bool IsMet(Bot bot) => Inventory.Count(bot.Avatar, itemType) >= minimum;
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.Obtain(bot, itemType, restockTo);
}

public sealed class RopeGoal : ProgressionGoal
{
	const int Minimum = 20;
	const int RestockTo = 60;

	public override bool IsMet(Bot bot) => RopeCount(bot.Avatar) >= Minimum;

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.Obtain(bot, ItemID.WebRope, RestockTo);

	static int RopeCount(Terraria.Player avatar) =>
		Inventory.Items(avatar).Where(item => item.createTile >= 0 && Terraria.Main.tileRope[item.createTile]).Sum(item => item.stack);
}

public sealed class StorageGoal : ProgressionGoal
{
	const int MinimumChests = 2;

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && HouseInspector.IsComplete(bot.Faction, bot.Faction.Settlement.Storage);

	public override bool IsMet(Bot bot)
	{
		int chests = BaseStorage.ChestIndices(bot.Faction).Count;
		return chests >= MinimumChests && chests > BaseStorage.FullChestCount(bot.Faction);
	}

	public override PlanStep Plan(Bot bot)
	{
		var held = Inventory.Items(bot.Avatar).FirstOrDefault(BaseStorage.IsStorageItem);
		return held != null ? new PlaceStationStep(held.type) : ProgressionPlanner.Obtain(bot, ItemID.Chest, 1);
	}
}

public sealed class ClaimBoostersGoal : ProgressionGoal
{
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
	readonly System.Func<Settlement, IEnumerable<HouseBlueprint>> select;

	public StructureGoal(System.Func<Settlement, IEnumerable<HouseBlueprint>> select) => this.select = select;

	public override bool AppliesTo(Bot bot) => bot.Faction.Settlement != null && HouseInspector.IsDesignatedBuilder(bot);

	public override bool IsMet(Bot bot) => NextUnfinished(bot) == null;

	HouseBlueprint NextUnfinished(Bot bot) =>
		select(bot.Faction.Settlement).FirstOrDefault(structure => !HouseInspector.IsFinished(bot.Faction, structure));

	public override PlanStep Plan(Bot bot)
	{
		var structure = NextUnfinished(bot);
		if (structure == null)
			return PlanStep.Idle;
		if (HouseInspector.NextOperation(bot.Faction, structure) is not BuildOperation operation)
		{
			HouseInspector.ReportUnfinishable(bot, structure);
			return PlanStep.Idle;
		}
		var materials = HouseInspector.MaterialsFor(bot.Faction);
		var placement = HouseMaterials.ReplacementFor(operation.Action);
		if (!HouseMaterials.IsNeeded(placement))
			return new BuildHouseStep(operation);
		var bill = ConstructionBill.For(structure, materials);
		if (ConstructionBill.FirstShortfall(bot, bill, materials) is BillLine shortfall)
			return ProgressionPlanner.Obtain(bot, shortfall.ItemType, shortfall.Amount);
		if (HouseMaterials.Find(bot.Avatar, placement, materials) != null)
			return new BuildHouseStep(operation);
		var line = bill.First(entry => entry.Placement == placement);
		return ProgressionPlanner.Obtain(bot, line.ItemType, line.Amount);
	}
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

	public override bool AppliesTo(Bot bot) => styles.Contains(EffectiveStyle(bot.Personality.Style));
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);

	public static CombatStyle EffectiveStyle(CombatStyle style) => style == CombatStyle.Summon ? CombatStyle.Melee : style;
}

public static class ProgressionGoals
{
	public static ProgressionGoal[] Build()
	{
		var goals = new List<ProgressionGoal>
		{
			new ClaimBoostersGoal(),
			new ManaCrystalGoal(),
			new StationGoal(TileID.WorkBenches, ItemID.WorkBench),
			new ReserveGoal(ItemID.Torch, 10, 30),
			new StructureGoal(settlement => new[] { settlement.Hall }),
			new StyleGoal(new GearGoal(GearKind.RangedWeapon, ItemID.WoodenBow), CombatStyle.Ranged),
			new StyleGoal(new ReserveGoal(ItemID.WoodenArrow, 60, 120), CombatStyle.Ranged),
			new GearGoal(GearKind.Head, ItemID.WoodHelmet),
			new GearGoal(GearKind.Body, ItemID.WoodBreastplate),
			new StationGoal(TileID.Furnaces, ItemID.Furnace),
			new StationGoal(TileID.Anvils, ItemID.IronAnvil, ItemID.LeadAnvil)
		};
		goals.Add(new StructureGoal(settlement => new[] { settlement.Storage }));
		goals.Add(new StorageGoal());
		goals.Add(new RopeGoal());
		goals.Add(new StructureGoal(settlement => settlement.Bedrooms));
		goals.Add(new StructureGoal(settlement => settlement.GuestRooms));
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
			armor: new (int, int, int)[] { (ItemID.SilverHelmet, ItemID.SilverChainmail, ItemID.SilverGreaves), (ItemID.TungstenHelmet, ItemID.TungstenChainmail, ItemID.TungstenGreaves) });
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.GoldPickaxe, ItemID.PlatinumPickaxe },
			swords: new int[] { ItemID.GoldBroadsword, ItemID.PlatinumBroadsword },
			bows: new int[] { ItemID.GoldBow, ItemID.PlatinumBow },
			staffs: new int[] { ItemID.RubyStaff, ItemID.DiamondStaff },
			armor: new (int, int, int)[] { (ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves), (ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves) });
		return goals.ToArray();
	}

	static readonly int[] Hooks =
	{
		ItemID.GrapplingHook, ItemID.AmethystHook, ItemID.TopazHook, ItemID.SapphireHook, ItemID.EmeraldHook, ItemID.RubyHook, ItemID.DiamondHook
	};

	static void AddOreTier(List<ProgressionGoal> goals, int[] pickaxes, int[] swords, int[] bows, int[] staffs, (int head, int body, int legs)[] armor)
	{
		goals.Add(new GearGoal(GearKind.Pickaxe, pickaxes));
		if (!goals.OfType<GearGoal>().Any(goal => goal.Kind == GearKind.Hook))
			goals.Add(new GearGoal(GearKind.Hook, Hooks));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MeleeWeapon, swords), CombatStyle.Melee));
		goals.Add(new StyleGoal(new GearGoal(GearKind.RangedWeapon, bows), CombatStyle.Ranged));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MagicWeapon, staffs), CombatStyle.Magic));
		goals.Add(new GearGoal(GearKind.Body, armor.Select(set => set.body).ToArray()));
		goals.Add(new GearGoal(GearKind.Head, armor.Select(set => set.head).ToArray()));
		goals.Add(new GearGoal(GearKind.Legs, armor.Select(set => set.legs).ToArray()));
	}
}
