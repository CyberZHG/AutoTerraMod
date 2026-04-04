using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class CraftTask : BotTask
{
	const int CraftDelayTicks = 20;
	const float BaseArrivalRadius = 16f * 2f;
	const float StationArrivalRadius = 16f * 3f;
	const float StationStandRadius = 12f;
	const float RecallTripDistance = 16f * 120f;
	const int RecallAfterWorkTicks = 600;

	int craftTimer;

	public CraftTask(Bot bot) : base(bot) { }

	public override string NameKey => Bot.Brain.Plan is PlaceStationStep ? "PlaceStation" : "Craft";

	public override float Evaluate()
	{
		switch (Bot.Brain.Plan)
		{
			case CraftStep craft:
				Detail = craft.Recipe.createItem.Name;
				break;
			case PlaceStationStep place:
				Detail = Lang.GetItemNameValue(place.ItemType);
				break;
			default:
				return 0f;
		}
		return 55f + Traits.Building * 25f;
	}

	public override void Start()
	{
		base.Start();
		craftTimer = 0;
	}

	public override void Tick()
	{
		switch (Bot.Brain.Plan)
		{
			case CraftStep craft:
				TickCraft(craft.Recipe);
				break;
			case PlaceStationStep place:
				TickPlace(place.ItemType);
				break;
			default:
				IsFinished = true;
				break;
		}
	}

	bool AtBase => Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < BaseArrivalRadius + 8f;

	void TickCraft(Recipe recipe)
	{
		Avatar.AdjTiles();
		int missingStation = recipe.requiredTile.FirstOrDefault(tile => !Avatar.adjTile[tile], -1);
		if (missingStation >= 0)
		{
			bool stationUnreachable = Main.GameUpdateCount - stationStuckAt < StationStuckMemoryTicks;
			if ((stationUnreachable || FarFromStation(missingStation)) && SetUpPortableStation(missingStation, PortableDepth))
				return;
			WalkToStation(missingStation);
			return;
		}
		if (ProgressionPlanner.NeedsWater(recipe) && !WaterFinder.IsNearWater(Bot.CenterTile))
		{
			WalkToWater();
			return;
		}
		Bot.Motor.Stop();
		Equipment.UnequipIngredients(Avatar, recipe);
		if (!Crafting.HasIngredients(Avatar, recipe))
		{
			BotDiagnostics.Log(Bot, $"missing ingredients for {recipe.createItem.Name}");
			IsFinished = true;
			return;
		}
		if (++craftTimer < CraftDelayTicks)
			return;
		if (Crafting.TryCraft(Avatar, recipe))
		{
			Bot.Watchdog.RecordProgress();
			Accomplished = true;
		}
		BotDiagnostics.Log(Bot, $"crafted {recipe.createItem.Name}");
		PackUpPortableStations();
		IsFinished = true;
	}

	const float PortableStationDistance = 16f * 40f;
	const int PortableDepth = 3;

	static readonly HashSet<int> PortableStationTiles = new()
	{
		TileID.WorkBenches, TileID.Furnaces, TileID.Anvils, TileID.Hellforge, TileID.MythrilAnvil, TileID.AdamantiteForge, TileID.Sawmill, TileID.Loom
	};

	public static bool IsPortableStation(Item item) => item.createTile >= 0 && PortableStationTiles.Contains(item.createTile);

	public static bool IsPortableStationTile(int tileType) => PortableStationTiles.Contains(tileType);

	readonly List<(Point tile, int itemType)> placedStations = new();

	bool FarFromStation(int stationTile)
	{
		if (!PortableStationTiles.Contains(stationTile))
			return false;
		var station = Stations.LocateAnywhere(Bot.Faction, stationTile, Bot.CenterTile);
		return station is not Point found || Vector2.Distance(Bot.Center, found.ToWorldCoordinates()) > PortableStationDistance;
	}

	bool SetUpPortableStation(int stationTile, int depth)
	{
		var carried = Inventory.Items(Avatar).FirstOrDefault(item => item.createTile == stationTile);
		if (carried != null)
			return PlaceNearby(carried, stationTile);
		if (depth <= 0)
			return false;
		foreach (int itemType in Stations.ItemsPlacing(stationTile))
		{
			foreach (var recipe in RecipeIndex.For(itemType))
			{
				if (!Crafting.HasIngredients(Avatar, recipe))
					continue;
				int missing = recipe.requiredTile.FirstOrDefault(tile => !Avatar.adjTile[tile], -1);
				if (missing < 0)
				{
					Crafting.TryCraft(Avatar, recipe);
					BotDiagnostics.Log(Bot, $"crafted a portable {recipe.createItem.Name}");
					return true;
				}
				if (PortableStationTiles.Contains(missing) && SetUpPortableStation(missing, depth - 1))
					return true;
			}
		}
		return false;
	}

	bool PlaceNearby(Item station, int stationTile)
	{
		var feet = Navigation.PathFinder.FeetOf(Bot.Npc);
		string name = station.Name;
		int itemType = station.type;
		foreach (int dx in new[] { 2, -3, 3, -4, 1, -2 })
		{
			for (int dy = 0; dy >= -1; dy--)
			{
				var spot = new Point(feet.X + dx, feet.Y + dy);
				if (!Bot.Hands.CanReach(station, spot) || !Bot.Hands.Place(station, spot))
					continue;
				if (FindPlaced(spot, stationTile) is Point placed)
					placedStations.Add((placed, itemType));
				Avatar.AdjTiles();
				BotDiagnostics.Log(Bot, $"set up a portable {name} at {spot}");
				return true;
			}
		}
		return false;
	}

	static Point? FindPlaced(Point near, int tileType)
	{
		for (int x = near.X - 2; x <= near.X + 2; x++)
		{
			for (int y = near.Y - 2; y <= near.Y + 1; y++)
			{
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
					return new Point(x, y);
			}
		}
		return null;
	}

	void PackUpPortableStations()
	{
		foreach (var (tile, itemType) in placedStations)
		{
			if (!Main.tile[tile.X, tile.Y].HasTile || !PortableStationTiles.Contains(Main.tile[tile.X, tile.Y].TileType))
				continue;
			WorldGen.KillTile(tile.X, tile.Y);
			Bot.Hands.PickUpDropNear(tile, item => item.type == itemType);
		}
		placedStations.Clear();
	}

	public override void Stop()
	{
		PackUpPortableStations();
		base.Stop();
	}

	void WalkToWater()
	{
		if (WaterFinder.NearestShore(Bot.Faction.BaseTile) is not Point shore)
		{
			BotDiagnostics.Log(Bot, "no water found near base");
			IsFinished = true;
			return;
		}
		Bot.Motor.MoveTo(shore.ToWorldCoordinates(), StationArrivalRadius);
		if (Bot.Motor.IsStuck)
		{
			BotDiagnostics.Log(Bot, $"stuck walking to water at {shore}");
			IsFinished = true;
		}
	}

	const uint StationStuckMemoryTicks = 1800;
	uint stationStuckAt;

	void WalkToStation(int stationTile)
	{
		var location = Stations.LocateAnywhere(Bot.Faction, stationTile, Bot.CenterTile);
		if (location is not Point station)
		{
			BotDiagnostics.Log(Bot, $"station tile {stationTile} not found at base");
			Stations.Invalidate(Bot.Faction);
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, station.ToWorldCoordinates()) > RecallTripDistance && Bot.Hands.TicksSinceWork > RecallAfterWorkTicks && Bot.Faction.Settlement?.Contains(station.X, station.Y) != false && RecallLandsAtBase() && TryRecallHome())
			return;
		var standAt = World.Grounds.StandingSpotNear(station) is Point feet ? Navigation.PathFinder.BodyCenterAt(feet) : station.ToWorldCoordinates();
		Bot.Motor.MoveTo(standAt, StationStandRadius);
		if (Bot.Motor.IsStuck)
		{
			BotDiagnostics.Log(Bot, $"stuck walking to station tile {stationTile} at {station}; motor: {Bot.Motor.Describe()}{World.TerrainDump.Around(Bot.CenterTile, 30, 12)}");
			stationStuckAt = Main.GameUpdateCount;
			if (Bot.Faction.Settlement is { } camp && camp.Contains(station.X, station.Y) && Bot.CenterTile.Y > camp.Floor)
				Bot.Watchdog.RequestEscape();
			IsFinished = true;
		}
	}

	const int FootprintWidth = 3;

	bool FlattenFloors(Item item)
	{
		bool changed = false;
		foreach (var spot in Stations.PlacementSpots(Bot.Faction, item))
		{
			for (int dx = 0; dx < FootprintWidth; dx++)
			{
				var floor = Main.tile[spot.X + dx, spot.Y + 1];
				if (!floor.HasTile || !floor.IsHalfBlock && floor.Slope == SlopeType.Solid || !Main.tileSolid[floor.TileType] || Main.tileSolidTop[floor.TileType])
					continue;
				floor.IsHalfBlock = false;
				floor.Slope = SlopeType.Solid;
				WorldGen.SquareTileFrame(spot.X + dx, spot.Y + 1);
				changed = true;
			}
		}
		return changed;
	}

	Point? GraveOnSpot(Item item)
	{
		if (Bot.Hands.BestPickaxe == null)
			return null;
		foreach (var spot in Stations.PlacementSpots(Bot.Faction, item))
		{
			for (int dx = 0; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 0; dy++)
				{
					var tile = Main.tile[spot.X + dx, spot.Y + dy];
					if (tile.HasTile && tile.TileType is TileID.Tombstones or TileID.Banners)
						return new Point(spot.X + dx, spot.Y + dy);
				}
			}
		}
		return null;
	}

	static bool OnGround(Point spot) =>
		World.TileQuery.IsSolid(spot.X, spot.Y + 1) && !Main.tileFrameImportant[Main.tile[spot.X, spot.Y + 1].TileType]
		|| Main.tile[spot.X, spot.Y + 1].HasTile && Main.tileTable[Main.tile[spot.X, spot.Y + 1].TileType];

	void TickPlace(int itemType)
	{
		var item = Inventory.Items(Avatar).FirstOrDefault(candidate => candidate.type == itemType);
		if (item == null)
		{
			IsFinished = true;
			return;
		}
		var spot = Stations.PlacementSpots(Bot.Faction, item).Cast<Point?>().FirstOrDefault(candidate => OnGround(candidate.Value) && Bot.Hands.CanPlaceAt(item, candidate.Value));
		if (spot == null && FlattenFloors(item))
			spot = Stations.PlacementSpots(Bot.Faction, item).Cast<Point?>().FirstOrDefault(candidate => OnGround(candidate.Value) && Bot.Hands.CanPlaceAt(item, candidate.Value));
		if (spot == null && GraveOnSpot(item) is Point grave)
		{
			if (!Bot.Hands.CanReach(Bot.Hands.BestPickaxe, grave))
			{
				Bot.Motor.MoveTo(grave.ToWorldCoordinates(), BaseArrivalRadius);
				if (Bot.Motor.IsStuck)
					IsFinished = true;
				return;
			}
			Bot.Motor.Stop();
			if (Bot.Hands.Dig(grave) is ToolResult.Unbreakable or ToolResult.NoTool)
				IsFinished = true;
			return;
		}
		if (spot is not Point target)
		{
			BotDiagnostics.Log(Bot, $"no valid spot to place {item.Name} near base {Bot.Faction.BaseTile}");
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(item, target))
		{
			Bot.Motor.MoveTo(target.ToWorldCoordinates(), BaseArrivalRadius);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		string name = item.Name;
		if (!Bot.Hands.Ready || !Bot.Hands.Place(item, target))
			return;
		Stations.Invalidate(Bot.Faction);
		BaseStorage.Reset();
		BotDiagnostics.Log(Bot, $"placed {name} at {target}");
		Accomplished = true;
		IsFinished = true;
	}
}

public sealed class GatherTask : BotTask
{
	const int KnowledgeSearchRadius = 400;
	const int SearchCooldownTicks = 30;
	const int ForgetUnreachableTicks = 3600;
	const int ProspectStepTiles = 20;
	const int ProspectDepthTiles = 40;
	const int TreeProspectTiles = 40;

	readonly HashSet<Point> unreachable = new();
	GatherStep step;
	Point? target;
	int prospectDirection = 1;
	Vector2? prospectTarget;
	readonly HashSet<Point> visitedCaves = new();
	int searchCooldown;
	uint unreachableClearedAt;

	public GatherTask(Bot bot) : base(bot) { }

	TileSource Tiles => (TileSource)step.Source;

	public override string NameKey => step?.Source is TileSource { IsTree: true } ? "Chop" : target == null ? "Prospect" : "Mine";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not GatherStep { Source: TileSource tiles } gather || !Inventory.HasRoomForType(Avatar, gather.ItemType))
			return 0f;
		step = gather;
		Detail = $"{Lang.GetItemNameValue(gather.ItemType)} x{gather.Amount}";
		float affinity = tiles.IsTree ? Traits.Building : Traits.Mining;
		return 40f + affinity * 40f;
	}

	public override void Start()
	{
		base.Start();
		target = null;
		prospectTarget = null;
		searchCooldown = 0;
		prospectDirection = Main.rand.NextBool() ? 1 : -1;
		if (!Tiles.IsTree && Main.rand.NextFloat() < TeleportChance && Shops.WithinShoppingTrip(Bot, Bot.Faction.BaseCenter))
			TryRandomTeleport();
	}

	const float TeleportChance = 0.3f;

	public override void Tick()
	{
		if (Main.GameUpdateCount - unreachableClearedAt > ForgetUnreachableTicks)
		{
			unreachable.Clear();
			unreachableClearedAt = Main.GameUpdateCount;
		}
		if (!TargetStillValid() && --searchCooldown <= 0)
		{
			searchCooldown = SearchCooldownTicks;
			target = FindTarget();
		}
		if (target is not Point tile)
		{
			Prospect();
			return;
		}
		bool chopping = Tiles.IsTree && !TileQuery.IsExposedLivingWood(tile.X, tile.Y);
		bool cutting = !Tiles.IsTree && Main.tileCut[Math.Max(0, TileQuery.TypeAt(tile))];
		var tool = chopping ? Bot.Hands.BestAxe : cutting ? Bot.Hands.BestWeapon() ?? Bot.Hands.BestPickaxe : Bot.Hands.BestPickaxe;
		if (tool != null && Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.Stop();
			Bot.Motor.Face(tile.ToWorldCoordinates());
			int tileType = TileQuery.TypeAt(tile);
			if (tileType < 0)
			{
				target = null;
				return;
			}
			var result = chopping ? Bot.Hands.Chop(tile)
				: Main.tileCut[tileType] ? Bot.Hands.Cut(tile)
				: Bot.Hands.Dig(tile);
			if (result is ToolResult.Unbreakable or ToolResult.NoTool)
				Abandon(tile);
			return;
		}
		Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
		if (Bot.Motor.IsStuck)
			Abandon(tile);
	}

	bool TargetStillValid()
	{
		if (target is not Point tile || !Tiles.TileTypes.Contains(TileQuery.TypeAt(tile)) || !Tiles.Accepts(tile.X, tile.Y))
			return false;
		return !Tiles.IsTree || TileQuery.IsWoodSource(tile.X, tile.Y);
	}

	void Abandon(Point tile)
	{
		unreachable.Add(tile);
		target = null;
		Bot.Motor.Stop();
	}

	Point? FindTarget()
	{
		var origin = Bot.CenterTile;
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		Func<int, int, bool> accept = Tiles.IsTree ? TileQuery.IsWoodSource : (x, y) => !HouseInspector.IsProtected(x, y) && !TileQuery.InOcean(x, y) && Tiles.Accepts(x, y) && !Bot.Hands.IsReclaimable(new Point(x, y));
		var visible = WorldScan.NearestVisibleTile(origin, Tiles.TileTypes, accept, unreachable, knowledge);
		if (visible != null)
			return visible;
		var known = knowledge.NearestKnown(origin, Tiles.TileTypes, unreachable, KnowledgeSearchRadius);
		return known is Point tile && accept(tile.X, tile.Y) ? known : null;
	}

	void Prospect()
	{
		if (prospectTarget is not Vector2 destination || Bot.Motor.HasArrived)
		{
			destination = ChooseProspectTarget();
			prospectTarget = destination;
			Bot.Motor.Stop();
		}
		bool moving = DeepOre() ? TravelOrTunnel(destination, 32f) : MoveToward(destination);
		if (moving)
			return;
		prospectDirection = -prospectDirection;
		prospectTarget = null;
		Bot.Motor.Stop();
	}

	bool MoveToward(Vector2 destination)
	{
		Bot.Motor.MoveTo(destination, 32f);
		return !Bot.Motor.IsStuck;
	}

	const int DeepOreMarginTiles = 20;

	static readonly HashSet<int> DeepOres = new()
	{
		TileID.Cobalt, TileID.Palladium, TileID.Mythril, TileID.Orichalcum, TileID.Adamantite, TileID.Titanium
	};

	bool DeepOre() => Tiles.TileTypes.Overlaps(DeepOres);

	Vector2 ChooseProspectTarget()
	{
		var here = Bot.CenterTile;
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		if (Tiles.Ground is Landmark ground && !Grounds.IsInside(knowledge, ground, here) && Grounds.PickSpot(knowledge, ground, here) is Vector2 spot)
			return spot;
		if (Tiles.IsTree || Tiles.OnSurface)
		{
			int x = Utils.Clamp(here.X + prospectDirection * TreeProspectTiles, 50, Main.maxTilesX - 50);
			if (Tiles.Ground is Landmark home && knowledge.SpanOf(home) is var (minX, maxX) && (x < minX || x > maxX))
			{
				prospectDirection = -prospectDirection;
				x = Utils.Clamp(here.X + prospectDirection * TreeProspectTiles, minX, maxX);
			}
			return new Vector2(x * 16f, TileQuery.SurfaceY(x) * 16f - 24f);
		}
		if (DeepOre() && here.Y < Main.rockLayer + DeepOreMarginTiles)
		{
			int x = Utils.Clamp(here.X + prospectDirection * ProspectStepTiles, 50, Main.maxTilesX - 50);
			return new Vector2(x * 16f, (float)(Main.rockLayer + DeepOreMarginTiles * 2) * 16f);
		}
		if (!DeepOre() && !Tiles.TileTypes.Contains(TileID.Hellstone))
			return SidewaysProspect(here);
		if (CaveFinder.NearestUnvisitedCave(here, visitedCaves) is Point cave && (!DeepOre() || cave.Y > Main.rockLayer))
		{
			visitedCaves.Add(CaveFinder.RegionOf(cave));
			return cave.ToWorldCoordinates();
		}
		bool deepEnough = here.Y > Main.UnderworldLayer - 60;
		int targetX = Utils.Clamp(here.X + prospectDirection * ProspectStepTiles, 50, Main.maxTilesX - 50);
		int targetY = deepEnough ? here.Y : here.Y + ProspectDepthTiles;
		return new Vector2(targetX * 16f, targetY * 16f);
	}

	const int SidewaysStepTiles = 40;
	const float LavaFreeShare = 0.35f;

	static int LavaFreeDepth => (int)(Main.rockLayer + (Main.UnderworldLayer - Main.rockLayer) * LavaFreeShare);

	Vector2 SidewaysProspect(Point here)
	{
		int deepest = LavaFreeDepth;
		int edge = here.X + prospectDirection * SidewaysStepTiles;
		if (edge < 50 || edge > Main.maxTilesX - 50)
			prospectDirection = -prospectDirection;
		if (here.Y <= deepest && CaveFinder.NearestUnvisitedCave(here, visitedCaves, prospectDirection, deepest) is Point cave)
		{
			visitedCaves.Add(CaveFinder.RegionOf(cave));
			return cave.ToWorldCoordinates();
		}
		int x = Utils.Clamp(here.X + prospectDirection * SidewaysStepTiles, 50, Main.maxTilesX - 50);
		int shallowest = (int)Main.worldSurface + ProspectDepthTiles / 2;
		int y = here.Y < shallowest ? here.Y + ProspectDepthTiles : Math.Min(here.Y, deepest);
		return new Vector2(x * 16f, y * 16f);
	}
}

public sealed class PickupTask : BotTask
{
	const float SearchRadius = 16f * 20f;
	const int ForgetUnreachableTicks = 600;

	readonly HashSet<int> unreachable = new();
	int itemIndex = -1;
	uint unreachableClearedAt;

	public PickupTask(Bot bot) : base(bot) { }

	public override string NameKey => "Pickup";

	public override float Evaluate()
	{
		if (Main.GameUpdateCount - unreachableClearedAt > ForgetUnreachableTicks)
		{
			unreachable.Clear();
			unreachableClearedAt = Main.GameUpdateCount;
		}
		itemIndex = NearestWantedItem();
		if (itemIndex < 0)
			return 0f;
		var item = Main.item[itemIndex];
		Detail = item.Name;
		if (item.type == ItemID.FallenStar)
			return 58f;
		if ((item.timeSinceItemSpawned < FreshDropTicks || IsMinedTreasure(item) || ResourceSources.IsWood(item.type)) && Vector2.Distance(item.Center, Bot.Center) < FreshDropRadius)
			return FreshDropPriority;
		bool planned = Bot.Brain.Plan is GatherStep gather && gather.ItemType == item.type;
		return planned ? 60f : 40f;
	}

	const float FreshDropRadius = 16f * 8f;
	const float FreshDropPriority = 86f;
	const int FreshDropTicks = 600;

	static readonly HashSet<int> Gems = new() { ItemID.Amethyst, ItemID.Topaz, ItemID.Sapphire, ItemID.Emerald, ItemID.Ruby, ItemID.Diamond, ItemID.Amber };

	static bool IsMinedTreasure(Item item) =>
		Gems.Contains(item.type) || item.createTile >= 0 && TileID.Sets.Ore[item.createTile];

	int NearestWantedItem()
	{
		int fallenStar = NearestVisibleFallenStar();
		if (fallenStar >= 0)
			return fallenStar;
		int nearest = -1;
		float nearestDistance = SearchRadius;
		for (int i = 0; i < Main.maxItems; i++)
		{
			var item = Main.item[i];
			if (!item.active || unreachable.Contains(i) || ItemReservations.IsReservedForOther(i, Bot) || Junk.IsUnwanted(Bot, item))
				continue;
			if (!Inventory.HasRoomFor(Avatar, item) && !Bot.Brain.PlannedItemTypes.Contains(item.type))
				continue;
			float distance = Vector2.Distance(item.Center, Bot.Center);
			if (distance < nearestDistance)
			{
				nearest = i;
				nearestDistance = distance;
			}
		}
		if (nearest >= 0 && !Inventory.HasRoomFor(Avatar, Main.item[nearest]) && !Junk.MakeRoomFor(Bot, Main.item[nearest]))
			return -1;
		return nearest;
	}

	int NearestVisibleFallenStar()
	{
		var origin = Bot.CenterTile;
		int nearest = -1;
		long nearestDistance = long.MaxValue;
		for (int i = 0; i < Main.maxItems; i++)
		{
			var item = Main.item[i];
			if (!item.active || item.type != ItemID.FallenStar || unreachable.Contains(i) || !Inventory.HasRoomFor(Avatar, item))
				continue;
			var tile = item.Center.ToTileCoordinates();
			if (Math.Abs(tile.X - origin.X) > WorldScan.ViewHalfWidth || Math.Abs(tile.Y - origin.Y) > WorldScan.ViewHalfHeight)
				continue;
			long distance = WorldScan.DistanceSq(origin, tile);
			if (distance < nearestDistance)
			{
				nearest = i;
				nearestDistance = distance;
			}
		}
		return nearest;
	}

	public override void Tick()
	{
		if (itemIndex < 0 || !Main.item[itemIndex].active)
		{
			IsFinished = true;
			return;
		}
		Bot.Motor.MoveTo(Main.item[itemIndex].Center, 8f);
		if (!Bot.Motor.IsStuck)
			return;
		unreachable.Add(itemIndex);
		IsFinished = true;
	}
}
