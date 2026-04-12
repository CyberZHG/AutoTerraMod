using System.Collections.Generic;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class OpportunisticMineTask : BotTask
{
	const int ScanRadius = 8;
	const int GraveReach = 40;

	static readonly HashSet<int> Graves = new() { TileID.Tombstones };
	const int ScanIntervalTicks = 30;
	const float Priority = 85f;
	const float StoreAfterDeathReach = 16f * 40f;

	public static readonly Dictionary<int, int> OreTiers = new()
	{
		[TileID.Copper] = 1, [TileID.Tin] = 1,
		[TileID.Iron] = 2, [TileID.Lead] = 2,
		[TileID.Silver] = 3, [TileID.Tungsten] = 3,
		[TileID.Gold] = 4, [TileID.Platinum] = 4,
		[TileID.Demonite] = 5, [TileID.Crimtane] = 5, [TileID.Meteorite] = 5, [TileID.Obsidian] = 5,
		[TileID.Hellstone] = 6,
		[TileID.Cobalt] = 7, [TileID.Palladium] = 7,
		[TileID.Mythril] = 8, [TileID.Orichalcum] = 8,
		[TileID.Adamantite] = 9, [TileID.Titanium] = 9,
		[TileID.Chlorophyte] = 10
	};

	public static readonly Dictionary<int, int> GemDrops = new()
	{
		[TileID.Amethyst] = ItemID.Amethyst, [TileID.Topaz] = ItemID.Topaz, [TileID.Sapphire] = ItemID.Sapphire,
		[TileID.Emerald] = ItemID.Emerald, [TileID.Ruby] = ItemID.Ruby, [TileID.Diamond] = ItemID.Diamond
	};

	static readonly (int power, int tier)[] PickTiers = { (35, 1), (40, 2), (45, 3), (55, 4), (65, 5), (100, 6), (110, 7), (150, 8), (180, 9), (200, 10) };

	readonly HashSet<Point> unreachable = new();
	Point? target;
	uint scannedAt;
	uint unreachableSince;
	const uint ForgetUnreachableTicks = 18000;

	public OpportunisticMineTask(Bot bot) : base(bot) { }

	public override string NameKey => "Mine";

	public override float Evaluate()
	{
		if (Inventory.IsNearlyFull(Avatar) || Bot.StoreAfterDeath && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < StoreAfterDeathReach)
			return 0f;
		if (target is Point known && WithinReach(known) && Worthwhile(known.X, known.Y, PickPower(), PickTier(PickPower())))
			return Priority;
		target = null;
		if (Main.GameUpdateCount - scannedAt < ScanIntervalTicks || Main.GameUpdateCount < pausedUntil)
			return 0f;
		if (Main.GameUpdateCount - unreachableSince > ForgetUnreachableTicks)
		{
			unreachable.Clear();
			unreachableSince = Main.GameUpdateCount;
		}
		scannedAt = Main.GameUpdateCount;
		wantsMore.Clear();
		wantsWood = Bot.Hands.HasAxe && WoodCarried() < EnoughWood;
		target = Scan();
		if (target is not Point found)
			return 0f;
		Detail = TileID.Search.GetName(Main.tile[found.X, found.Y].TileType);
		return Priority;
	}

	Point? Scan()
	{
		int power = PickPower();
		if (power <= 0)
			return null;
		int tier = PickTier(power);
		var here = Bot.CenterTile;
		Point? best = null;
		int bestDistance = int.MaxValue;
		for (int x = here.X - ScanRadius; x <= here.X + ScanRadius; x++)
		{
			for (int y = here.Y - ScanRadius; y <= here.Y + ScanRadius; y++)
			{
				if (!WorldGen.InWorld(x, y, 5) || !Worthwhile(x, y, power, tier))
					continue;
				int distance = (x - here.X) * (x - here.X) + (y - here.Y) * (y - here.Y);
				if (distance < bestDistance)
				{
					best = new Point(x, y);
					bestDistance = distance;
				}
			}
		}
		return best ?? Systems.BotWorldSystem.KnowledgeOf(Bot.Faction).NearestKnown(here, Graves, unreachable, GraveReach);
	}

	bool WithinReach(Point tile)
	{
		int reach = Main.tile[tile.X, tile.Y].TileType == TileID.Tombstones ? GraveReach : ScanRadius * 2;
		return System.Math.Abs(tile.X - Bot.CenterTile.X) <= reach && System.Math.Abs(tile.Y - Bot.CenterTile.Y) <= reach;
	}

	int PickPower() => Bot.Hands.BestPickaxe?.pick ?? 0;

	bool Worthwhile(int x, int y, int power, int pickTier)
	{
		var tile = Main.tile[x, y];
		if (!tile.HasTile || unreachable.Contains(new Point(x, y)) || HouseInspector.IsProtected(x, y))
			return false;
		int type = tile.TileType;
		if (TileQuery.IsChoppableTree(type))
			return wantsWood && TileQuery.IsTreeBase(x, y);
		if (type == TileID.Pots)
			return WorldGen.CanCutTile(x, y, Terraria.Enums.TileCuttingContext.AttackMelee);
		if (type == TileID.Tombstones)
			return WorldGen.CanKillTile(x, y);
		if (!OreTiers.ContainsKey(type) && !GemDrops.ContainsKey(type))
			return false;
		int drop = GemDrops.TryGetValue(type, out int gem) ? gem : Planning.Stations.ItemsPlacing(type).Count > 0 ? Planning.Stations.ItemsPlacing(type)[0] : 0;
		if (!Inventory.HasRoomForType(Avatar, drop))
			return false;
		return WantsMore(type) && PickRequirement.MinimumPick(type, x, y) <= power && WorldGen.CanKillTile(x, y);
	}

	public static int PickTier(int power)
	{
		int tier = 0;
		foreach (var (threshold, level) in PickTiers)
		{
			if (power >= threshold)
				tier = level;
		}
		return tier;
	}

	const int EnoughOfOneKind = 100;
	const int EnoughWood = 200;

	bool wantsWood;

	int WoodCarried()
	{
		int total = 0;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (ResourceSources.IsWood(item.type))
				total += item.stack;
		}
		return total;
	}

	readonly Dictionary<int, bool> wantsMore = new();

	bool WantsMore(int tileType)
	{
		if (wantsMore.TryGetValue(tileType, out bool cached))
			return cached;
		int drop = GemDrops.TryGetValue(tileType, out int gem) ? gem : Terraria.ModLoader.TileLoader.GetItemDropFromTypeAndStyle(tileType);
		bool wanted = drop > 0 && Inventory.Count(Avatar, drop) < EnoughOfOneKind && Inventory.HasRoomFor(Avatar, new Item(drop));
		wantsMore[tileType] = wanted;
		return wanted;
	}

	public override void Tick()
	{
		if (target is not Point tile || !Worthwhile(tile.X, tile.Y, PickPower(), PickTier(PickPower())))
		{
			target = null;
			IsFinished = true;
			return;
		}
		int type = Main.tile[tile.X, tile.Y].TileType;
		bool pot = type == TileID.Pots;
		bool tree = TileQuery.IsChoppableTree(type);
		var tool = pot ? Bot.Hands.BestWeapon() ?? Bot.Hands.BestPickaxe : tree ? Bot.Hands.BestAxe : Bot.Hands.BestPickaxe;
		if (tool != null && Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.Stop();
			Bot.Motor.Face(tile.ToWorldCoordinates());
			var result = pot ? Bot.Hands.Cut(tile) : tree ? Bot.Hands.Chop(tile) : Bot.Hands.Dig(tile);
			if (result is ToolResult.Unbreakable or ToolResult.NoTool)
				GiveUp(tile);
			else if (result is ToolResult.Done or ToolResult.Working)
				Accomplished = true;
			return;
		}
		Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
		if (Bot.Motor.IsStalled)
			GiveUp(tile);
	}

	const int GiveUpsBeforePause = 3;
	const uint GiveUpWindowTicks = 600;
	const uint PauseTicks = 1800;

	uint pausedUntil;
	uint firstGiveUpAt;
	int recentGiveUps;

	void GiveUp(Point tile)
	{
		if (Main.GameUpdateCount - firstGiveUpAt > GiveUpWindowTicks)
		{
			firstGiveUpAt = Main.GameUpdateCount;
			recentGiveUps = 0;
		}
		if (++recentGiveUps >= GiveUpsBeforePause)
			pausedUntil = Main.GameUpdateCount + PauseTicks;
		unreachable.Add(tile);
		target = null;
		Bot.Motor.Stop();
		IsFinished = true;
	}
}
