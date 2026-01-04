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
		bool tasteAffordable = Planning.Stations.BaseHas(faction, TileID.Furnaces) && IsComplete(faction, faction.Settlement.Hall);
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
		if (structure.Purpose == StructurePurpose.Storage)
			return true;
		if (!WorldGen.RoomNeeds(NPCID.Guide))
			return false;
		return structure.Purpose != StructurePurpose.Bedroom || structure.FindTileInside(TileID.Beds) != null;
	}

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
		BotDiagnostics.Log(bot, $"skipping {operation.Action} at {operation.Tile} (structure {operation.Structure})");
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
		var construction = structure.PendingOperations(MaterialsFor(faction))
			.Where(operation => !excluded.Contains(operation.Tile) && !faction.Scaffolding.Contains(operation.Tile));
		var cleanup = ScaffoldToRemove(faction).Select(tile => new BuildOperation(BuildAction.RemoveScaffold, tile, structure.Index));
		return construction.Concat(cleanup).Cast<BuildOperation?>().FirstOrDefault();
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

	public static bool IsProtected(int x, int y)
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var settlement = faction.Settlement;
			if (settlement != null && settlement.Contains(x, y) && settlement.IsShell(x, y))
				return true;
		}
		return false;
	}

	public static bool IsDesignatedBuilder(Bot bot)
	{
		var builder = BotWorldSystem.Bots
			.Where(member => member.FactionId == bot.FactionId)
			.MaxBy(member => member.Personality.Building);
		return builder == bot || bot.Personality.Building >= 0.6f;
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
		BuildAction.PlaceBlock => Inventory.Items(avatar).FirstOrDefault(item => item.type == design.BlockItem),
		BuildAction.PlaceWall => Inventory.Items(avatar).FirstOrDefault(item => item.type == design.WallItem),
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
		BuildAction.PlaceDoor => ItemID.WoodenDoor,
		BuildAction.PlaceTorch => ItemID.Torch,
		BuildAction.PlaceTable => ItemID.WoodenTable,
		BuildAction.PlaceChair => ItemID.WoodenChair,
		BuildAction.PlaceBed => ItemID.Bed,
		_ => ItemID.None
	};

	public static int CountAvailable(Player avatar, BuildAction placement, HouseDesign design) => placement switch
	{
		BuildAction.PlaceBlock => Inventory.Count(avatar, design.BlockItem),
		BuildAction.PlaceWall => Inventory.Count(avatar, design.WallItem),
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

	static Item FindPlacing(Player avatar, int[] tileTypes) =>
		Inventory.Items(avatar).FirstOrDefault(item => item.createTile >= 0 && tileTypes.Contains(item.createTile) && !TileID.Sets.Platforms[item.createTile]);
}
