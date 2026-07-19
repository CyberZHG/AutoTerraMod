using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class BuildTask : BotTask
{
	const int FailuresBeforeSkipping = 12;
	const float ApproachRadius = 16f * 2f;

	const uint OperationTimeoutTicks = 1200;
	const float NearOperationDistance = 16f * 10f;
	const float FarOperationDistance = 16f * 40f;
	const float StepAsideRadius = 6f;

	BuildOperation? lastOperation;
	uint operationStartedAt;
	HouseBlueprint structure;
	int failures;

	public BuildTask(Bot bot) : base(bot) { }

	public override string NameKey => "Build";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not BuildHouseStep build || Bot.Faction.Settlement == null)
			return 0f;
		structure = Bot.Faction.Settlement.Structures[build.Operation.Structure];
		Detail = Terraria.Localization.Language.GetTextValue($"Mods.AutoTerraMod.BuildActions.{build.Operation.Action}");
		return 50f + Traits.Building * 40f;
	}

	public override void Tick()
	{
		if (structure == null || HouseInspector.NextOperation(Bot.Faction, structure) is not BuildOperation operation)
		{
			HouseInspector.Invalidate(Bot.Faction);
			IsFinished = true;
			return;
		}
		if (operation != lastOperation)
		{
			lastOperation = operation;
			failures = 0;
			operationStartedAt = Main.GameUpdateCount;
		}
		bool near = Vector2.Distance(Bot.Center, operation.Tile.ToWorldCoordinates()) < NearOperationDistance;
		if (!near)
			operationStartedAt = Main.GameUpdateCount;
		else if (Main.GameUpdateCount - operationStartedAt > OperationTimeoutTicks)
		{
			HouseInspector.Skip(Bot, operation);
			Bot.Motor.Stop();
			lastOperation = null;
			return;
		}
		Bot.Motor.ScaffoldingAllowed = true;
		var tool = ToolFor(operation);
		if (tool == null)
		{
			IsFinished = true;
			return;
		}
		if (BodyOverlaps(operation) && Bot.Npc is NPC body)
		{
			var feet = Navigation.PathFinder.FeetOf(body);
			int side = body.Center.X >= operation.Tile.X * 16f + 8f ? operation.Tile.X + 1 : operation.Tile.X - 2;
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(new Point(side, feet.Y)), StepAsideRadius);
			if (Bot.Motor.IsStuck)
			{
				HouseInspector.Skip(Bot, operation);
				Bot.Motor.Stop();
			}
			return;
		}
		if (!Bot.Hands.CanReach(tool, operation.Tile))
		{
			if (Vector2.Distance(Bot.Center, operation.Tile.ToWorldCoordinates()) > FarOperationDistance)
			{
				if (!TravelOrTunnel(operation.Tile.ToWorldCoordinates(), NearOperationDistance * 0.5f))
				{
					HouseInspector.Skip(Bot, operation);
					Bot.Motor.Stop();
				}
				return;
			}
			Bot.Motor.MoveTo(StandingSpotFor(operation), ApproachRadius);
			if (Bot.Motor.IsStuck)
			{
				HouseInspector.Skip(Bot, operation);
				Bot.Motor.Stop();
			}
			return;
		}
		Bot.Motor.Stop();
		if (!Bot.Hands.Ready)
			return;
		if (Execute(operation, tool))
		{
			failures = 0;
			HouseInspector.ForgetPlannedOperations(Bot.Faction);
		}
		else if (++failures >= FailuresBeforeSkipping)
		{
			BotDiagnostics.Log(Bot, $"{operation.Action} with {tool.Name} (tile {tool.createTile} style {tool.placeStyle}) keeps failing at {operation.Tile}{TerrainDump.Around(operation.Tile, 8, 4)}");
			HouseInspector.Skip(Bot, operation);
			Bot.Motor.Stop();
		}
	}

	Item ToolFor(BuildOperation operation) => operation.Action switch
	{
		BuildAction.ChopTree => Bot.Hands.BestAxe,
		BuildAction.ClearTile or BuildAction.RemoveScaffold => Bot.Hands.BestPickaxe ?? Avatar.inventory[Avatar.selectedItem],
		BuildAction.PlaceTable when structure.Purpose == StructurePurpose.Bedroom && Inventory.Items(Avatar).FirstOrDefault(item => item.createTile == TileID.WorkBenches) is Item bench => bench,
		_ => HouseMaterials.Find(Avatar, HouseMaterials.ReplacementFor(operation.Action), HouseInspector.MaterialsFor(Bot.Faction))
	};

	const int FailuresBeforeClearingRoom = 6;

	bool ClearFurnitureForBed()
	{
		if (failures < FailuresBeforeClearingRoom || Bot.Hands.BestPickaxe is not Item pickaxe)
			return false;
		for (int x = structure.Left + 1; x < structure.Right; x++)
		{
			var spot = new Point(x, structure.Floor - 1);
			int type = TileQuery.TypeAt(spot);
			if (type < 0 || !TileID.Sets.RoomNeeds.CountsAsTable.Contains(type) && !TileID.Sets.RoomNeeds.CountsAsChair.Contains(type)
				&& !TileQuery.IsPlatform(spot.X, spot.Y) && !Main.tileRope[type])
				continue;
			if (!Bot.Hands.CanReach(pickaxe, spot))
				continue;
			BotDiagnostics.Log(Bot, $"removing the {TileID.Search.GetName(type)} at {spot} to fit a bed");
			return Bot.Hands.Dig(spot) == ToolResult.Working;
		}
		return false;
	}

	bool BodyOverlaps(BuildOperation operation)
	{
		if (operation.Action is not (BuildAction.PlaceBlock or BuildAction.PlaceDoor or BuildAction.PlaceTable or BuildAction.PlaceChair or BuildAction.PlaceBed))
			return false;
		var tileArea = new Rectangle(operation.Tile.X * 16, operation.Tile.Y * 16, 16, 16);
		return Bot.Npc.Hitbox.Intersects(tileArea);
	}

	Vector2 StandingSpotFor(BuildOperation operation)
	{
		var blueprint = structure;
		var here = Bot.CenterTile;
		bool solidTarget = operation.Action is BuildAction.ClearTile or BuildAction.RemoveScaffold or BuildAction.RenovateBlock;
		if ((operation.Tile.Y < blueprint.Ceiling || solidTarget) && FootholdNear(operation.Tile) is Point foothold)
			return Navigation.PathFinder.BodyCenterAt(foothold);
		if (operation.Tile.Y < blueprint.Ceiling)
		{
			int towardMiddle = operation.Tile.X <= (blueprint.Left + blueprint.Right) / 2 ? 2 : -2;
			int x = System.Math.Clamp(operation.Tile.X + towardMiddle, blueprint.Left, blueprint.Right - 1);
			return Navigation.PathFinder.BodyCenterAt(new Point(x, blueprint.Ceiling - 1));
		}
		if (BodyOverlaps(operation))
		{
			int awayX = operation.Tile.X <= (blueprint.Left + blueprint.Right) / 2 ? operation.Tile.X + 3 : operation.Tile.X - 3;
			return new Vector2(awayX * 16f + 8f, (blueprint.Floor - 1) * 16f - 8f);
		}
		return operation.Tile.ToWorldCoordinates();
	}

	const int FootholdReachX = 3;
	const int FootholdBelowMin = -2;
	const int FootholdBelowMax = 4;

	static Point? FootholdNear(Point tile)
	{
		Point? best = null;
		int bestScore = int.MaxValue;
		for (int dy = FootholdBelowMin; dy <= FootholdBelowMax; dy++)
		{
			for (int dx = -FootholdReachX; dx <= FootholdReachX; dx++)
			{
				var feet = new Point(tile.X + dx, tile.Y + dy);
				if (!Standable(feet) || Overlaps(feet, tile))
					continue;
				int score = System.Math.Abs(dx) + System.Math.Abs(dy);
				if (score < bestScore)
				{
					best = feet;
					bestScore = score;
				}
			}
		}
		return best;
	}

	static bool Standable(Point feet)
	{
		for (int y = feet.Y - 2; y <= feet.Y; y++)
		{
			if (TileQuery.IsSolid(feet.X, y) || TileQuery.IsSolid(feet.X + 1, y))
				return false;
		}
		return Navigation.PathFinder.IsSupportedNow(feet.X, feet.Y);
	}

	static bool Overlaps(Point feet, Point tile) => tile.X >= feet.X && tile.X <= feet.X + 1 && tile.Y >= feet.Y - 2 && tile.Y <= feet.Y;

	static void Flatten(int x, int y)
	{
		var anchor = Main.tile[x, y];
		if (!anchor.HasTile || !anchor.IsHalfBlock && anchor.Slope == SlopeType.Solid)
			return;
		anchor.IsHalfBlock = false;
		anchor.Slope = SlopeType.Solid;
		WorldGen.SquareTileFrame(x, y);
	}

	bool Execute(BuildOperation operation, Item tool)
	{
		var tile = operation.Tile;
		switch (operation.Action)
		{
			case BuildAction.ChopTree:
				return Bot.Hands.Chop(tile) == ToolResult.Working;
			case BuildAction.RemoveScaffold:
				return Bot.Hands.Dig(tile) == ToolResult.Working;
			case BuildAction.RenovateBlock:
				return Bot.Hands.ReplaceBlock(tool, tile) == ToolResult.Working;
			case BuildAction.RenovateWall:
				return Bot.Hands.ReplaceWall(tool, tile);
			case BuildAction.ClearTile:
				if (TileQuery.IsSolid(tile))
					return Bot.Hands.Dig(tile) == ToolResult.Working;
				WorldGen.KillTile(tile.X, tile.Y);
				return true;
			case BuildAction.PlaceWall:
				return Bot.Hands.PlaceWall(tool, tile);
			case BuildAction.PlaceTable:
			case BuildAction.PlaceChair:
			case BuildAction.PlaceBed:
				for (int x = structure.Left; x <= structure.Right; x++)
					Flatten(x, structure.Floor);
				foreach (var spot in structure.FurnitureSpots(tile))
				{
					if (Bot.Hands.CanReach(tool, spot) && Bot.Hands.Place(tool, spot))
						return true;
				}
				return operation.Action == BuildAction.PlaceBed && ClearFurnitureForBed();
			case BuildAction.PlaceDoor:
				Flatten(tile.X, tile.Y - 2);
				Flatten(tile.X, tile.Y + 2);
				return TileQuery.IsSolid(tile.X, tile.Y - 2) && TileQuery.IsSolid(tile.X, tile.Y + 2) && Bot.Hands.Place(tool, tile);
			case BuildAction.PlaceBlock when Main.tile[tile.X, tile.Y].HasTile && !TileQuery.IsSolid(tile) && !Main.tileCut[Main.tile[tile.X, tile.Y].TileType]:
				if (TileQuery.IsPlatform(tile.X, tile.Y) || !Main.tileFrameImportant[Main.tile[tile.X, tile.Y].TileType])
					return Bot.Hands.Dig(tile) == ToolResult.Working;
				return false;
			default:
				return Bot.Hands.Place(tool, tile);
		}
	}
}
