using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Terraria;

namespace AutoTerraMod.Common.Planning;

public static class Champions
{
	public const uint AfterBossTicks = 3 * 3600;
	const uint CacheTicks = 300;
	const float DonorReach = 16f * 150f;

	static readonly Dictionary<int, (uint at, Dictionary<int, int> shortage)> pools = new();

	public static bool InWindow(Faction faction) => faction.Rally != null && !faction.RallyAssembled || Main.GameUpdateCount < faction.PoolUntil;

	static IEnumerable<Bot> Members(Faction faction) => BotWorldSystem.Bots.Where(member => member.FactionId == faction.Id && member.IsAlive);

	public static int Strength(Bot bot) =>
		CraftableUpgradeGoal.KindsFor(bot).Sum(kind => Equipment.BestPower(bot.Avatar, kind));

	static readonly Dictionary<int, (int strongest, int weakest)> picks = new();

	public static Bot Strongest(Faction faction) => Pick(faction).strongest;

	public static Bot Weakest(Faction faction) => Pick(faction).weakest;

	static (Bot strongest, Bot weakest) Pick(Faction faction)
	{
		if (!InWindow(faction))
		{
			picks.Remove(faction.Id);
			return (null, null);
		}
		if (picks.TryGetValue(faction.Id, out var ids) && BotWorldSystem.FindBot(ids.strongest) is { IsAlive: true } strongest && BotWorldSystem.FindBot(ids.weakest) is { IsAlive: true } weakest)
			return (strongest, weakest);
		var ranked = Members(faction).OrderBy(Strength).ToList();
		if (ranked.Count == 0)
			return (null, null);
		picks[faction.Id] = (ranked[^1].Id, ranked[0].Id);
		return (ranked[^1], ranked[0]);
	}

	public static bool IsMaterial(Item item) =>
		item.headSlot < 0 && item.bodySlot < 0 && item.legSlot < 0 && !item.accessory && item.damage <= 0 && item.pick <= 0 && item.axe <= 0 && item.hammer <= 0;

	public static Dictionary<int, int> Shortage(Bot champion)
	{
		if (pools.TryGetValue(champion.Id, out var cached) && Main.GameUpdateCount - cached.at < CacheTicks)
			return cached.shortage;
		var shortage = FindShortage(champion);
		pools[champion.Id] = (Main.GameUpdateCount, shortage);
		return shortage;
	}

	public static List<Bot> Donors(Bot champion) =>
		Members(champion.Faction).Where(member => member != champion && Microsoft.Xna.Framework.Vector2.Distance(member.Center, champion.Center) < DonorReach).ToList();

	public static int ShareOf(Bot donor, Bot champion, int itemType, int need)
	{
		foreach (var member in Donors(champion).OrderByDescending(member => Inventory.Count(member.Avatar, itemType)).ThenBy(member => member.Id))
		{
			int held = Inventory.Count(member.Avatar, itemType);
			if (member == donor)
				return System.Math.Min(held, need);
			need -= held;
			if (need <= 0)
				return 0;
		}
		return 0;
	}

	static Dictionary<int, int> FindShortage(Bot champion)
	{
		var teammates = Donors(champion);
		var best = new Dictionary<int, int>();
		int bestGain = 0;
		foreach (var recipe in CraftableUpgradeGoal.GearRecipes())
		{
			var result = recipe.createItem;
			int gain = CraftableUpgradeGoal.KindsFor(champion).Max(kind => Equipment.PowerOf(result, kind) - Equipment.BestPower(champion.Avatar, kind));
			if (gain <= bestGain || Crafting.HasIngredients(champion.Avatar, recipe))
				continue;
			var missing = new Dictionary<int, int>();
			bool coverable = true;
			foreach (var required in recipe.requiredItem)
			{
				var accepted = RecipeIndex.AcceptedTypes(recipe, required);
				int need = required.stack - Inventory.CountAny(champion.Avatar, accepted);
				if (need <= 0)
					continue;
				if (!IsMaterial(required))
				{
					coverable = false;
					break;
				}
				int type = accepted.MaxBy(candidate => teammates.Sum(member => Inventory.Count(member.Avatar, candidate)));
				if (teammates.Sum(member => Inventory.Count(member.Avatar, type)) < need)
				{
					coverable = false;
					break;
				}
				missing[type] = need;
			}
			if (coverable && missing.Count > 0)
			{
				best = missing;
				bestGain = gain;
			}
		}
		return best;
	}
}

public sealed class ChampionPoolGoal : ProgressionGoal
{
	public override string Describe() => "PoolForChampion";

	public override bool IsUrgent(Bot bot) => Gift(bot, out _, out _, out _);

	public override bool IsMet(Bot bot) => !Gift(bot, out _, out _, out _);

	public override PlanStep Plan(Bot bot) =>
		Gift(bot, out int itemType, out int keep, out var champion) ? new DeliverStep(itemType, champion.Id, keep, HandOver: true) : PlanStep.Idle;

	static bool Gift(Bot bot, out int itemType, out int keep, out Bot champion)
	{
		itemType = 0;
		keep = 0;
		champion = null;
		if (!Champions.InWindow(bot.Faction) || Champions.Strongest(bot.Faction) is not Bot strongest || strongest == bot)
			return false;
		foreach (var (type, need) in Champions.Shortage(strongest))
		{
			int share = Champions.ShareOf(bot, strongest, type, need);
			if (share <= 0)
				continue;
			itemType = type;
			keep = Inventory.Count(bot.Avatar, type) - share;
			champion = strongest;
			return true;
		}
		return false;
	}
}

public sealed class ChampionCraftGoal : ProgressionGoal
{
	public override string Describe() => "ChampionUpgrade";

	static bool Applies(Bot bot) => Champions.InWindow(bot.Faction) && Champions.Strongest(bot.Faction) == bot;

	public override bool IsUrgent(Bot bot) => Applies(bot) && CraftableUpgradeGoal.CraftedUpgrade(bot) != null;

	public override bool IsMet(Bot bot) => !IsUrgent(bot);

	public override PlanStep Plan(Bot bot) => Applies(bot) ? CraftableUpgradeGoal.CraftedUpgrade(bot) ?? PlanStep.Idle : PlanStep.Idle;
}

public sealed class HandDownGoal : ProgressionGoal
{
	public override string Describe() => "HandDown";

	public override bool IsUrgent(Bot bot) => Gift(bot, out _, out _);

	public override bool IsMet(Bot bot) => !Gift(bot, out _, out _);

	public override PlanStep Plan(Bot bot) =>
		Gift(bot, out var item, out var weakest) ? new DeliverStep(item.type, weakest.Id, Inventory.Count(bot.Avatar, item.type) - 1, HandOver: true) : PlanStep.Idle;

	static bool Gift(Bot bot, out Item item, out Bot weakest)
	{
		item = null;
		weakest = null;
		if (!Champions.InWindow(bot.Faction) || Champions.Strongest(bot.Faction) != bot || Champions.Weakest(bot.Faction) is not Bot candidate || candidate == bot || !Champions.Donors(bot).Contains(candidate))
			return false;
		item = Inventory.Items(bot.Avatar).FirstOrDefault(gear => gear.maxStack == 1 && !Champions.IsMaterial(gear) && !CraftableUpgradeGoal.Improves(bot, gear) && CraftableUpgradeGoal.Improves(candidate, gear));
		weakest = candidate;
		return item != null;
	}
}
