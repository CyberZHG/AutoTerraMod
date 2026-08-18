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

	(Point tile, uint at)? cover;
	const uint CoverTicks = 60;

	bool CoverSpikes()
	{
		if (cover is not var (tile, at))
			return false;
		if (Main.GameUpdateCount - at > CoverTicks || TileQuery.HasTile(tile) && !(Main.tile[tile.X, tile.Y].TileType is TileID.Spikes or TileID.WoodenSpikes))
		{
			cover = null;
			return false;
		}
		if (!TileQuery.HasTile(tile) && Bot.Hands.PlaceBridge(tile, solidDeck: true))
			cover = null;
		return true;
	}

	public override float Evaluate()
	{
		if (cover != null)
			return Priority;
		if (Inventory.IsNearlyFull(Avatar) || Bot.StoreAfterDeath && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < StoreAfterDeathReach)
			return 0f;
		if (target is Point known && WithinReach(known) && Worthwhile(known.X, known.Y, PickPower(), PickTier(PickPower())))
			return PriorityOf(known);
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
		return PriorityOf(found);
	}

	const float GravePriority = 35f;

	static float PriorityOf(Point tile) => Main.tile[tile.X, tile.Y].TileType == TileID.Tombstones ? GravePriority : Priority;

	Point? Scan()
	{
		int power = PickPower();
		if (power <= 0)
			return null;
		int tier = PickTier(power);
		var here = Bot.CenterTile;
		if (Bot.Npc is NPC body)
		{
			var feet = Navigation.PathFinder.FeetOf(body);
			for (int x = feet.X; x <= feet.X + 1; x++)
			{
				if (Main.tile[x, feet.Y + 1].HasTile && Main.tile[x, feet.Y + 1].TileType is TileID.Spikes or TileID.WoodenSpikes && Worthwhile(x, feet.Y + 1, power, tier))
					return new Point(x, feet.Y + 1);
			}
		}
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
		best ??= Systems.BotWorldSystem.KnowledgeOf(Bot.Faction).NearestKnown(here, Graves, unreachable, GraveReach);
		return best is Point target && Bosses.BossRituals.InPillarZone(target.ToWorldCoordinates()) ? null : best;
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
		if (!tile.HasTile)
			return false;
		int type = tile.TileType;
		if (!TileQuery.IsChoppableTree(type) && type != TileID.Pots && type != TileID.Tombstones && !Hazards.Contains(type) && !Salvage.Contains(type) && !OreTiers.ContainsKey(type) && !GemDrops.ContainsKey(type))
			return false;
		if (unreachable.Contains(new Point(x, y)) || HouseInspector.IsProtected(x, y) || Navigation.PathFinder.InsideMaze(new Point(x, y)))
			return false;
		if (TileQuery.IsChoppableTree(type))
			return wantsWood && TileQuery.IsTreeBase(x, y);
		if (type == TileID.Pots)
			return WorldGen.CanCutTile(x, y, Terraria.Enums.TileCuttingContext.AttackMelee);
		if (type == TileID.Tombstones)
			return WorldGen.CanKillTile(x, y);
		if (Hazards.Contains(type))
			return PickRequirement.MinimumPick(type, x, y) <= power && WorldGen.CanKillTile(x, y);
		if (Salvage.Contains(type))
			return WantsSalvage(type) && IsWildFurniture(x, y) && WorldGen.CanKillTile(x, y);
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

	const int EnoughOfOneKind = 999;
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

	const float HazardStandoff = 16f * 3f;

	static readonly HashSet<int> Hazards = new() { TileID.Spikes, TileID.WoodenSpikes, TileID.JungleThorns, TileID.PressurePlates, TileID.WeightedPressurePlate, TileID.Traps, TileID.Boulder, TileID.RollingCactus };

	static readonly HashSet<int> Salvage = new() { TileID.ClosedDoor, TileID.OpenDoor, TileID.Beds, TileID.Tables, TileID.Tables2, TileID.WorkBenches, TileID.Chairs };
	const int SalvageCarried = 2;
	const int SettlementMargin = 30;
	const int OutpostMargin = 15;
	const int TownHomeMargin = 30;

	bool WantsSalvage(int type)
	{
		int placed = type == TileID.OpenDoor ? TileID.ClosedDoor : type;
		int carried = 0;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (item.createTile == placed)
				carried += item.stack;
		}
		return carried < SalvageCarried && Inventory.HasRoomFor(Avatar, new Item(ItemID.WoodenDoor));
	}

	static bool IsWildFurniture(int x, int y)
	{
		if (y < Main.worldSurface + 5)
			return false;
		foreach (var faction in Systems.BotWorldSystem.Factions)
		{
			if (faction.Settlement is { } camp && x > camp.Left - SettlementMargin && x < camp.Right + SettlementMargin && y > camp.Top - SettlementMargin && y < camp.Floor + SettlementMargin)
				return false;
			if (faction.ArenaBed is Point arena && Near(arena, x, y, OutpostMargin))
				return false;
			foreach (var bed in faction.Outposts)
			{
				if (Near(bed, x, y, OutpostMargin))
					return false;
			}
		}
		foreach (var bot in Systems.BotWorldSystem.Bots)
		{
			if (bot.OutpostBed is Point bed && Near(bed, x, y, OutpostMargin))
				return false;
		}
		foreach (var npc in Main.npc)
		{
			if (npc.active && npc.townNPC && !npc.homeless && Near(new Point(npc.homeTileX, npc.homeTileY), x, y, TownHomeMargin))
				return false;
		}
		return true;
	}

	static bool Near(Point anchor, int x, int y, int margin) => System.Math.Abs(anchor.X - x) <= margin && System.Math.Abs(anchor.Y - y) <= margin;

	public override void Tick()
	{
		if (CoverSpikes())
			return;
		if (target is not Point tile || !Worthwhile(tile.X, tile.Y, PickPower(), PickTier(PickPower())))
		{
			target = Accomplished && Main.GameUpdateCount >= pausedUntil ? Scan() : null;
			if (target is not Point next || !WithinReach(next))
			{
				target = null;
				IsFinished = true;
				return;
			}
			tile = next;
		}
		int type = Main.tile[tile.X, tile.Y].TileType;
		bool pot = type == TileID.Pots;
		bool tree = TileQuery.IsChoppableTree(type);
		var tool = pot ? Bot.Hands.BestWeapon() ?? Bot.Hands.BestPickaxe : tree ? Bot.Hands.BestAxe : Bot.Hands.BestPickaxe;
		if (tool != null && Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.Stop();
			Bot.Motor.Face(tile.ToWorldCoordinates());
			bool spikes = type is TileID.Spikes or TileID.WoodenSpikes;
			var result = pot ? Bot.Hands.Cut(tile) : tree ? Bot.Hands.Chop(tile) : Bot.Hands.Dig(tile);
			if (spikes)
				cover = (tile, Main.GameUpdateCount);
			if (result is ToolResult.Unbreakable or ToolResult.NoTool)
				GiveUp(tile);
			else if (result is ToolResult.Done or ToolResult.Working)
				Accomplished = true;
			return;
		}
		Bot.Motor.MoveTo(tile.ToWorldCoordinates(), Hazards.Contains(type) ? HazardStandoff : 24f);
		if (Bot.Motor.IsStalled || Bot.Motor.HasArrived)
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
