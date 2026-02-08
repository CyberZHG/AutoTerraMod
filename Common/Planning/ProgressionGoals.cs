using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Planning;

public abstract class ProgressionGoal
{
	public virtual bool AppliesTo(Bot bot) => true;
	public virtual bool IsUrgent(Bot bot) => false;
	public virtual string Describe() => GetType().Name;
	public abstract bool IsMet(Bot bot);
	public abstract PlanStep Plan(Bot bot);
	public virtual bool IsExpedition => false;

	string probeLabel;
	public string ProbeLabel => probeLabel ??= "g:" + ProgressionGoals.Unwrap(this).GetType().Name;
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
		int lifetime = OutgrownCacheTicks + (bot.Id * 131 + requiredPower * 17) % OutgrownCacheTicks;
		if (outgrownCache.TryGetValue(bot.Id, out var cached) && Terraria.Main.GameUpdateCount - cached.at < lifetime)
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

public sealed class AmmoGoal : ProgressionGoal
{
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

	public override PlanStep Plan(Bot bot) => ProgressionPlanner.ObtainAnyAmount(bot, OptionsFor(AmmoClass(bot)), RestockTo);
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

	public AspirationGoal(Aspiration aspiration, ProgressionGoal inner)
	{
		this.aspiration = aspiration;
		this.inner = inner;
	}

	public ProgressionGoal Inner => inner;
	public override bool AppliesTo(Bot bot) => bot.Personality.Aspiration == aspiration && inner.AppliesTo(bot);
	public override string Describe() => inner.Describe();
	public override bool IsMet(Bot bot) => inner.IsMet(bot);
	public override PlanStep Plan(Bot bot) => inner.Plan(bot);
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

public sealed class StockpileGoal : ProgressionGoal
{
	readonly string name;
	readonly int floor;
	readonly int target;
	readonly System.Func<Bot, IReadOnlyList<int>> options;

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

public sealed class ShoppingGoal : ProgressionGoal
{
	const long CoinReserve = 5000;
	const int PotionStock = 10;
	const int PotionRestockBelow = 4;
	const float UpgradeRatio = 1.2f;
	const int ChoiceCacheTicks = 600;

	readonly Dictionary<int, (uint at, BuyStep choice)> choices = new();

	public override string Describe() => "Shopping";

	public override bool AppliesTo(Bot bot) => Coins(bot) > CoinReserve;

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
			if (gain <= bestGain || (long)price * amount > budget)
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
	static readonly int[] Wood = { ItemID.Wood };
	static readonly int[] Torches = { ItemID.Torch };

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
			new AmmoGoal(),
			new ShareSuppliesGoal(),
			new ShoppingGoal(),
			new GearGoal(GearKind.Head, ItemID.WoodHelmet),
			new GearGoal(GearKind.Body, ItemID.WoodBreastplate),
			new StationGoal(TileID.Furnaces, ItemID.Furnace),
			new StationGoal(TileID.Anvils, ItemID.IronAnvil, ItemID.LeadAnvil),
			new StationGoal(TileID.TinkerersWorkbench, ItemID.TinkerersWorkshop),
			new StationGoal(TileID.TeleportationPylon, ItemID.TeleportationPylonPurity, ItemID.TeleportationPylonSnow, ItemID.TeleportationPylonDesert,
				ItemID.TeleportationPylonJungle, ItemID.TeleportationPylonOcean, ItemID.TeleportationPylonHallow, ItemID.TeleportationPylonUnderground, ItemID.TeleportationPylonMushroom)
		};
		goals.Add(new StructureGoal(settlement => new[] { settlement.Storage }));
		goals.Add(new StructureGoal(settlement => new[] { settlement.Workshop }));
		goals.Add(new StorageGoal());
		goals.Add(new RopeGoal());
		goals.Add(new StockpileGoal("Wood", 150, 300, bot => Wood));
		goals.Add(new StockpileGoal("Torch", 40, 100, bot => Torches));
		goals.Add(new StockpileGoal("Arrows", 200, 500, bot => StockpileGoal.FactionAmmo(bot, AmmoID.Arrow)));
		goals.Add(new StockpileGoal("Bullets", 200, 500, bot => StockpileGoal.FactionAmmo(bot, AmmoID.Bullet)));
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
		goals.Add(new AspirationGoal(Aspiration.Collector, new OwnGoal(ItemID.Mannequin)));
		goals.Add(new AspirationGoal(Aspiration.Naturalist, new OwnGoal(ItemID.BugNet, ItemID.GoldenBugNet)));
		goals.Add(new AspirationGoal(Aspiration.Naturalist, new CageGoal()));
		goals.Add(new AspirationGoal(Aspiration.Angler, new OwnGoal(ItemID.ReinforcedFishingPole, ItemID.FisherofSouls, ItemID.Fleshcatcher, ItemID.WoodFishingPole)));
		int hardmodeStart = goals.Count;
		goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, ItemID.ImpStaff), CombatStyle.Summon));
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
		goals.Add(new StyleGoal(new GearGoal(GearKind.SummonWeapon, ItemID.OpticStaff), CombatStyle.Summon));
		AddOreTier(goals,
			pickaxes: new int[] { ItemID.ChlorophytePickaxe },
			swords: new int[] { ItemID.ChlorophyteClaymore, ItemID.ChlorophyteSaber },
			bows: new int[] { ItemID.ChlorophyteShotbow },
			staffs: new int[] { ItemID.CursedFlames, ItemID.GoldenShower },
			armor: new (int, int, int)[] { (ItemID.ChlorophyteMask, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHelmet, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves), (ItemID.ChlorophyteHeadgear, ItemID.ChlorophytePlateMail, ItemID.ChlorophyteGreaves) });
		goals.Add(new OwnGoal(ItemID.AngelWings, ItemID.DemonWings));
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
		if (UpgradeScenarios.Focus is int[] focus)
			goals.Insert(0, new OwnGoal(focus));
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
