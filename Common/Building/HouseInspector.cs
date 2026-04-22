using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public static class HouseInspector
{
	const int CacheTicks = 120;
	const int ForgetSkippedTicks = 3600;
	const int MinimumScaffoldPlatforms = 10;

	static readonly Dictionary<(int faction, int structure), (uint tick, bool complete)> completion = new();
	static readonly Dictionary<int, (uint since, HashSet<Point> tiles)> skipped = new();
	static readonly HashSet<(int faction, int structure)> reportedFailures = new();

	public static void Reset()
	{
		completion.Clear();
		operationCache.Clear();
		skipped.Clear();
		reportedFailures.Clear();
	}

	public static HouseDesign MaterialsFor(Faction faction)
	{
		bool tasteAffordable = Planning.Stations.BaseHas(faction, TileID.Furnaces) && faction.Settlement.Structures.All(structure => IsComplete(faction, structure));
		return tasteAffordable ? faction.HouseDesign : faction.HouseDesign.AsRustic();
	}

	public static bool IsComplete(Faction faction) => faction.Settlement != null && IsComplete(faction, faction.Settlement.Hall);

	public static bool IsComplete(Faction faction, HouseBlueprint structure)
	{
		var key = (faction.Id, structure.Index);
		if (completion.TryGetValue(key, out var cached) && Main.GameUpdateCount - cached.tick < CacheTicks)
			return cached.complete;
		bool complete;
		using (Perf.Measure(PerfSection.HouseQueries))
			complete = PassesRoomCheck(structure);
		completion[key] = (Main.GameUpdateCount, complete);
		return complete;
	}

	static bool PassesRoomCheck(HouseBlueprint structure)
	{
		var probe = structure.InsideProbe;
		if (!WorldGen.StartRoomCheck(probe.X, probe.Y))
			return false;
		if (structure.Purpose is StructurePurpose.Storage or StructurePurpose.Workshop or StructurePurpose.Gallery)
			return true;
		if (!WorldGen.RoomNeeds(NPCID.Guide))
			return false;
		return true;
	}

	public static bool HasSpareBed(Faction faction) =>
		HouseDesign.Beds.Any(bed => BaseStorage.Count(faction, bed) > 0)
		|| BotWorldSystem.Bots.Any(bot => bot.FactionId == faction.Id && IsDesignatedBuilder(bot) && Outpost.Count(bot.Avatar, Outpost.IsBed) > 0);

	public static bool MissesBed(HouseBlueprint structure) => structure.Purpose == StructurePurpose.Bedroom && structure.FindTileInside(TileID.Beds) == null;

	public static void Invalidate(Faction faction)
	{
		foreach (var key in completion.Keys.Where(key => key.faction == faction.Id).ToList())
			completion.Remove(key);
	}

	public static bool IsFinished(Faction faction) => faction.Settlement != null && IsFinished(faction, faction.Settlement.Hall);

	public static bool IsFinished(Faction faction, HouseBlueprint structure) =>
		IsComplete(faction, structure) && NextOperation(faction, structure) == null;

	public static ISet<Point> Skipped(Faction faction)
	{
		if (!skipped.TryGetValue(faction.Id, out var entry) || Main.GameUpdateCount - entry.since > ForgetSkippedTicks)
			skipped[faction.Id] = entry = (Main.GameUpdateCount, new HashSet<Point>());
		return entry.tiles;
	}

	public static void Skip(Bot bot, BuildOperation operation)
	{
		Skipped(bot.Faction).Add(operation.Tile);
		ForgetPlannedOperations(bot.Faction);
		BotDiagnostics.Log(bot, $"skipping {operation.Action} at {operation.Tile} (structure {operation.Structure}) from {bot.CenterTile}; motor: {bot.Motor.Describe()}{World.TerrainDump.Around(operation.Tile, 12, 7)}");
	}

	const int OperationCacheTicks = 20;

	static readonly Dictionary<(int faction, int structure), (uint tick, BuildOperation? operation)> operationCache = new();

	public static void ForgetPlannedOperations(Faction faction)
	{
		foreach (var key in operationCache.Keys.Where(key => key.faction == faction.Id).ToList())
			operationCache.Remove(key);
	}

	public static BuildOperation? NextOperation(Faction faction, HouseBlueprint structure)
	{
		var key = (faction.Id, structure.Index);
		if (operationCache.TryGetValue(key, out var cached) && Main.GameUpdateCount - cached.tick < OperationCacheTicks)
			return cached.operation;
		BuildOperation? operation;
		using (Perf.Measure(PerfSection.HouseQueries))
			operation = ComputeNextOperation(faction, structure);
		operationCache[key] = (Main.GameUpdateCount, operation);
		return operation;
	}

	static BuildOperation? ComputeNextOperation(Faction faction, HouseBlueprint structure)
	{
		var excluded = Skipped(faction);
		bool bedReady = HasSpareBed(faction);
		var pending = structure.PendingOperations(MaterialsFor(faction))
			.Where(operation => !faction.Scaffolding.Contains(operation.Tile) && (operation.Action != BuildAction.PlaceBed || bedReady))
			.ToList();
		foreach (var operation in pending)
		{
			if (!excluded.Contains(operation.Tile))
				return operation;
		}
		if (pending.Count > 0)
			return null;
		foreach (var tile in ScaffoldToRemove(faction))
			return new BuildOperation(BuildAction.RemoveScaffold, tile, structure.Index);
		return null;
	}

	static IEnumerable<Point> ScaffoldToRemove(Faction faction)
	{
		faction.Scaffolding.RemoveWhere(tile => !Main.tile[tile.X, tile.Y].HasTile);
		return faction.Scaffolding.OrderBy(tile => tile.Y);
	}

	public static void ReportUnfinishable(Bot bot, HouseBlueprint structure)
	{
		if (!reportedFailures.Add((bot.FactionId, structure.Index)))
			return;
		var probe = structure.InsideProbe;
		WorldGen.StartRoomCheck(probe.X, probe.Y);
		BotDiagnostics.Log(bot, $"{structure.Purpose} {structure.Index} finished its blueprint but room check failed: {WorldGen.roomCheckFailureReason}");
	}

	const int DungeonEntranceHalfWidth = 12;
	const int DungeonEntranceHeight = 14;
	const int DungeonEntranceDepth = 2;

	static bool InDungeonEntrance(int x, int y) =>
		Main.dungeonX > 0 && System.Math.Abs(x - Main.dungeonX) <= DungeonEntranceHalfWidth
		&& y >= Main.dungeonY - DungeonEntranceHeight && y <= Main.dungeonY + DungeonEntranceDepth;

	public static bool IsProtected(int x, int y)
	{
		if (InDungeonEntrance(x, y) || World.Highway.ProtectsAnyDeck(x, y))
			return true;
		foreach (var faction in BotWorldSystem.Factions)
		{
			var settlement = faction.Settlement;
			if (settlement != null && settlement.IsShell(x, y))
				return true;
		}
		foreach (var (bed, frame) in Outpost.Guarded())
		{
			if (frame is Point known ? Outpost.InFrame(known, x, y) : InOutpostShell(bed, x, y))
				return true;
		}
		return false;
	}

	static bool InOutpostShell(Point bed, int x, int y)
	{
		if (System.Math.Abs(x - bed.X) > Outpost.Width || y < bed.Y - Outpost.Height || y > bed.Y + 1 || !World.TileQuery.IsSolid(x, y))
			return false;
		return RoomWallAt(x - 1, y) || RoomWallAt(x + 1, y) || RoomWallAt(x, y - 1) || RoomWallAt(x, y + 1);
	}

	static bool RoomWallAt(int x, int y) =>
		WorldGen.InWorld(x, y, 2) && !World.TileQuery.IsSolid(x, y) && Main.tile[x, y].WallType > WallID.None && Main.wallHouse[Main.tile[x, y].WallType];

	const uint BuilderShiftTicks = 60 * 60 * 60;

	const uint BuilderCacheTicks = 60;
	static readonly Dictionary<int, (uint at, uint shift, int botId)> builders = new();

	public static bool IsDesignatedBuilder(Bot bot)
	{
		uint shift = Main.GameUpdateCount / BuilderShiftTicks;
		if (!builders.TryGetValue(bot.FactionId, out var cached) || Main.GameUpdateCount - cached.at >= BuilderCacheTicks)
		{
			bool keep = cached.shift == shift && BotWorldSystem.Bots.FirstOrDefault(member => member.Id == cached.botId) is { IsAlive: true, CommittedToMining: false } current
				&& current.FactionId == bot.FactionId;
			builders[bot.FactionId] = cached = (Main.GameUpdateCount, shift, keep ? cached.botId : PickBuilder(bot.FactionId));
		}
		return cached.botId == bot.Id;
	}

	const uint UnfinishedCacheTicks = 300;
	static readonly Dictionary<int, (uint at, bool unfinished)> unfinishedCache = new();

	public static bool BuildingDuty(Bot bot)
	{
		if (!IsDesignatedBuilder(bot) || bot.Faction.Settlement is not { } settlement)
			return false;
		if (!unfinishedCache.TryGetValue(bot.FactionId, out var cached) || Main.GameUpdateCount - cached.at >= UnfinishedCacheTicks)
			unfinishedCache[bot.FactionId] = cached = (Main.GameUpdateCount, settlement.Structures.Any(structure => !IsFinished(bot.Faction, structure)));
		return cached.unfinished;
	}

	static int PickBuilder(int factionId)
	{
		var members = BotWorldSystem.Bots.Where(member => member.FactionId == factionId && member.IsAlive).OrderBy(member => member.Id).ToList();
		if (members.Count == 0)
			return -1;
		int shift = (int)(Main.GameUpdateCount / BuilderShiftTicks % (uint)members.Count) + factionId;
		for (int offset = 0; offset < members.Count; offset++)
		{
			var candidate = members[(shift + offset) % members.Count];
			if (!candidate.CommittedToMining)
				return candidate.Id;
		}
		return -1;
	}

	public static bool HasScaffoldMaterial(Player avatar) =>
		Inventory.Items(avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack) >= MinimumScaffoldPlatforms;
}

public static class HouseMaterials
{
	static readonly int[] Beds = { TileID.Beds };

	public static bool IsNeeded(BuildAction action) => action is not (BuildAction.ChopTree or BuildAction.ClearTile or BuildAction.RemoveScaffold);

	public static BuildAction ReplacementFor(BuildAction action) => action switch
	{
		BuildAction.RenovateBlock => BuildAction.PlaceBlock,
		BuildAction.RenovateWall => BuildAction.PlaceWall,
		_ => action
	};

	public static Item Find(Player avatar, BuildAction action, HouseDesign design) => action switch
	{
		BuildAction.PlaceBlock => Preferring(avatar, design.BlockItems, ItemID.StoneBlock),
		BuildAction.PlaceWall => Preferring(avatar, design.WallItems, ItemID.StoneWall),
		BuildAction.PlacePlatform => Inventory.Items(avatar).FirstOrDefault(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]),
		BuildAction.PlaceDoor => FindPlacing(avatar, TileID.Sets.RoomNeeds.CountsAsDoor),
		BuildAction.PlaceTorch => FindPlacing(avatar, TileID.Sets.RoomNeeds.CountsAsTorch),
		BuildAction.PlaceTable => FindPlacing(avatar, TileID.Sets.RoomNeeds.CountsAsTable),
		BuildAction.PlaceChair => FindPlacing(avatar, TileID.Sets.RoomNeeds.CountsAsChair),
		BuildAction.PlaceBed => FindPlacing(avatar, Beds),
		_ => null
	};

	public static int DefaultItem(BuildAction action, HouseDesign design) => action switch
	{
		BuildAction.PlaceBlock => design.BlockItem,
		BuildAction.PlaceWall => design.WallItem,
		BuildAction.PlacePlatform => ItemID.WoodPlatform,
		BuildAction.PlaceDoor => ItemID.WoodenDoor,
		BuildAction.PlaceTorch => ItemID.Torch,
		BuildAction.PlaceTable => ItemID.WoodenTable,
		BuildAction.PlaceChair => ItemID.WoodenChair,
		BuildAction.PlaceBed => ItemID.Bed,
		_ => ItemID.None
	};

	public static int CountAvailable(Player avatar, BuildAction placement, HouseDesign design) => placement switch
	{
		BuildAction.PlaceBlock => Inventory.CountAny(avatar, design.BlockItems),
		BuildAction.PlaceWall => Inventory.CountAny(avatar, design.WallItems),
		BuildAction.PlacePlatform => Inventory.Items(avatar).Where(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]).Sum(item => item.stack),
		_ => Inventory.Items(avatar).Where(item => Matches(item, placement)).Sum(item => item.stack)
	};

	static bool Matches(Item item, BuildAction placement)
	{
		int[] tiles = placement switch
		{
			BuildAction.PlaceDoor => TileID.Sets.RoomNeeds.CountsAsDoor,
			BuildAction.PlaceTorch => TileID.Sets.RoomNeeds.CountsAsTorch,
			BuildAction.PlaceTable => TileID.Sets.RoomNeeds.CountsAsTable,
			BuildAction.PlaceChair => TileID.Sets.RoomNeeds.CountsAsChair,
			BuildAction.PlaceBed => Beds,
			_ => System.Array.Empty<int>()
		};
		return item.createTile >= 0 && tiles.Contains(item.createTile) && !TileID.Sets.Platforms[item.createTile];
	}

	static Item Preferring(Player avatar, int[] itemTypes, int preferred) =>
		itemTypes.Contains(preferred) && Inventory.Items(avatar).FirstOrDefault(item => item.type == preferred) is Item held ? held : MostOf(avatar, itemTypes);

	const int StoneForWalls = 4;

	static Item MostOf(Player avatar, int[] itemTypes)
	{
		Item best = null;
		foreach (int type in itemTypes)
		{
			int count = Inventory.Count(avatar, type);
			if (count > 0 && (best == null || count > Inventory.Count(avatar, best.type)))
				best = Inventory.Items(avatar).FirstOrDefault(item => item.type == type);
		}
		return best;
	}

	const int StoneForPlatforms = 10;

	public static int PlatformToObtain(Bots.Bot bot)
	{
		int stone = Inventory.Count(bot.Avatar, ItemID.StoneBlock) + BaseStorage.Count(bot.Faction, ItemID.StoneBlock)
			+ 2 * (Inventory.Count(bot.Avatar, ItemID.GrayBrick) + BaseStorage.Count(bot.Faction, ItemID.GrayBrick));
		return stone >= StoneForPlatforms && Planning.Stations.BaseHas(bot.Faction, TileID.Furnaces) ? ItemID.StonePlatform : ItemID.WoodPlatform;
	}

	public static int WallToObtain(Bots.Bot bot, HouseDesign design)
	{
		if (design.WallItems.Length == 1)
			return design.WallItem;
		if (Inventory.Count(bot.Avatar, ItemID.StoneBlock) + BaseStorage.Count(bot.Faction, ItemID.StoneBlock) >= StoneForWalls)
			return ItemID.StoneWall;
		int best = 0;
		int bestCount = -1;
		for (int index = 0; index < HouseDesign.RusticWalls.Length; index++)
		{
			int source = HouseDesign.RusticWallSources[index];
			int count = Inventory.Count(bot.Avatar, source) + BaseStorage.Count(bot.Faction, source);
			if (count > bestCount)
			{
				best = index;
				bestCount = count;
			}
		}
		return HouseDesign.RusticWalls[best];
	}

	public static int[] Options(BuildAction placement, HouseDesign design) => placement switch
	{
		BuildAction.PlaceBlock => design.BlockItems,
		BuildAction.PlaceWall => design.WallItems,
		BuildAction.PlaceDoor => HouseDesign.Doors,
		BuildAction.PlaceTable => HouseDesign.Tables,
		BuildAction.PlaceChair => HouseDesign.Chairs,
		BuildAction.PlaceBed => HouseDesign.Beds,
		_ => new[] { DefaultItem(placement, design) }
	};

	public static int BlockToObtain(Bots.Bot bot, HouseDesign design)
	{
		int best = design.BlockItem;
		int bestCount = -1;
		foreach (int type in design.BlockItems)
		{
			int count = Inventory.Count(bot.Avatar, type) + BaseStorage.Count(bot.Faction, type);
			if (count > bestCount)
			{
				best = type;
				bestCount = count;
			}
		}
		return best;
	}

	static Item FindPlacing(Player avatar, int[] tileTypes) =>
		Inventory.Items(avatar).FirstOrDefault(item => item.createTile >= 0 && tileTypes.Contains(item.createTile) && !TileID.Sets.Platforms[item.createTile]);
}
