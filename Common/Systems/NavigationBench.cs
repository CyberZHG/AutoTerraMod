using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Bots.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class NavigationBench : ModSystem
{
	sealed record BenchCase(string Name, string[] Map, Point WorldStart = default, Point WorldGoal = default)
	{
		public bool InWorld => Map == null;
	}

	const int ArenaTop = 60;
	const int ArenaLeft = 300;
	const int ArenaSpacing = 60;
	const int TimeoutTicks = 30 * 60;
	const int WorldTripTimeoutTicks = 90 * 60;
	const int SettleTicks = 60;
	const float ArrivalDistance = 40f;
	const int BuildingBlocks = 60;
	const int BenchRopes = 40;
	const int BenchPlatforms = 40;

	static readonly BenchCase[] BuiltInCases =
	{
		new("Flat", new[]
		{
			"################################",
			"#..............................#",
			"#..............................#",
			"#..............................#",
			"#S............................G#",
			"################################"
		}),
		new("Stairs", new[]
		{
			"################################",
			"#..............................#",
			"#..............................#",
			"#..............................#",
			"#..........................G...#",
			"#.....................##########",
			"#..................#############",
			"#...............################",
			"#S...........###################",
			"################################"
		}),
		new("Ledge", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.............G......#",
			"#S.........###########",
			"#..........###########",
			"######################"
		}),
		new("Shaft", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.................G..#",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######S.#############",
			"######################"
		}),
		new("RopePit", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.................G..#",
			"#######..#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######S!#############",
			"######################"
		}),
		new("RopeWall", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.................G..#",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######.!#############",
			"#######S!#############",
			"######################"
		}),
		new("SideHole", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S...................#",
			"#########....#########",
			"#########....#########",
			"#########....#########",
			"#########....#########",
			"#########..###########",
			"#########..###########",
			"#########..........###",
			"#########..........###",
			"#########..........G.#",
			"######################"
		}),
		new("Gap", new[]
		{
			"################################",
			"#..............................#",
			"#..............................#",
			"#..............................#",
			"#..............................#",
			"#S...........###............G..#",
			"##########...###################",
			"##########...###################",
			"##########...###################",
			"##########...###################",
			"##########.......###############",
			"################################"
		}),
		new("Drop", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S...................#",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######....############",
			"######..............##",
			"######..............##",
			"######..............G#",
			"######################"
		}),
		new("Platforms", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.............G......#",
			"#.....========########",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.....========.......#",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S....========.......#",
			"######################"
		}),
		new("WaterShaft", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.................G..#",
			"#######~~#############",
			"#######~~#############",
			"#######~~#############",
			"#######~~#############",
			"#######~~#############",
			"#######~~#############",
			"#######~~#############",
			"#######S~#############",
			"######################"
		}),
		new("DirtWall", new[]
		{
			"################################",
			"#..........dddddd..............#",
			"#..........dddddd..............#",
			"#..........dddddd..............#",
			"#S.........dddddd.............G#",
			"################################"
		}),
		new("DigDown", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S...................#",
			"#dddddddddddddddddddd#",
			"#dddddddddddddddddddd#",
			"#dddddddddddddddddddd#",
			"#dddddddddddddddddddd#",
			"#dddddddddddddddddddd#",
			"#dddddddddddddddddddd#",
			"#....................#",
			"#....................#",
			"#..........G.........#",
			"######################"
		}),
		new("RopeCliff", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#..............G.....#",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#S........############",
			"######################"
		}),
		new("NoRopeCliff", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#..............G.....#",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#.........############",
			"#S........############",
			"######################"
		}),
		new("DirtCliff", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#..............G.....#",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#.........dddddddddd##",
			"#S........dddddddddd##",
			"######################"
		}),
		new("LavaGap", new[]
		{
			"################################",
			"#..............................#",
			"#..............................#",
			"#..............................#",
			"#S............................G#",
			"###########.........############",
			"###########%%%%%%%%%############",
			"################################"
		}),
		new("Door", new[]
		{
			"################################",
			"#..............#...............#",
			"#..............................#",
			"#..............................#",
			"#S.............D.............G.#",
			"################################"
		}),
		new("DeepChasm", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S...................#",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########G..###########",
			"######################"
		}),
		new("ChasmClimb", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#...................G#",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########...###########",
			"########S..###########",
			"######################"
		}),
		new("HellevatorDown", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#S...................#",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########G~############",
			"######################"
		}),
		new("HellevatorUp", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#...................G#",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########..############",
			"########==############",
			"########..############",
			"########..############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########~~############",
			"########==############",
			"########~~############",
			"########~~############",
			"########S~############",
			"######################"
		}),
		new("WingShaft", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#....................#",
			"#.................G..#",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######..#############",
			"#######S.#############",
			"######################"
		}),
		new("CloudLedge", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#.............G......#",
			"#..........###########",
			"#..........###########",
			"#..........###########",
			"#..........###########",
			"#..........###########",
			"#..........###########",
			"#..........###########",
			"#S.........###########",
			"######################"
		}),
		new("HookCliff", new[]
		{
			"######################",
			"#....................#",
			"#....................#",
			"#..............G.....#",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#...........##########",
			"#S..........##########",
			"######################"
		}),
		new("CloudFall", FallShaft(42)),
		new("HookFall", FallShaft(70)),
		new("ClimbTreeLid", LidShaft(5, "#....T.......#", "#gggggggggggg#", "#dddddddddddd#", "#####..#######", "#####S.#######")),
		new("ClimbChestLid", LidShaft(7, "#....c.......#", "#gggggggggggg#", "#dddddddddddd#", "####.....#####", "####S....#####")),
		new("ClimbLeafLid", LidShaft(5, "#............#", "#llllllllllll#", "#llllllllllll#", "#####..#######", "#####S.#######")),
		new("NoRopeTreeWall", TreeWall()),
		new("PlatformJut", new[]
		{
			"##########",
			"#........#",
			"#........#",
			"#S.......#",
			"###...####",
			"###=..####",
			"###=..####",
			"###=.#####",
			"###=.#####",
			"###=.#####",
			"###=..####",
			"###=..####",
			"###=..####",
			"#........#",
			"#........#",
			"#.......G#",
			"##########"
		}),
		new("NarrowDrop", new[]
		{
			"##########",
			"#........#",
			"#........#",
			"#S.......#",
			"####..####",
			"####..####",
			"####..####",
			"####..####",
			"####..####",
			"####G.####",
			"#####.####",
			"#####.####",
			"##########"
		}),
		new("PoolExit", new[]
		{
			"################",
			"#..............#",
			"#..............#",
			"#..........G...#",
			"####.......#####",
			"####~~~~~~~#####",
			"####~~~~~~~#####",
			"####~~~~~~~#####",
			"####~~~S~~~#####",
			"################"
		}),
		new("SwimDown", new[]
		{
			"##########",
			"#........#",
			"#........#",
			"#S.......#",
			"###~~~~###",
			"###~~~~###",
			"###~~~~~~#",
			"###~~~~~~#",
			"###~~~~~G#",
			"##########"
		}),
		new("RopeTunnel", new[]
		{
			"############",
			"#...########",
			"#.!........#",
			"#.!........#",
			"#.!.....G..#",
			"#.!.########",
			"#.!.########",
			"#.!.########",
			"#.!.########",
			"#.!.########",
			"#S!.########",
			"############"
		}),
	};

	static string[] LidShaft(int goalColumn, string surface, string grass, string soil, string shaft, string bottom)
	{
		var rows = new List<string> { new('#', surface.Length) };
		for (int row = 0; row < 22; row++)
			rows.Add("#" + new string('.', surface.Length - 2) + "#");
		rows[^1] = rows[^1][..goalColumn] + "G" + rows[^1][(goalColumn + 1)..];
		rows.AddRange(new[] { surface, grass, soil, shaft, shaft, bottom, new string('#', surface.Length) });
		return rows.ToArray();
	}

	static string[] TreeWall()
	{
		var rows = new List<string> { "################" };
		for (int row = 0; row < 14; row++)
			rows.Add("#..............#");
		rows.Add("#...........G..#");
		for (int row = 0; row < 8; row++)
			rows.Add("#.........WWWWW#");
		rows.Add("#S.......TWWWWW#");
		rows.Add("#gggggggggggggg#");
		rows.Add("################");
		return rows.ToArray();
	}

	static string[] FallShaft(int depth)
	{
		var rows = new List<string> { "##########", "#........#", "#........#", "####S.####" };
		for (int row = 0; row < depth; row++)
			rows.Add("####..####");
		rows.Add("#.....G..#");
		rows.Add("##########");
		return rows.ToArray();
	}

	static readonly string[] StuckCaptures =
	{
		"navcapture origin=2190,321 feet=2214,335 dest=2162,323 rows=#####~~~#######.............##....#!.#####.......;####~~~########.........#####......!.............;###~~~####### .....................!.............;##~~~~##### .......................!.............;####~~#.. ........................########.......;###### ................ ####......########.......;#####.................###.........#########......;.####.....######...######........###########.....;...#...............######........############....;...................#####.....#################...;..................#######..........#############.;.................#######.!..........##########...;..............#########..!..........######### ..#;................#######..!..........########.....;..............#########..!.#........########.....;.......... ..##########..!.#........########.....;............###########..!.#........########.. ..; .....    . ###########..!.#..........######~##..;~~~~~     #############..!.#..........##########.;~  ~###################..!.#......  . ##########.;~  ####################..!.#.  ..     ########## ;#####################....! #          ###########;#####################. . ! #          ###########;###################      ! #     ################;###################      !!#    #################;###################     !!!#   ##################;####################    !!!#   # ################;###############         !!!# #### ###############;###############          !!####### ##############",
		"navcapture origin=2224,346 feet=2248,360 dest=2256,281 rows=####################   ! #   ####################;###################    ! #     ##################;##################     ! #      #################; #################     ! #      #################;## #############       ! #      #################;## ############        ! #      #################;##############         ! #     ##################;##############         !!#    ###################;#############          !!# ######################;#############          !!#   ####################;#############          !!     ###################;#############          !!      ##################;#############          !! !     #################;#####~######~~  ~ ~~## #! !     #################;#####~##################! !      ################;##~~~~~~################!#!       ###############;##~~~~~~~#################!        ##############;##~~~~~~~ ################!       ###############;#~~~~~~~~ ################!     #################;~~~~~~~~~~~###############!   ###################;~~~~~~~~~~~###############!  ####################;~~~~~~~~~~ ##############.!.#####################;#~~~~~~~~~##############..!.#####################;##~~~~~~~~############  ..!.#####################;###~  ~~ ###########..  ..!. .###################;###~   ############ ......!...###...#############;################### ......!....#....#############;##################........!........##############;##################........!........##############"
	};

	BenchCase[] cases;

	BenchCase[] Cases => cases ??= (System.Environment.GetEnvironmentVariable("AUTOTERRA_NAV_ONLY_CAPTURES") == "1" ? CapturedCases() : BuiltInCases.Concat(StuckCaptures.Select((capture, index) => FromCapture(capture, index) with { Name = $"Stuck{index}" })).Concat(CapturedCases())).Where(Selected).ToArray();

	static bool Selected(BenchCase bench)
	{
		string filter = System.Environment.GetEnvironmentVariable("AUTOTERRA_NAV_FILTER");
		return string.IsNullOrEmpty(filter) || filter.Split(',').Any(name => bench.Name.StartsWith(name));
	}

	static IEnumerable<BenchCase> CapturedCases()
	{
		string path = System.Environment.GetEnvironmentVariable("AUTOTERRA_NAV_CAPTURES");
		if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
			yield break;
		int index = 0;
		foreach (string line in System.IO.File.ReadLines(path))
		{
			int start = line.IndexOf("navcapture ");
			if (start < 0)
				continue;
			yield return WorldTrips ? WorldTrip(line[start..], index++) : FromCapture(line[start..], index++);
		}
	}

	static bool WorldTrips => System.Environment.GetEnvironmentVariable("AUTOTERRA_NAV_WORLD") == "1";

	static Point CaptureField(string capture, string name)
	{
		string text = capture.Split(' ').First(part => part.StartsWith(name + "="))[(name.Length + 1)..];
		return new Point(int.Parse(text.Split(',')[0]), int.Parse(text.Split(',')[1]));
	}

	static BenchCase WorldTrip(string capture, int index)
	{
		var feet = CaptureField(capture, "feet");
		var dest = CaptureField(capture, "dest");
		return new BenchCase($"Trip{index}@{feet.X},{feet.Y}->{dest.X},{dest.Y}", null, feet, dest);
	}

	static BenchCase FromCapture(string capture, int index)
	{
		string Field(string name) => capture.Split(' ').First(part => part.StartsWith(name + "="))[(name.Length + 1)..];
		Point ParsePoint(string text) => new(int.Parse(text.Split(',')[0]), int.Parse(text.Split(',')[1]));
		var origin = ParsePoint(Field("origin"));
		var feet = ParsePoint(Field("feet"));
		var dest = ParsePoint(Field("dest"));
		var rows = capture[(capture.IndexOf("rows=") + 5)..].Split(';')
			.Select(row => "#" + new string(row.Select(glyph => glyph switch { '#' => 's', '!' or '=' or '~' or '%' => glyph, _ => '.' }).ToArray()) + "#")
			.ToList();
		string border = new('#', rows[0].Length);
		rows.Insert(0, border);
		rows.Add(border);
		var map = rows.Select(row => row.ToCharArray()).ToArray();
		int startX = feet.X - origin.X + 1, startY = feet.Y - origin.Y + 1;
		map[startY][startX] = 'S';
		var goal = NearestOpenCell(map, Utils.Clamp(dest.X - origin.X + 1, 1, map[0].Length - 2), Utils.Clamp(dest.Y - origin.Y + 1, 1, map.Length - 2));
		map[goal.Y][goal.X] = 'G';
		return new BenchCase($"Capture{index}@{feet.X},{feet.Y}", map.Select(row => new string(row)).ToArray());
	}

	static Point NearestOpenCell(char[][] map, int x, int y)
	{
		for (int radius = 0; radius < map.Length + map[0].Length; radius++)
		{
			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					int cx = x + dx, cy = y + dy;
					bool inside = cy > 3 && cy < map.Length - 1 && cx > 0 && cx < map[0].Length - 2;
					if (inside && map[cy][cx] == '.' && map[cy - 1][cx] == '.' && map[cy - 2][cx] == '.' && map[cy + 1][cx] != '.')
						return new Point(cx, cy);
				}
			}
		}
		return new Point(x, y);
	}

	int caseIndex = -1;
	uint caseStartedAt;
	Vector2 goal;
	readonly List<string> results = new();

	static bool Active => HeadlessTestMode.Scenario == "nav";

	public override void OnWorldUnload()
	{
		caseIndex = -1;
		results.Clear();
	}

	public override void PostUpdateWorld()
	{
		if (!Active || !BotWorldSystem.IsRunning || caseIndex >= Cases.Length)
			return;
		var runner = BotWorldSystem.Bots.FirstOrDefault();
		if (runner == null || !runner.IsAlive)
			return;
		if (caseIndex < 0)
		{
			foreach (var bot in BotWorldSystem.Bots)
				bot.Brain.Override = new HoldStillTask(bot);
			StartCase(runner, 0);
			return;
		}
		uint elapsed = Main.GameUpdateCount - caseStartedAt;
		if (Cases[caseIndex].InWorld)
		{
			runner.Avatar.statLife = runner.Avatar.statLifeMax2;
			runner.Avatar.immune = true;
			runner.Avatar.immuneTime = SettleTicks;
		}
		if (elapsed < SettleTicks)
			return;
		bool arrived = Vector2.Distance(runner.Center, goal) < ArrivalDistance;
		if (!arrived && elapsed < (Cases[caseIndex].InWorld ? WorldTripTimeoutTicks : TimeoutTicks))
			return;
		string verdict = arrived ? $"PASS in {(elapsed - SettleTicks) / 60f:0.0}s hp={runner.Avatar.statLife}/{runner.Avatar.statLifeMax2}" : $"FAIL at {runner.CenterTile}; motor: {runner.Motor.Describe()}; fresh search: {FreshSearch(runner)}; below: {Below(runner)} ropes={Inventory.Count(runner.Avatar, ItemID.Rope)} ready={runner.Hands.Ready}{World.TerrainDump.Around(runner.CenterTile, Cases[caseIndex].InWorld ? 40 : 16, Cases[caseIndex].InWorld ? 25 : 8)}";
		results.Add($"{Cases[caseIndex].Name}: {(arrived ? "PASS" : "FAIL")}");
		BotDiagnostics.LogWorld($"nav bench {Cases[caseIndex].Name}: {verdict}");
		if (caseIndex + 1 < Cases.Length)
		{
			StartCase(runner, caseIndex + 1);
			return;
		}
		caseIndex = Cases.Length;
		runner.Brain.Override = new HoldStillTask(runner);
		BotDiagnostics.LogWorld($"nav bench done: {results.Count(result => result.EndsWith("PASS"))}/{results.Count} passed ({string.Join(", ", results)})");
	}

	static string Below(Bot runner)
	{
		var feet = Navigation.PathFinder.FeetOf(runner.Npc);
		return string.Join(" ", Enumerable.Range(feet.Y - 1, 5).Select(y => $"{y}:{(Main.tile[feet.X, y].HasTile ? Main.tile[feet.X, y].TileType.ToString() : "-")}/{(Main.tile[feet.X + 1, y].HasTile ? Main.tile[feet.X + 1, y].TileType.ToString() : "-")}"));
	}

	string FreshSearch(Bot runner)
	{
		var feet = Navigation.PathFinder.FeetOf(runner.Npc);
		var result = Navigation.PathFinder.Find(feet, new Point((int)(goal.X / 16f), (int)((goal.Y + 21f) / 16f)), 1, runner.Motor.Abilities());
		string fresh = $"{result.Nodes.Count} nodes reachesGoal={result.ReachesGoal} [{string.Join(" ", result.Nodes.Take(12).Select(node => $"{node.Feet.X},{node.Feet.Y}:{node.Arrival}"))}] abilities={runner.Motor.Abilities()}";
		if (!Cases[caseIndex].InWorld)
			return fresh;
		var whole = Navigation.PathFinder.FindLongRoute(feet, new Point((int)(goal.X / 16f), (int)((goal.Y + 21f) / 16f)), 1, runner.Motor.Abilities(), 120000);
		return $"{fresh}; whole route: {whole.Nodes.Count} nodes reachesGoal={whole.ReachesGoal} end={(whole.Nodes.Count > 0 ? whole.Nodes[^1].Feet : feet)} [{string.Join(" ", whole.Nodes.Where((node, i) => i % 8 == 0).Select(node => $"{node.Feet.X},{node.Feet.Y}:{node.Arrival}"))}]";
	}

	void StartCase(Bot runner, int index)
	{
		caseIndex = index;
		var bench = Cases[index];
		int left = ArenaLeft + index * ArenaSpacing;
		Point start = bench.WorldStart, target = bench.WorldGoal;
		for (int row = 0; !bench.InWorld && row < bench.Map.Length; row++)
		{
			for (int column = 0; column < bench.Map[row].Length; column++)
			{
				int x = left + column;
				int y = ArenaTop + row;
				char glyph = bench.Map[row][column];
				Build(x, y, glyph);
				if (glyph == 'S')
					start = new Point(x, y);
				if (glyph == 'G')
					target = new Point(x, y);
			}
		}
		for (int row = 0; !bench.InWorld && row < bench.Map.Length; row++)
		{
			for (int column = 0; column < bench.Map[row].Length; column++)
			{
				if (bench.Map[row][column] == 'D')
					WorldGen.PlaceDoor(left + column, ArenaTop + row - 1, TileID.ClosedDoor);
			}
		}
		for (int row = 0; !bench.InWorld && row < bench.Map.Length; row++)
		{
			for (int column = 0; column < bench.Map[row].Length; column++)
				WorldGen.SquareTileFrame(left + column, ArenaTop + row, true);
		}
		foreach (bool walls in new[] { false, true })
		{
			for (int row = 0; !bench.InWorld && row < bench.Map.Length; row++)
			{
				for (int column = 0; column < bench.Map[row].Length; column++)
				{
					if (bench.Map[row][column] == 'W' == walls)
						PlaceLate(left + column, ArenaTop + row, bench.Map[row][column]);
				}
			}
		}
		ClearClutter(runner);
		EquipBenchGear(runner, bench.Name);
		bool toolless = bench.Name.StartsWith("Wing") || bench.Name.StartsWith("Cloud") || bench.Name.StartsWith("Hook");
		if (!toolless)
		{
			if (runner.Hands.BestPickaxe == null)
				TopUp(runner, ItemID.CopperPickaxe, 1);
			if (runner.Hands.BestAxe == null)
				TopUp(runner, ItemID.CopperAxe, 1);
			TopUp(runner, ItemID.DirtBlock, BuildingBlocks);
		}
		if (bench.Name.StartsWith("NoRope") || toolless)
			Inventory.Consume(runner.Avatar, new int[] { ItemID.Rope }, Inventory.Count(runner.Avatar, ItemID.Rope));
		else
			TopUp(runner, ItemID.Rope, BenchRopes);
		if (!toolless)
			TopUp(runner, ItemID.WoodPlatform, BenchPlatforms);
		if (toolless)
			StripTools(runner);
		if (bench.InWorld)
			TopUp(runner, ItemID.TempleKey, 1);
		if (bench.Name.StartsWith("Hook"))
			TopUp(runner, ItemID.GrapplingHook, 1);
		var body = runner.Npc;
		body.position = new Vector2((start.X + 1) * 16f - body.width / 2f, (start.Y + 1) * 16f - body.height);
		body.velocity = Vector2.Zero;
		runner.Vitals.Teleported(body);
		runner.Avatar.statLife = runner.Avatar.statLifeMax2;
		runner.Motor.Stop();
		goal = target.ToWorldCoordinates();
		runner.Brain.Override = new BenchMoveTask(runner, goal, bench.Name.StartsWith("Climb"));
		caseStartedAt = Main.GameUpdateCount;
	}

	static readonly int[] BenchAccessories = { ItemID.AngelWings, ItemID.CloudinaBottle };

	static void EquipBenchGear(Bot runner, string name)
	{
		var armor = runner.Avatar.armor;
		for (int slot = Planning.Equipment.FirstAccessorySlot; slot <= Planning.Equipment.LastAccessorySlot; slot++)
		{
			if (BenchAccessories.Contains(armor[slot].type))
				armor[slot].TurnToAir();
		}
		int wanted = name.StartsWith("Wing") ? ItemID.AngelWings : name.StartsWith("Cloud") ? ItemID.CloudinaBottle : ItemID.None;
		if (wanted != ItemID.None)
			armor[Planning.Equipment.LastAccessorySlot].SetDefaults(wanted);
		runner.Avatar.UpdateEquips(runner.Avatar.whoAmI);
	}

	static void StripTools(Bot runner)
	{
		foreach (var item in runner.Avatar.inventory.Take(Inventory.MainSlotCount))
		{
			if (item.pick > 0 || item.createTile >= 0)
				item.TurnToAir();
		}
	}

	static void ClearClutter(Bot runner)
	{
		var keep = new HashSet<Item> { runner.Hands.BestPickaxe, runner.Hands.BestAxe, runner.Hands.BestWeapon(), runner.Hands.BestHammer };
		foreach (var item in runner.Avatar.inventory.Take(Inventory.MainSlotCount))
		{
			if (!item.IsAir && !keep.Contains(item) && !(item.potion && item.healLife > 0))
				item.TurnToAir();
		}
	}

	static void TopUp(Bot runner, int itemType, int amount)
	{
		int missing = amount - Inventory.Count(runner.Avatar, itemType);
		if (missing > 0)
			Inventory.Add(runner.Avatar, new Item(itemType, missing));
	}

	static void Build(int x, int y, char glyph)
	{
		var tile = Main.tile[x, y];
		tile.ClearEverything();
		switch (glyph)
		{
			case '#':
				WorldGen.PlaceTile(x, y, TileID.LihzahrdBrick, true, true);
				break;
			case 'd':
				WorldGen.PlaceTile(x, y, TileID.Dirt, true, true);
				break;
			case 's':
				WorldGen.PlaceTile(x, y, TileID.Stone, true, true);
				break;
			case 'g':
				WorldGen.PlaceTile(x, y, TileID.Dirt, true, true);
				tile.TileType = TileID.Grass;
				break;
			case 'l':
				WorldGen.PlaceTile(x, y, TileID.LeafBlock, true, true);
				break;
			case '=':
				WorldGen.PlaceTile(x, y, TileID.Platforms, true, true);
				break;
			case '!':
				WorldGen.PlaceTile(x, y, TileID.Rope, true, true);
				break;
			case '~':
				tile.LiquidType = LiquidID.Water;
				tile.LiquidAmount = byte.MaxValue;
				break;
			case '%':
				tile.LiquidType = LiquidID.Lava;
				tile.LiquidAmount = byte.MaxValue;
				break;
		}
	}

	static void PlaceLate(int x, int y, char glyph)
	{
		switch (glyph)
		{
			case 'T':
				if (!WorldGen.GrowTree(x, y + 1))
					BotDiagnostics.LogWorld($"nav bench: tree did not grow at {x},{y}");
				break;
			case 'c':
				int chest = WorldGen.PlaceChest(x, y);
				if (chest >= 0)
					Main.chest[chest].item[0] = new Item(ItemID.DirtBlock, 10);
				else
					BotDiagnostics.LogWorld($"nav bench: chest not placed at {x},{y}");
				break;
			case 'W':
				WorldGen.PlaceTile(x, y, TileID.LihzahrdBrick, true, true);
				WorldGen.SquareTileFrame(x, y, true);
				break;
		}
	}

	sealed class BenchMoveTask : BotTask
	{
		readonly Vector2 target;

		readonly bool climbsStraightUp;

		public BenchMoveTask(Bot bot, Vector2 target, bool climbsStraightUp) : base(bot)
		{
			this.target = target;
			this.climbsStraightUp = climbsStraightUp;
		}

		public override string NameKey => "Explore";

		public override float Evaluate() => 0f;

		public override void Tick()
		{
			Bot.Motor.MoveTo(target, 8f);
			Bot.Motor.ClimbingStraightUp = climbsStraightUp;
		}
	}

	sealed class HoldStillTask : BotTask
	{
		public HoldStillTask(Bot bot) : base(bot) { }

		public override string NameKey => "Idle";

		public override float Evaluate() => 0f;

		public override void Tick() => Bot.Motor.Stop();
	}
}
