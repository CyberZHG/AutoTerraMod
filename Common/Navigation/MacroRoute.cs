using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Navigation;

public sealed class MacroRoute
{
	const int LegTiles = 40;
	const int LegReachedTiles = 4;
	const int OffRouteTiles = 15;
	const int ReplanTicks = 600;
	const int MinReplanTicks = 180;
	const int GoalMovedTiles = 12;
	const int MazeRouteNodes = 60000;
	const int LongTripRouteNodes = 30000;

	PathResult route = PathResult.None;
	Point goal;
	int legIndex = -1;
	uint plannedAt;

	public string Planned { get; private set; }

	public void ClearPlanned() => Planned = null;

	public void Clear()
	{
		route = PathResult.None;
		legIndex = -1;
		plannedAt = 0;
	}

	public Vector2? LegToward(Point feet, Point target, int goalRadius, NavigationAbilities abilities, bool straightLineFailed)
	{
		bool maze = PathFinder.InsideMaze(target);
		if (!maze && !straightLineFailed || Distance(feet, target) <= (maze ? LegTiles : PathFinder.LongTripTiles))
		{
			Clear();
			return null;
		}
		if (NeedsPlan(feet, target) && PathFinder.HasBudget)
			Plan(feet, target, goalRadius, abilities, maze ? MazeRouteNodes : LongTripRouteNodes);
		if (legIndex < 0)
			return null;
		if (Distance(feet, route.Nodes[legIndex].Feet) <= LegReachedTiles)
			legIndex = FarthestLegFrom(feet, legIndex);
		var leg = route.Nodes[legIndex].Feet;
		return new Vector2(leg.X * 16f + 8f, leg.Y * 16f - 13f);
	}

	bool NeedsPlan(Point feet, Point target)
	{
		if (plannedAt == 0)
			return true;
		uint since = Main.GameUpdateCount - plannedAt;
		if (since < MinReplanTicks)
			return false;
		bool goalMoved = Distance(goal, target) > Math.Max(GoalMovedTiles, Distance(feet, target) / 4);
		bool barelyMoved = route.Nodes.Count <= LegTiles / 4;
		bool reachedPartialEnd = legIndex == route.Nodes.Count - 1 && !route.ReachesGoal && Distance(feet, route.Nodes[legIndex].Feet) <= LegReachedTiles && (!barelyMoved || since > ReplanTicks);
		return goalMoved || reachedPartialEnd || legIndex < 0 || since > ReplanTicks && OffRoute(feet);
	}

	void Plan(Point feet, Point target, int goalRadius, NavigationAbilities abilities, int maxNodes)
	{
		plannedAt = Main.GameUpdateCount;
		goal = target;
		route = PathFinder.FindLongRoute(feet, target, goalRadius, abilities, maxNodes);
		legIndex = route.Nodes.Count == 0 ? -1 : FarthestLegFrom(feet, 0);
		Planned = $"long route {feet} -> {target}: nodes={route.Nodes.Count} reaches={route.ReachesGoal}";
	}

	int FarthestLegFrom(Point feet, int from)
	{
		int chosen = from;
		for (int index = from; index < route.Nodes.Count && Distance(feet, route.Nodes[index].Feet) <= LegTiles; index++)
			chosen = index;
		return chosen;
	}

	bool OffRoute(Point feet)
	{
		if (legIndex < 0)
			return true;
		for (int index = Math.Max(0, legIndex - LegTiles); index <= legIndex; index++)
		{
			if (Distance(feet, route.Nodes[index].Feet) <= OffRouteTiles)
				return false;
		}
		return true;
	}

	static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
