using System.Linq;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class LootTask : BotTask
{
	const int OpenDelayTicks = 30;
	const float MaxTravel = 16f * 200f;

	const float WantedScore = 90f;

	int chestIndex = -1;
	int openTimer;
	bool emptied;
	bool wanted;
	readonly System.Collections.Generic.Dictionary<int, uint> wantedRetryAt = new();

	const uint WantedRetryTicks = 10 * 3600;

	public LootTask(Bot bot) : base(bot) { }

	public override string NameKey => "Loot";

	public override float Evaluate()
	{
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		NoticeVisibleChests(knowledge);
		chestIndex = WantedChest(knowledge);
		wanted = chestIndex >= 0;
		if (!wanted)
			chestIndex = NearestLootable(knowledge);
		if (chestIndex < 0 || Inventory.UsedSlots(Avatar) >= 45)
			return 0f;
		var chestTile = Main.tile[Main.chest[chestIndex].x, Main.chest[chestIndex].y];
		Detail = Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(chestTile.TileType, chestTile.TileFrameX / 36)) ?? "";
		return wanted ? WantedScore : 30f + Traits.Exploring * 40f;
	}

	public override void Start()
	{
		base.Start();
		openTimer = 0;
		emptied = false;
	}

	public override void Tick()
	{
		var chest = chestIndex >= 0 ? Main.chest[chestIndex] : null;
		if (chest == null)
		{
			IsFinished = true;
			return;
		}
		var chestTile = new Point(chest.x, chest.y);
		var reachTool = emptied && ShouldCarryHome(chest) ? Bot.Hands.BestPickaxe : null;
		if (!Bot.Hands.CanReach(reachTool, chestTile))
		{
			if (wanted)
			{
				if (!TravelOrTunnel(chestTile.ToWorldCoordinates(16f, 16f), 24f))
				{
					BotDiagnostics.Log(Bot, $"cannot reach chest {chestIndex} at {chestTile} yet, trying again later; motor: {Bot.Motor.Describe()}");
					wantedRetryAt[chestIndex] = Main.GameUpdateCount + WantedRetryTicks;
					IsFinished = true;
				}
				return;
			}
			Bot.Motor.MoveTo(chestTile.ToWorldCoordinates(16f, 16f), 24f);
			if (Bot.Motor.IsStuck)
				Abandon("stuck on the way");
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(chestTile.ToWorldCoordinates());
		if (openTimer++ == 0)
			SoundEngine.PlaySound(SoundID.MenuOpen, chestTile.ToWorldCoordinates());
		if (openTimer < OpenDelayTicks)
			return;
		if (!emptied)
		{
			TakeEverything(chest);
			Bot.Watchdog.RecordProgress();
			SoundEngine.PlaySound(SoundID.MenuClose, chestTile.ToWorldCoordinates());
			emptied = true;
		}
		if (!ShouldCarryHome(chest))
		{
			IsFinished = true;
			return;
		}
		var result = Bot.Hands.Dig(chestTile);
		if (result is ToolResult.Unbreakable or ToolResult.NoTool)
			BotWorldSystem.KnowledgeOf(Bot.Faction).AbandonChest(chestIndex);
		if (!TileQuery.HasTile(chestTile))
			Bot.Hands.PickUpDropNear(chestTile, item => item.createTile >= 0 && TileID.Sets.BasicChest[item.createTile]);
		if (result is not ToolResult.Working)
			IsFinished = true;
	}

	bool ShouldCarryHome(Chest chest) =>
		chest.item.All(item => item == null || item.IsAir)
		&& Inventory.UsedSlots(Avatar) < Inventory.MainSlotCount
		&& Bot.Hands.BestPickaxe != null;

	void TakeEverything(Chest chest)
	{
		int taken = 0;
		for (int slot = 0; slot < chest.item.Length; slot++)
		{
			var item = chest.item[slot];
			if (item == null || item.IsAir)
				continue;
			var leftover = Inventory.Add(Avatar, item.Clone());
			taken += item.stack - (leftover.IsAir ? 0 : leftover.stack);
			chest.item[slot] = leftover.IsAir ? new Item() : leftover;
		}
		if (chest.item.All(item => item == null || item.IsAir))
			BotWorldSystem.KnowledgeOf(Bot.Faction).MarkLooted(chestIndex);
		BotDiagnostics.Log(Bot, $"looted chest at ({chest.x},{chest.y}), took {taken} items");
	}

	void Abandon(string reason)
	{
		BotDiagnostics.Log(Bot, $"gave up chest {chestIndex}: {reason}");
		BotWorldSystem.KnowledgeOf(Bot.Faction).AbandonChest(chestIndex);
		IsFinished = true;
	}

	void NoticeVisibleChests(FactionKnowledge knowledge)
	{
		var origin = Bot.CenterTile;
		for (int index = 0; index < Main.maxChests; index++)
		{
			var chest = Main.chest[index];
			if (chest == null)
				continue;
			if (System.Math.Abs(chest.x - origin.X) > WorldScan.ViewHalfWidth || System.Math.Abs(chest.y - origin.Y) > WorldScan.ViewHalfHeight)
				continue;
			if (IsExposed(chest))
				knowledge.RememberChest(index);
		}
	}

	static bool IsExposed(Chest chest)
	{
		for (int x = chest.x - 1; x <= chest.x + 2; x++)
		{
			for (int y = chest.y - 1; y <= chest.y + 2; y++)
			{
				bool outsideChest = x < chest.x || x > chest.x + 1 || y < chest.y || y > chest.y + 1;
				if (outsideChest && !TileQuery.IsSolid(x, y))
					return true;
			}
		}
		return false;
	}

	int WantedChest(FactionKnowledge knowledge)
	{
		if (Bot.Brain.Plan is not Planning.GatherStep { ItemType: int itemType })
			return -1;
		bool knownTemple = knowledge.LandmarkOf(World.Landmark.Temple) != null;
		return Enumerable.Range(0, Main.maxChests)
			.Where(index => Main.chest[index] is Chest chest && chest.item.Any(item => item?.type == itemType && item.stack > 0)
				&& IsLootable(chest) && knowledge.MayLoot(index) && IsExposed(chest)
				&& (!wantedRetryAt.TryGetValue(index, out uint retryAt) || Main.GameUpdateCount >= retryAt)
				&& (knowledge.KnowsChest(index) || knowledge.IsExplored(chest.x / FactionKnowledge.ExploreCell, chest.y / FactionKnowledge.ExploreCell)
					|| knownTemple && Navigation.PathFinder.InsideMaze(new Point(chest.x, chest.y))))
			.OrderBy(index => WalkCost(Main.chest[index]))
			.DefaultIfEmpty(-1)
			.First();
	}

	const int ClimbCostFactor = 2;

	int WalkCost(Chest chest) => System.Math.Abs(chest.x - Bot.CenterTile.X) + ClimbCostFactor * System.Math.Abs(chest.y - Bot.CenterTile.Y);

	int NearestLootable(FactionKnowledge knowledge) => knowledge.CollectableChests
		.Where(index => IsLootable(Main.chest[index]))
		.Select(index => (index, distance: Vector2.Distance(Bot.Center, new Point(Main.chest[index].x, Main.chest[index].y).ToWorldCoordinates())))
		.Where(candidate => candidate.distance < MaxTravel)
		.OrderBy(candidate => candidate.distance)
		.Select(candidate => candidate.index)
		.DefaultIfEmpty(-1)
		.First();

	bool IsLootable(Chest chest) =>
		!Chest.IsLocked(chest.x, chest.y)
		&& !IsAtAnyBase(chest)
		&& Main.LocalPlayer.chest != System.Array.IndexOf(Main.chest, chest);

	static bool IsAtAnyBase(Chest chest) =>
		BotWorldSystem.Factions.Any(faction => faction.Settlement?.Contains(chest.x, chest.y) == true);
}
