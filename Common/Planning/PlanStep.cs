using System;
using System.Collections.Generic;
using AutoTerraMod.Common.Building;
using Terraria;

namespace AutoTerraMod.Common.Planning;

public abstract record PlanStep
{
	public static readonly PlanStep Idle = new IdleStep();
	public static readonly PlanStep Blocked = new BlockedStep();
}

public sealed record IdleStep : PlanStep;

public sealed record BlockedStep : PlanStep;

public sealed record GatherStep(int ItemType, int Amount, ResourceSource Source) : PlanStep;

public sealed record CraftStep(Recipe Recipe) : PlanStep;

public sealed record PlaceStationStep(int ItemType) : PlanStep;

public sealed record BuildHouseStep(BuildOperation Operation) : PlanStep;

public sealed record WithdrawStep(int ItemType, int Amount) : PlanStep;

public sealed record DeliverStep(int ItemType, int RecipientId, int Keep = 0, bool HandOver = false) : PlanStep;

public sealed record SummonBossStep(Bosses.BossChallenge Challenge, Bosses.BossTrigger Trigger, Microsoft.Xna.Framework.Vector2 Place, Microsoft.Xna.Framework.Point? Shrine) : PlanStep;

public sealed record ScoutStep(World.Landmark Landmark, bool Underground, int SeekTile = -1) : PlanStep;
public sealed record SurveyStep(Microsoft.Xna.Framework.Point Target) : PlanStep;

public sealed record SmashShrineStep(Microsoft.Xna.Framework.Point Shrine) : PlanStep;

public sealed record DigShaftStep(World.Hellevator Shaft) : PlanStep;

public sealed record BuildBridgeStep(World.ICorridor Bridge, int Direction) : PlanStep;

public sealed record HangRopeStep(int X, int Top, int Bottom) : PlanStep;

public sealed record BuyStep(int ItemType, int Amount, int SellerType) : PlanStep;

public sealed record BuildOutpostStep : PlanStep;

public sealed record ClaimOutpostStep(Microsoft.Xna.Framework.Point Bed) : PlanStep;

public sealed record TravelStep(Microsoft.Xna.Framework.Vector2 Place) : PlanStep;

public sealed record FillTileStep(Microsoft.Xna.Framework.Point Tile) : PlanStep;

public sealed record BuildSkyArenaStep : PlanStep;

public sealed record DigTempleShaftStep : PlanStep;

public abstract record ResourceSource;

public sealed record TileSource(ISet<int> TileTypes, bool IsTree, Func<int, int, bool> Accept = null, bool OnSurface = false, World.Landmark? Ground = null) : ResourceSource
{
	public bool Accepts(int x, int y) => Accept?.Invoke(x, y) ?? true;

	public bool Cavern { get; init; }
}

public sealed record NpcSource(ISet<int> NpcTypes, World.Landmark? Ground = null) : ResourceSource
{
	public bool IsRare { get; init; }
	public bool Underground { get; init; }
	public bool Sky { get; init; }
	public bool Critter { get; init; }
}
