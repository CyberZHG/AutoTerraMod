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

public sealed record DeliverStep(int ItemType, int RecipientId) : PlanStep;

public sealed record SummonBossStep(int BossType, int SummonItem) : PlanStep;

public abstract record ResourceSource;

public sealed record TileSource(ISet<int> TileTypes, bool IsTree, Func<int, int, bool> Accept = null, bool OnSurface = false) : ResourceSource
{
	public bool Accepts(int x, int y) => Accept?.Invoke(x, y) ?? true;
}

public sealed record NpcSource(ISet<int> NpcTypes) : ResourceSource;
