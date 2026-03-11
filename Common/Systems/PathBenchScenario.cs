using System.Collections.Generic;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class PathBenchScenario : ModSystem
{
	bool done;

	public override void PostUpdateWorld()
	{
		if (done || HeadlessTestMode.Scenario != "pathbench" || Main.GameUpdateCount < 120)
			return;
		done = true;
		var rand = new System.Random(7);
		var pairs = new List<(Point, Point)>();
		while (pairs.Count < 300)
		{
			var a = Standable(rand);
			var b = Standable(rand);
			if (a is Point start && b is Point goal && System.Math.Abs(start.X - goal.X) < 80 && System.Math.Abs(start.Y - goal.Y) < 60)
				pairs.Add((start, goal));
		}
		var hook = new NavigationAbilities(35, true, true, true, true, JumpRise: 5, HookReach: 17, CanChop: true);
		foreach (var (label, hangs) in new[] { ("warm", true), ("hang-on", true), ("hang-off", false) })
		{
			PathSearch.HangEdges = hangs;
			var watch = System.Diagnostics.Stopwatch.StartNew();
			int reached = 0, nodes = 0;
			foreach (var (start, goal) in pairs)
			{
				var result = PathFinder.Find(start, goal, 1, hook);
				if (result.ReachesGoal)
					reached++;
				nodes += result.Nodes.Count;
			}
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"pathbench {label}: {watch.Elapsed.TotalMilliseconds:0}ms reached={reached}/{pairs.Count} pathLength={nodes}");
		}
		PathSearch.HangEdges = true;
	}

	static Point? Standable(System.Random rand)
	{
		int x = rand.Next(1900, 2400);
		for (int y = rand.Next(250, 700); y < 800; y++)
		{
			if (TileQuery.CanStandOn(x, y + 1) && TileQuery.CanStandOn(x + 1, y + 1) && !TileQuery.IsSolid(x, y) && !TileQuery.IsSolid(x, y - 1) && !TileQuery.IsSolid(x, y - 2) && !TileQuery.IsSolid(x + 1, y) && !TileQuery.IsSolid(x + 1, y - 1) && !TileQuery.IsSolid(x + 1, y - 2))
				return new Point(x, y);
		}
		return null;
	}
}
