using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public static class Outpost
{
	public const int Width = 11;
	public const int Height = 6;
	public const int BlocksNeeded = 30;
	public const int WallsNeeded = (Width - 2) * (Height - 2);
	const float RebuildDistance = 16f * 150f;

	public static bool IsBlock(Item item)
	{
		int tile = item.createTile;
		return tile >= 0 && Main.tileSolid[tile] && !Main.tileSolidTop[tile] && !Main.tileFrameImportant[tile]
			&& !TileID.Sets.Falling[tile] && !TileID.Sets.Ore[tile] && !PlacementRules.IsEvil(tile);
	}

	public static bool IsHouseWall(Item item) => item.createWall > WallID.None && Main.wallHouse[item.createWall];

	public static bool IsDoor(Item item) => item.createTile == TileID.ClosedDoor;

	public static bool IsBed(Item item) => item.createTile == TileID.Beds;

	public static int Count(Player avatar, System.Func<Item, bool> kind) => Inventory.Items(avatar).Where(kind).Sum(item => item.stack);

	public static int StoneBlocks(Player avatar) => Inventory.Count(avatar, ItemID.StoneBlock) + Inventory.Count(avatar, ItemID.GrayBrick);

	public static bool CarriesKit(Player avatar) => Count(avatar, IsBed) > 0 && Count(avatar, IsDoor) > 0 && Count(avatar, IsHouseWall) >= WallsNeeded;

	public static bool InUnderworld(Point bed) => bed.Y >= Main.UnderworldLayer - 10;

	static bool InBridgeClearance(int x, int y)
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (faction.Hellbridge is { } bridge && x >= bridge.LeftEnd && x <= bridge.RightEnd && y > bridge.FeetRow(x) - bridge.Headroom && y <= bridge.FeetRow(x) + 1)
				return true;
		}
		return false;
	}

	public static bool IsDeep(Bot bot) => bot.CenterTile.Y > Main.rockLayer && bot.CenterTile.Y < Main.UnderworldLayer - 10;

	const int AfieldTiles = 100;

	const int HighwayReach = 12;
	const int HighwayAfieldTiles = 200;

	const float DeepAfieldDistance = 16f * 250f;

	const int ShaftVicinity = 15;

	public static bool IsAfield(Bot bot)
	{
		if (bot.Faction.Hellevator is { } shaft && System.Math.Abs(bot.CenterTile.X - shaft.RopeX) <= ShaftVicinity && bot.CenterTile.Y > shaft.TopY)
			return false;
		int away = System.Math.Abs(bot.CenterTile.X - bot.Faction.BaseTile.X);
		return IsDeep(bot) && Vector2.Distance(bot.Center, bot.Faction.BaseCenter) > DeepAfieldDistance || away > AfieldTiles && (away > HighwayAfieldTiles || !OnHighway(bot));
	}

	static bool OnHighway(Bot bot) =>
		World.Highway.For(bot.Faction).Any(highway => highway.Spans(bot.CenterTile.X) && System.Math.Abs(bot.CenterTile.Y - highway.Y) < HighwayReach);

	const uint ValidationTicks = 600;
	static uint validatedAt = uint.MaxValue;
	static readonly List<(Point bed, Point? frame)> guarded = new();

	public static void Invalidate() => validatedAt = uint.MaxValue;

	public static IReadOnlyList<(Point bed, Point? frame)> Guarded()
	{
		if (validatedAt != uint.MaxValue && Main.GameUpdateCount - validatedAt < ValidationTicks)
			return guarded;
		validatedAt = Main.GameUpdateCount;
		guarded.Clear();
		var verdicts = new Dictionary<Point, bool>();
		bool Valid(Point bed)
		{
			if (!verdicts.TryGetValue(bed, out bool valid))
				verdicts[bed] = valid = (!InUnderworld(bed) || BotWorldSystem.Factions.Any(faction => faction.ArenaBed == bed)) && IsUsable(bed);
			return valid;
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			faction.Outposts.RemoveAll(bed => !Valid(bed) || Trapping(bed));
			foreach (var stale in faction.OutpostFrames.Keys.Where(bed => !faction.Outposts.Contains(bed) && faction.ArenaBed != bed).ToList())
				faction.OutpostFrames.Remove(stale);
			foreach (var bed in faction.Outposts)
				guarded.Add((bed, faction.OutpostFrames.TryGetValue(bed, out var frame) ? frame : null));
			if (faction.ArenaBed is Point arena && BedStands(arena))
				guarded.Add((arena, faction.OutpostFrames.TryGetValue(arena, out var arenaFrame) ? arenaFrame : null));
		}
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (bot.OutpostBed is not Point bed)
				continue;
			if (Valid(bed) && !Trapping(bed))
				guarded.Add((bed, null));
			else
			{
				string reason = Trapping(bed) ? "its doorway is blocked" : $"{WorldGen.roomCheckFailureReason} at {FirstHole(bed)?.ToString() ?? "?"}";
				BotDiagnostics.Log(bot, $"the outpost bed {bed} is no longer in a valid room ({reason}), respawning at home again");
				bot.OutpostBed = null;
			}
		}
		return guarded;
	}

	const int HoleSearchReach = 30;

	static Point? FirstHole(Point bed)
	{
		var seen = new HashSet<Point> { new(bed.X, bed.Y - 1) };
		var frontier = new Queue<Point>(seen);
		while (frontier.Count > 0)
		{
			var cell = frontier.Dequeue();
			if (Main.tile[cell.X, cell.Y].WallType == WallID.None)
				return cell;
			foreach (var next in new[] { new Point(cell.X + 1, cell.Y), new Point(cell.X - 1, cell.Y), new Point(cell.X, cell.Y + 1), new Point(cell.X, cell.Y - 1) })
			{
				if (System.Math.Abs(next.X - bed.X) > HoleSearchReach || System.Math.Abs(next.Y - bed.Y) > HoleSearchReach || World.TileQuery.IsSolid(next) || seen.Contains(next))
					continue;
				var tile = Main.tile[next.X, next.Y];
				if (tile.HasTile && tile.TileType is TileID.ClosedDoor or TileID.OpenDoor)
					continue;
				seen.Add(next);
				frontier.Enqueue(next);
			}
		}
		return null;
	}

	static bool Trapping(Point bed)
	{
		for (int x = bed.X + Width; x > bed.X; x--)
		{
			for (int y = bed.Y - 3; y <= bed.Y; y++)
			{
				if (!WorldGen.InWorld(x, y, 2) || !Main.tile[x, y].HasTile || Main.tile[x, y].TileType is not (TileID.ClosedDoor or TileID.OpenDoor))
					continue;
				int doorTop = y;
				while (doorTop > y - 3 && Main.tile[x, doorTop - 1].HasTile && Main.tile[x, doorTop - 1].TileType is TileID.ClosedDoor or TileID.OpenDoor)
					doorTop--;
				for (int row = doorTop; row < doorTop + 3; row++)
				{
					if (World.TileQuery.IsSolid(x + 1, row) && !WorldGen.CanKillTile(x + 1, row))
						return true;
				}
				return false;
			}
		}
		return false;
	}

	public static bool ExitBlocked(int right, int floor)
	{
		int x = right + 1;
		for (int y = floor - 3; y < floor; y++)
		{
			if (WorldGen.InWorld(x, y, 2) && World.TileQuery.IsSolid(x, y) && !WorldGen.CanKillTile(x, y))
				return true;
		}
		return false;
	}

	public static bool InFrame(Point frame, int x, int y)
	{
		int left = frame.X, right = frame.X + Width - 1, floor = frame.Y, ceiling = frame.Y - Height + 1;
		return x >= left && x <= right && y >= ceiling && y <= floor && (x == left || x == right || y == floor || y == ceiling);
	}

	public static List<Point> FactionBeds(Factions.Faction faction)
	{
		Guarded();
		foreach (var member in BotWorldSystem.Bots)
		{
			if (member.FactionId == faction.Id && member.OutpostBed is Point bed && BedStands(bed) && !InUnderworld(bed) && !faction.Outposts.Contains(bed))
				faction.Outposts.Add(bed);
		}
		return faction.Outposts;
	}

	const float ClaimMargin = 16f * 40f;

	const float TempleBedReach = 16f * 200f;

	public static Point? TempleBed(Bot bot)
	{
		bool templeBound = bot.Brain.Plan is Planning.ScoutStep { Landmark: World.Landmark.Temple }
			|| bot.Faction.Rally is { } rally && Navigation.PathFinder.InsideMaze(rally.point.ToTileCoordinates());
		if (!templeBound || BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(World.Landmark.Temple) is not Point temple)
			return null;
		var near = FactionBeds(bot.Faction).Where(bed => BedStands(bed) && Vector2.Distance(bed.ToWorldCoordinates(), temple.ToWorldCoordinates()) < TempleBedReach).ToList();
		return near.Count == 0 ? null : near.MinBy(bed => Vector2.DistanceSquared(bed.ToWorldCoordinates(), temple.ToWorldCoordinates()));
	}

	static readonly Dictionary<(int bot, Point bed), uint> failedClaims = new();
	const uint FailedClaimTicks = 10 * 3600;

	public static void ClaimFailed(Bot bot, Point bed)
	{
		if (failedClaims.Count > 256)
			failedClaims.Clear();
		failedClaims[(bot.Id, bed)] = Main.GameUpdateCount + FailedClaimTicks;
	}

	public static Point? ClaimableBed(Bot bot)
	{
		if (!IsAfield(bot) || HeadingHome(bot) || bot.OutpostBed is Point held && held == ArenaBedFor(bot.Faction))
			return null;
		var beds = FactionBeds(bot.Faction).Where(bed => bed != bot.OutpostBed && !NearCamp(bot.Faction, bed) && !Bosses.BossRituals.InPillarZone(bed.ToWorldCoordinates())
			&& !(failedClaims.TryGetValue((bot.Id, bed), out uint until) && Main.GameUpdateCount < until)).ToList();
		if (beds.Count == 0)
			return null;
		var nearest = beds.MinBy(bed => Vector2.DistanceSquared(bed.ToWorldCoordinates(), bot.Center));
		float toBed = Vector2.Distance(bot.Center, nearest.ToWorldCoordinates());
		float toSpawn = Vector2.Distance(bot.Center, BotWorldSystem.RespawnFeet(bot));
		return toBed < RebuildDistance && toBed + ClaimMargin < toSpawn ? nearest : null;
	}

	const float CampOutpostClearance = 16f * 300f;

	static bool HeadingHome(Bot bot) => bot.Motor.Destination is Vector2 destination && Vector2.Distance(destination, bot.Faction.BaseCenter) < CampOutpostClearance;

	static bool NearCamp(Factions.Faction faction, Point bed) => Vector2.Distance(bed.ToWorldCoordinates(), faction.BaseCenter) < CampOutpostClearance;

	public static bool WantsNew(Bot bot)
	{
		if (!IsAfield(bot) || bot.CenterTile.Y >= Main.UnderworldLayer - 10 || Bosses.BossRituals.InPillarZone(bot.Center) || FactionBeds(bot.Faction).Any(bed => !NearCamp(bot.Faction, bed) && Vector2.Distance(bed.ToWorldCoordinates(), bot.Center) < RebuildDistance))
			return false;
		if (Vector2.Distance(bot.Center, bot.Faction.BaseCenter) < CampOutpostClearance)
			return false;
		var anchor = bot.OutpostBed is Point bed ? bed.ToWorldCoordinates() : BotWorldSystem.RespawnFeet(bot);
		return Vector2.Distance(bot.Center, anchor) > RebuildDistance;
	}

	const float ArenaOutpostDistance = 16f * 300f;
	const float ArenaSiteReach = 16f * 80f;
	const float ArenaBedReach = 16f * 160f;
	const float ArenaFightReach = 16f * 300f;

	public static bool BedStands(Point bed) =>
		WorldGen.InWorld(bed.X, bed.Y, 10) && Main.tile[bed.X, bed.Y].HasTile && Main.tile[bed.X, bed.Y].TileType == TileID.Beds;

	public static Point? ArenaBedFor(Factions.Faction faction)
	{
		if (faction.ArenaBed is Point bed && Serves(faction, bed))
			return bed;
		foreach (var other in BotWorldSystem.Factions)
		{
			if (other != faction && other.ArenaBed is Point shared && Serves(faction, shared))
			{
				faction.ArenaBed = shared;
				return shared;
			}
		}
		return null;
	}

	public const float BedsideReach = 16f * 3f;

	public static Vector2 Bedside(Point bed) => new(bed.X * 16f + 8f, (bed.Y + 1) * 16f - 21f);

	public static bool TakeArenaBed(Bot bot)
	{
		if (ArenaBedFor(bot.Faction) is not Point arena || bot.OutpostBed == arena || Vector2.Distance(bot.Center, Bedside(arena)) > BedsideReach)
			return false;
		bot.OutpostBed = arena;
		BotDiagnostics.Log(bot, $"set the respawn point at the arena outpost bed {arena}");
		return true;
	}

	static bool Serves(Factions.Faction faction, Point bed)
	{
		var at = bed.ToWorldCoordinates();
		bool serving = faction.Rally is { } rally ? Vector2.Distance(rally.point, at) < ArenaBedReach : Bosses.BossCatalog.NearestActiveBoss(at, ArenaFightReach) != null;
		return serving && UsableCached(bed);
	}

	static readonly Dictionary<Point, (uint at, bool usable)> usableCache = new();

	static bool UsableCached(Point bed)
	{
		if (usableCache.TryGetValue(bed, out var known) && Main.GameUpdateCount - known.at < ValidationTicks)
			return known.usable;
		bool usable = IsUsable(bed);
		usableCache[bed] = (Main.GameUpdateCount, usable);
		return usable;
	}

	public static bool IsDistantArena(Factions.Faction faction, Vector2 point) =>
		Vector2.Distance(point, faction.BaseCenter) > ArenaOutpostDistance && !Navigation.PathFinder.InsideMaze(point.ToTileCoordinates());

	public static bool NearArenaSite(Bot bot) =>
		bot.Faction.Rally is { } rally && Vector2.Distance(bot.Center, rally.point) < ArenaSiteReach;

	public static bool WantsArenaOutpost(Bot bot) =>
		NearArenaSite(bot) && bot.Faction.Rally is { } rally && IsDistantArena(bot.Faction, rally.point) && !RacingTheNight(bot.Faction)
		&& ArenaBedFor(bot.Faction) == null && !Bots.Tasks.OutpostTask.ArenaTakenByOther(bot) && !Bots.Tasks.OutpostTask.ArenaRecentlyRejected(bot) && CarriesKit(bot.Avatar) && Count(bot.Avatar, IsBlock) >= BlocksNeeded
		&& (!OnUnderworldBridge(bot.Faction, rally.point) || Count(bot.Avatar, item => item.createTile >= 0 && Main.tileRope[item.createTile]) >= BridgeAccessRopes);

	static bool RacingTheNight(Factions.Faction faction) =>
		!Main.dayTime && Bosses.BossCatalog.ChallengeOf(faction.RallyBossType) is { NightOnly: true };

	const int BridgeAccessRopes = 8;

	static bool OnUnderworldBridge(Factions.Faction faction, Vector2 point) =>
		faction.Hellbridge is { } bridge && System.Math.Abs(point.ToTileCoordinates().Y - bridge.Y) <= 4;

	public static bool ExpectsDistantArena(Bot bot)
	{
		if (bot.Faction.Rally != null || Bosses.BossCredit.NextChallengeFor(bot.Faction) is not { DistantArena: { } arena } challenge)
			return false;
		return BotWorldSystem.KnowledgeOf(bot.Faction).LandmarkOf(arena) is Point site
			&& Vector2.Distance(site.ToWorldCoordinates(), bot.Faction.BaseCenter) > ArenaOutpostDistance
			&& Bosses.BossReadiness.Evaluate(bot.Faction, challenge, bot).IsReady;
	}

	public static bool IsUsable(Point bed) =>
		WorldGen.InWorld(bed.X, bed.Y, 10) && Main.tile[bed.X, bed.Y].HasTile && Main.tile[bed.X, bed.Y].TileType == TileID.Beds
		&& WorldGen.StartRoomCheck(bed.X, bed.Y);

	public sealed class Site
	{
		public Site(Point feet)
		{
			Left = feet.X - Width / 2;
			Floor = feet.Y + 1;
		}

		public int Left { get; }
		public int Right => Left + Width - 1;
		public int Floor { get; }
		public int Ceiling => Floor - Height + 1;
		public int WallColumn => Left + Width / 2;
		public Point DoorTile => new(Right, Floor - 2);
		public Point StandingNear(Point tile) => new(System.Math.Clamp(tile.X, Left + 1, Right - 2), Floor - 1);

		public bool IsDoorway(int x, int y) => x == Right && y >= Floor - 3 && y < Floor;

		public IEnumerable<Point> Interior()
		{
			for (int y = Ceiling + 1; y < Floor; y++)
				for (int x = Left + 1; x < Right; x++)
					yield return new Point(x, y);
		}

		public IEnumerable<Point> Shell()
		{
			for (int x = Left; x <= Right; x++)
				yield return new Point(x, Floor);
			for (int y = Floor - 1; y > Ceiling; y--)
				yield return new Point(Left, y);
			for (int x = Left; x <= Right; x++)
				yield return new Point(x, Ceiling);
			for (int y = Ceiling + 1; y < Floor; y++)
				if (!IsDoorway(Right, y))
					yield return new Point(Right, y);
		}

		public IEnumerable<Point> FloorRow()
		{
			for (int x = Left; x <= Right; x++)
				yield return new Point(x, Floor);
		}

		public IEnumerable<Point> Doorway()
		{
			for (int y = Floor - 3; y < Floor; y++)
				yield return new Point(Right, y);
		}

		public IEnumerable<Point> WallSpots() => Interior();

		const int Spacing = 3;

		public bool Overlaps(Site other) =>
			Left - Spacing <= other.Right && other.Left - Spacing <= Right && Ceiling - Spacing <= other.Floor && other.Ceiling - Spacing <= Floor;

		public bool NearBed()
		{
			for (int x = Left - Width; x <= Right + Width; x++)
				for (int y = Ceiling - Height; y <= Floor + Height; y++)
					if (World.TileQuery.TypeAt(new Point(x, y)) == TileID.Beds && UsableCached(new Point(x, y)))
						return true;
			return false;
		}

		public bool HasGround()
		{
			int solid = 0;
			for (int x = Left; x <= Right; x++)
			{
				if (World.TileQuery.IsSolid(x, Floor))
					solid++;
			}
			return solid * 2 >= Width;
		}

		public bool Buildable(bool wetShell = false)
		{
			if (!WorldGen.InWorld(Left, Ceiling, 20) || !WorldGen.InWorld(Right, Floor, 20))
				return false;
			if (ExitBlocked(Right, Floor))
				return false;
			var interior = Interior().ToHashSet();
			foreach (var tile in interior.Concat(Shell()))
			{
				var here = Main.tile[tile.X, tile.Y];
				if (here.LiquidAmount > 0 && (!wetShell || interior.Contains(tile)))
					return false;
				if (World.TileQuery.SupportsShrine(tile.X, tile.Y) || Navigation.PathFinder.InsideMaze(tile) || HouseInspector.IsProtected(tile.X, tile.Y) || World.Highway.InAnyClearance(tile.X, tile.Y) || InBridgeClearance(tile.X, tile.Y))
					return false;
				if (here.HasTile && (TileID.Sets.BasicChest[here.TileType] || !WorldGen.CanKillTile(tile.X, tile.Y)))
					return false;
			}
			return true;
		}
	}
}
