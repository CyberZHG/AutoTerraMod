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

public abstract record ResourceSource;

public sealed record TileSource(ISet<int> TileTypes, bool IsTree) : ResourceSource;

public sealed record NpcSource(ISet<int> NpcTypes) : ResourceSource;
