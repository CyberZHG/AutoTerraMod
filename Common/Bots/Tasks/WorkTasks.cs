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
			WalkToStation(missingStation);
			return;
		}
		if (ProgressionPlanner.NeedsWater(recipe) && !WaterFinder.IsNearWater(Bot.CenterTile))
		{
			WalkToWater();
			return;
		}
		Bot.Motor.Stop();
		if (!Crafting.HasIngredients(Avatar, recipe))
		{
			BotDiagnostics.Log(Bot, $"missing ingredients for {recipe.createItem.Name}");
			IsFinished = true;
			return;
		}
		if (++craftTimer < CraftDelayTicks)
			return;
		if (Crafting.TryCraft(Avatar, recipe))
			Bot.Watchdog.RecordProgress();
		BotDiagnostics.Log(Bot, $"crafted {recipe.createItem.Name}");
		IsFinished = true;
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
		Bot.Motor.MoveTo(station.ToWorldCoordinates(), StationArrivalRadius);
		if (Bot.Motor.IsStuck)
		{
			BotDiagnostics.Log(Bot, $"stuck walking to station tile {stationTile} at {station}; motor: {Bot.Motor.Describe()}{World.TerrainDump.Around(Bot.CenterTile, 30, 12)}");
			IsFinished = true;
		}
	}

	void TickPlace(int itemType)
	{
		var item = Inventory.Items(Avatar).FirstOrDefault(candidate => candidate.type == itemType);
		if (item == null)
		{
			IsFinished = true;
			return;
		}
		var spot = Stations.PlacementSpots(Bot.Faction, item).Cast<Point?>().FirstOrDefault(candidate => Bot.Hands.CanPlaceAt(item, candidate.Value));
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
		if (!Bot.Hands.Ready || !Bot.Hands.Place(item, target))
			return;
		Stations.Invalidate(Bot.Faction);
		BaseStorage.Reset();
		BotDiagnostics.Log(Bot, $"placed {item.Name} at {target}");
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
		if (Bot.Brain.Plan is not GatherStep { Source: TileSource tiles } gather)
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
	}

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
		var tool = Tiles.IsTree ? Bot.Hands.BestAxe : Bot.Hands.BestPickaxe;
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
			var result = Tiles.IsTree ? Bot.Hands.Chop(tile)
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
		return !Tiles.IsTree || TileQuery.IsTreeBase(tile.X, tile.Y);
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
		Func<int, int, bool> accept = Tiles.IsTree ? TileQuery.IsTreeBase : (x, y) => !HouseInspector.IsProtected(x, y) && Tiles.Accepts(x, y);
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
		Bot.Motor.MoveTo(destination, 32f);
		if (!Bot.Motor.IsStuck)
			return;
		prospectDirection = -prospectDirection;
		prospectTarget = null;
		Bot.Motor.Stop();
	}

	Vector2 ChooseProspectTarget()
	{
		var here = Bot.CenterTile;
		if (Tiles.IsTree || Tiles.OnSurface)
		{
			int x = Utils.Clamp(here.X + prospectDirection * TreeProspectTiles, 50, Main.maxTilesX - 50);
			return new Vector2(x * 16f, TileQuery.SurfaceY(x) * 16f - 24f);
		}
		if (CaveFinder.NearestUnvisitedCave(here, visitedCaves) is Point cave)
		{
			visitedCaves.Add(CaveFinder.RegionOf(cave));
			return cave.ToWorldCoordinates();
		}
		bool deepEnough = here.Y > Main.UnderworldLayer - 60;
		int targetX = Utils.Clamp(here.X + prospectDirection * ProspectStepTiles, 50, Main.maxTilesX - 50);
		int targetY = deepEnough ? here.Y : here.Y + ProspectDepthTiles;
		return new Vector2(targetX * 16f, targetY * 16f);
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
		bool planned = Bot.Brain.Plan is GatherStep gather && gather.ItemType == item.type;
		return planned ? 60f : 40f;
	}

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
			if (!item.active || unreachable.Contains(i) || !Inventory.HasRoomFor(Avatar, item) || Junk.IsUnwanted(Bot, item))
				continue;
			float distance = Vector2.Distance(item.Center, Bot.Center);
			if (distance < nearestDistance)
			{
				nearest = i;
				nearestDistance = distance;
			}
		}
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
