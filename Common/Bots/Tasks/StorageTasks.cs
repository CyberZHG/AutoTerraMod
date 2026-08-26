using System;
using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public abstract class ChestVisitTask : BotTask
{
	const int HandlingTicks = 20;
	const float RecallTripDistance = 16f * 80f;

	int handlingTimer;

	protected ChestVisitTask(Bot bot) : base(bot) { }

	protected abstract Chest PickChest();
	protected abstract void UseChest(Chest chest);

	public override void Start()
	{
		base.Start();
		handlingTimer = 0;
	}

	public override void Tick()
	{
		var chest = PickChest();
		if (chest == null)
		{
			IsFinished = true;
			return;
		}
		var tile = BaseStorage.TileOf(chest);
		if (!Bot.Hands.CanReach(null, tile))
		{
			if (Vector2.Distance(Bot.Center, tile.ToWorldCoordinates()) > RecallTripDistance && Bot.Faction.Settlement?.Contains(tile.X, tile.Y) == true && RecallReachesCamp() && RecallBringsCloser(tile.ToWorldCoordinates()) && TryRecallToCamp())
				return;
			if (!TravelOrTunnel(tile.ToWorldCoordinates(16f, 16f), 24f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(tile.ToWorldCoordinates());
		if (handlingTimer++ == 0)
			SoundEngine.PlaySound(SoundID.MenuOpen, tile.ToWorldCoordinates());
		if (handlingTimer < HandlingTicks)
			return;
		UseChest(chest);
		Bot.Watchdog.RecordProgress();
		SoundEngine.PlaySound(SoundID.MenuClose, tile.ToWorldCoordinates());
		IsFinished = true;
	}
}

public sealed class StoreTask : ChestVisitTask
{
	const int FullishSlots = 40;
	const int PurgeBelowFreeSlots = 20;
	const int OpportunisticSlots = 30;
	const int MinimumStoreBatch = 8;
	const int CrampedSlots = 3;
	const float NearBase = 16f * 12f;
	const float SurplusTreasurePriority = 45f;
	const float StoreAfterDeathPriority = 82f;

	public StoreTask(Bot bot) : base(bot) { }

	public override string NameKey => "Store";

	public override float Evaluate()
	{
		int used = Inventory.UsedSlots(Avatar);
		bool sharing = StatBoosters.CarriesBoostersToShare(Bot);
		if (used < OpportunisticSlots && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) >= NearBase && !sharing)
			return 0f;
		if (!BaseStorage.HasRoom(Bot.Faction) && (used < FullishSlots || !Junk.StorageHasClutter(Bot, BaseStorage.Chests(Bot.Faction))))
			return 0f;
		if (sharing)
		{
			Detail = Terraria.Localization.Language.GetTextValue("Mods.AutoTerraMod.UI.SharingBoosters");
			return 55f;
		}
		Detail = "";
		bool atBase = Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < NearBase;
		float score = atBase && Kit.CarriesSurplusTreasure(Bot) ? SurplusTreasurePriority : 0f;
		bool hasItemsToStore = Kit.ItemsToStore(Bot).Any();
		if (atBase && Bot.StoreAfterDeath && !Bot.UpgradeAfterDeath && hasItemsToStore)
			return StoreAfterDeathPriority;
		if (used < OpportunisticSlots || !hasItemsToStore || Bot.CommittedToMining && used < Inventory.MainSlotCount - 1)
			return score;
		if (used >= FullishSlots && (used >= Inventory.MainSlotCount - CrampedSlots || Kit.ItemsToStore(Bot).Count() >= MinimumStoreBatch))
			return Math.Max(score, 60f + (used - FullishSlots) * 3f);
		return Math.Max(score, atBase ? 40f : 0f);
	}

	protected override Chest PickChest() => BaseStorage.ChestWithRoom(Bot.Faction, Bot.Center)
		?? BaseStorage.Chests(Bot.Faction).OrderBy(chest => Vector2.DistanceSquared(Bot.Center, BaseStorage.TileOf(chest).ToWorldCoordinates())).FirstOrDefault();

	protected override void UseChest(Chest chest)
	{
		var reachable = BaseStorage.Chests(Bot.Faction)
			.Where(candidate => Bot.Hands.CanReach(null, BaseStorage.TileOf(candidate)))
			.ToList();
		if (!reachable.Contains(chest))
			reachable.Add(chest);
		if (BaseStorage.FreeSlots(Bot.Faction) < PurgeBelowFreeSlots)
			Junk.PurgeStorage(Bot, BaseStorage.Chests(Bot.Faction));
		int stored = 0;
		foreach (var item in Kit.ItemsToStore(Bot).ToList())
		{
			int room = item.maxStack > 1 ? Junk.Plenty - BaseStorage.Count(Bot.Faction, item.type) : int.MaxValue;
			if (room < item.stack)
			{
				int excess = item.stack - Math.Max(room, 0);
				BotDiagnostics.Log(Bot, $"trashed {item.Name} x{excess} (storage already holds {Junk.Plenty})");
				item.stack -= excess;
				if (item.stack <= 0)
				{
					item.TurnToAir();
					continue;
				}
			}
			var category = BaseStorage.CategoryOf(item);
			var ordered = reachable
				.OrderBy(candidate => BaseStorage.CategoryOfChest(Bot.Faction, candidate) == category ? 0 : 1)
				.ThenBy(candidate => BaseStorage.CategoryOfChest(Bot.Faction, candidate) == StorageCategory.Misc ? 0 : 1);
			foreach (var target in ordered)
			{
				int before = item.stack;
				var leftover = BaseStorage.Deposit(target, item.Clone());
				int moved = before - (leftover.IsAir ? 0 : leftover.stack);
				item.stack -= moved;
				stored += moved;
				if (item.stack <= 0)
				{
					item.TurnToAir();
					break;
				}
			}
		}
		BotDiagnostics.Log(Bot, $"stored {stored} items by category");
		Bot.FinishChoresAfterDeath();
	}
}

public sealed class WithdrawTask : ChestVisitTask
{
	WithdrawStep step;

	public WithdrawTask(Bot bot) : base(bot) { }

	public override string NameKey => "Withdraw";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not WithdrawStep withdraw)
			return 0f;
		step = withdraw;
		Detail = $"{Lang.GetItemNameValue(withdraw.ItemType)} x{withdraw.Amount}";
		return 65f;
	}

	protected override Chest PickChest() => step == null ? null : BaseStorage.ChestWith(Bot.Faction, step.ItemType, Bot.Center);

	protected override void UseChest(Chest chest)
	{
		int taken = BaseStorage.Withdraw(Avatar, chest, step.ItemType, step.Amount);
		if (taken > 0)
			Bot.WithdrawnAt[step.ItemType] = Main.GameUpdateCount;
		BotDiagnostics.Log(Bot, $"withdrew {Lang.GetItemNameValue(step.ItemType)} x{taken}");
	}
}
