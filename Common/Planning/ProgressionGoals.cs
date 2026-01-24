using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Systems;
using Terraria.ID;

namespace AutoTerraMod.Common.Planning;

public abstract class ProgressionGoal
{
	public virtual bool AppliesTo(Bot bot) => true;
	public virtual string Describe() => GetType().Name;
	public abstract bool IsMet(Bot bot);
	public abstract PlanStep Plan(Bot bot);
	public virtual bool IsExpedition => false;
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

	public override bool IsMet(Bot bot) => Equipment.BestPower(bot.Avatar, kind) >= requiredPower || Outgrown(bot);
	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAny(bot, options);

	public List<GearGoal> Successors { get; } = new();

	const int OutgrownCacheTicks = 600;

	readonly Dictionary<int, (uint at, bool outgrown)> outgrownCache = new();

	bool Outgrown(Bot bot)
	{
		if (Successors.Count == 0)
			return false;
		if (outgrownCache.TryGetValue(bot.Id, out var cached) && Terraria.Main.GameUpdateCount - cached.at < OutgrownCacheTicks)
			return cached.outgrown;
		bool outgrown = Successors.Any(successor => successor.ReadyToMake(bot));
		outgrownCache[bot.Id] = (Terraria.Main.GameUpdateCount, outgrown);
		return outgrown;
	}

	bool ReadyToMake(Bot bot) =>
		Equipment.BestPower(bot.Avatar, kind) < requiredPower && ProgressionPlanner.ObtainAny(bot, options) is CraftStep or WithdrawStep or PlaceStationStep;
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

public sealed class SupportConstructionGoal : ProgressionGoal
{
	const float MinimumCooperation = 0.4f;
	const int BatchSize = 40;

	public override bool AppliesTo(Bot bot) =>
		bot.Faction.Settlement != null && bot.Personality.Cooperation >= MinimumCooperation && !HouseInspector.IsDesignatedBuilder(bot);

	public override bool IsMet(Bot bot) => Need(bot, out _) == null;

	public override PlanStep Plan(Bot bot)
	{
		if (Need(bot, out var builder) is not BillLine need)
			return PlanStep.Idle;
		int batch = System.Math.Min(need.Amount, BatchSize);
		if (Inventory.Count(bot.Avatar, need.ItemType) < batch)
			return ProgressionPlanner.Obtain(bot, need.ItemType, batch);
		bool canHandOver = BaseStorage.HasRoom(bot.Faction) || Inventory.UsedSlots(builder.Avatar) < Inventory.MainSlotCount;
		return canHandOver ? new DeliverStep(need.ItemType, builder.Id) : PlanStep.Idle;
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
		return ConstructionBill.FirstShortfall(builder, ConstructionBill.For(structure, materials), materials);
	}
}

public sealed class BossGoal : ProgressionGoal
{
	public override bool IsExpedition => true;

	const int ReadinessLogIntervalTicks = 1800;

	public override bool AppliesTo(Bot bot) => Bosses.BossCredit.NextChallengeFor(bot.Faction) != null && IsSummoner(bot);

	public override bool IsMet(Bot bot) => Bosses.BossCredit.NextChallengeFor(bot.Faction) == null;

	public override string Describe() => "Boss";

	public static bool IsSummoner(Bot bot) =>
		BotWorldSystem.Bots.Where(member => member.FactionId == bot.FactionId && member.IsAlive).MaxBy(member => member.Personality.Combat) == bot;

	public override PlanStep Plan(Bot bot)
	{
		if (Terraria.Main.GameUpdateCount < bot.Faction.RallyCooldownUntil)
			return PlanStep.Idle;
		foreach (var challenge in Bosses.BossCredit.OpenChallengesFor(bot.Faction))
		{
			if (Bosses.BossCatalog.IsBossAlive(challenge))
				return PlanStep.Idle;
			var report = Bosses.BossReadiness.Evaluate(bot.Faction, challenge, bot);
			LogReadiness(bot, challenge, report);
			if (!report.IsReady)
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

	readonly Dictionary<int, uint> lastReadinessLog = new();

	void LogReadiness(Bot bot, Bosses.BossChallenge challenge, Bosses.ReadinessReport report)
	{
		if (lastReadinessLog.TryGetValue(challenge.BossType, out uint loggedAt) && Terraria.Main.GameUpdateCount - loggedAt < ReadinessLogIntervalTicks)
			return;
		lastReadinessLog[challenge.BossType] = Terraria.Main.GameUpdateCount;
		BotDiagnostics.Log(bot, $"boss readiness for {challenge.Name}: fighters={report.Fighters} teamDps={report.TeamDps:0} bossLife={report.BossLife:0} kill={report.KillSeconds:0}s survive={report.SurviveSeconds:0}s needRatio={report.RequiredRatio:0.0} ready={report.IsReady}");
	}
}

public sealed class HellevatorGoal : ProgressionGoal
{
	public override bool IsExpedition => true;

	public override bool AppliesTo(Bot bot) => Bosses.BossRituals.PreparingForWall(bot.Faction);

	public override bool IsMet(Bot bot) => bot.Faction.Hellevator?.IsComplete == true;

	public override PlanStep Plan(Bot bot) => Bosses.BossRituals.PlanShaftWork(bot);
}

public sealed class HellbridgeGoal : ProgressionGoal
{
	public override bool IsExpedition => true;

	public override bool AppliesTo(Bot bot) => Bosses.BossRituals.PreparingForWall(bot.Faction);

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

	public override string Describe() => $"Own({Terraria.Lang.GetItemNameValue(options[0])})";

	public override bool IsMet(Bot bot) =>
		options.Any(option => Inventory.Count(bot.Avatar, option) > 0 || bot.Avatar.armor.Any(worn => worn.type == option));

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAny(bot, options);
}

public sealed class GatedGoal : ProgressionGoal
{
	readonly ProgressionGoal inner;
	readonly Func<bool> open;

	public GatedGoal(ProgressionGoal inner, Func<bool> open)
	{
		this.inner = inner;
		this.open = open;
	}

	public ProgressionGoal Inner => inner;
	public override bool AppliesTo(Bot bot) => open() && inner.AppliesTo(bot);
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);
	public override bool IsExpedition => inner.IsExpedition;
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
		return knowledge.NearestKnown(bot.Faction.BaseTile, AltarTiles, new HashSet<Microsoft.Xna.Framework.Point>(), AltarSearchRadius) is Microsoft.Xna.Framework.Point altar
			? new SmashShrineStep(altar)
			: new ScoutStep(World.Landmarks.WorldEvil, true);
	}
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
			new SupportConstructionGoal(),
			new BossGoal(),
			new HellevatorGoal(),
			new HellbridgeGoal(),
			new StyleGoal(new GearGoal(GearKind.RangedWeapon, ItemID.WoodenBow), CombatStyle.Ranged),
			new StyleGoal(new ReserveGoal(ItemID.WoodenArrow, 60, 120), CombatStyle.Ranged),
			new GearGoal(GearKind.Head, ItemID.WoodHelmet),
			new GearGoal(GearKind.Body, ItemID.WoodBreastplate),
			new StationGoal(TileID.Furnaces, ItemID.Furnace),
			new StationGoal(TileID.Anvils, ItemID.IronAnvil, ItemID.LeadAnvil),
			new StationGoal(TileID.TinkerersWorkbench, ItemID.TinkerersWorkshop)
		};
		goals.Add(new StructureGoal(settlement => new[] { settlement.Storage }));
		goals.Add(new StructureGoal(settlement => new[] { settlement.Workshop }));
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
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.NightmarePickaxe, ItemID.DeathbringerPickaxe },
			swords: new int[] { ItemID.LightsBane, ItemID.BloodButcherer },
			bows: new int[] { ItemID.DemonBow, ItemID.TendonBow },
			staffs: new int[] { ItemID.Vilethorn, ItemID.CrimsonRod },
			armor: new (int, int, int)[] { (ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves), (ItemID.CrimsonHelmet, ItemID.CrimsonScalemail, ItemID.CrimsonGreaves) });
		goals.Add(new StationGoal(TileID.Hellforge, ItemID.Hellforge));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.MoltenPickaxe },
			swords: new int[] { ItemID.FieryGreatsword },
			bows: new int[] { ItemID.MoltenFury },
			staffs: new int[] { ItemID.Flamelash },
			armor: new (int, int, int)[] { (ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves) });
		int hardmodeStart = goals.Count;
		goals.Add(new SmashAltarsGoal());
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
			});
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
			});
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.PickaxeAxe, ItemID.Drax },
			swords: new int[] { ItemID.Excalibur },
			bows: new int[] { ItemID.HallowedRepeater },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[] { (ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHelmet, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHeadgear, ItemID.HallowedPlateMail, ItemID.HallowedGreaves) });
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.ChlorophytePickaxe },
			swords: new int[] { ItemID.ChlorophyteClaymore, ItemID.ChlorophyteSaber },
			bows: new int[] { ItemID.ChlorophyteShotbow },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[] { (ItemID.ChlorophyteMask, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHelmet, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHeadgear, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves) });
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
		goals.Add(new StyleGoal(new OwnGoal(ItemID.EmpressBlade), CombatStyle.Summon));
		for (int i = hardmodeStart; i < goals.Count; i++)
			goals[i] = new GatedGoal(goals[i], () => Terraria.Main.hardMode);
		LinkSuccessors(goals);
		if (UpgradeScenarios.GearFirst)
		{
			var gear = goals.Where(goal => Unwrap(goal) is StationGoal or GearGoal or SmashAltarsGoal).ToList();
			goals.RemoveAll(gear.Contains);
			goals.InsertRange(0, gear);
		}
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

	static void AddOreTier(List<ProgressionGoal> goals, int[] pickaxes, int[] swords, int[] bows, int[] staffs, (int head, int body, int legs)[] armor)
	{
		goals.Add(new GearGoal(GearKind.Pickaxe, pickaxes));
		if (!goals.OfType<GearGoal>().Any(goal => goal.Kind == GearKind.Hook))
			goals.Add(new GearGoal(GearKind.Hook, Hooks));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MeleeWeapon, swords), CombatStyle.Melee));
		goals.Add(new StyleGoal(new GearGoal(GearKind.RangedWeapon, bows), CombatStyle.Ranged));
		goals.Add(new StyleGoal(new GearGoal(GearKind.MagicWeapon, staffs), CombatStyle.Magic));
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
	}
}
