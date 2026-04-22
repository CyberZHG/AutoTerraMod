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

	public static bool IsDeep(Bot bot) => bot.CenterTile.Y > Main.rockLayer && bot.CenterTile.Y < Main.UnderworldLayer - 10;

	const int AfieldTiles = 100;

	const int HighwayReach = 12;
	const int HighwayAfieldTiles = 200;

	const float DeepAfieldDistance = 16f * 250f;

	public static bool IsAfield(Bot bot)
	{
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
				verdicts[bed] = valid = !InUnderworld(bed) && IsUsable(bed);
			return valid;
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			faction.Outposts.RemoveAll(bed => !Valid(bed));
			foreach (var stale in faction.OutpostFrames.Keys.Where(bed => !faction.Outposts.Contains(bed)).ToList())
				faction.OutpostFrames.Remove(stale);
			foreach (var bed in faction.Outposts)
				guarded.Add((bed, faction.OutpostFrames.TryGetValue(bed, out var frame) ? frame : null));
			if (faction.ArenaBed is Point arena && Valid(arena))
				guarded.Add((arena, null));
		}
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (bot.OutpostBed is not Point bed)
				continue;
			if (Valid(bed))
				guarded.Add((bed, null));
			else
			{
				BotDiagnostics.Log(bot, $"the outpost bed {bed} is no longer in a valid room, respawning at home again");
				bot.OutpostBed = null;
			}
		}
		return guarded;
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

	public static Point? ClaimableBed(Bot bot)
	{
		if (!IsAfield(bot))
			return null;
		var beds = FactionBeds(bot.Faction).Where(bed => bed != bot.OutpostBed).ToList();
		if (beds.Count == 0)
			return null;
		var nearest = beds.MinBy(bed => Vector2.DistanceSquared(bed.ToWorldCoordinates(), bot.Center));
		float toBed = Vector2.Distance(bot.Center, nearest.ToWorldCoordinates());
		float toSpawn = Vector2.Distance(bot.Center, BotWorldSystem.RespawnFeet(bot));
		return toBed < RebuildDistance && toBed + ClaimMargin < toSpawn ? nearest : null;
	}

	public static bool WantsNew(Bot bot)
	{
		if (!IsAfield(bot) || bot.CenterTile.Y >= Main.UnderworldLayer - 10 || FactionBeds(bot.Faction).Any(bed => Vector2.Distance(bed.ToWorldCoordinates(), bot.Center) < RebuildDistance))
			return false;
		var anchor = bot.OutpostBed is Point bed ? bed.ToWorldCoordinates() : BotWorldSystem.RespawnFeet(bot);
		return Vector2.Distance(bot.Center, anchor) > RebuildDistance;
	}

	const float ArenaOutpostDistance = 16f * 300f;
	const float ArenaSiteReach = 16f * 40f;
	const float ArenaBedReach = 16f * 80f;
	const float ArenaFightReach = 16f * 300f;

	public static bool BedStands(Point bed) =>
		WorldGen.InWorld(bed.X, bed.Y, 10) && Main.tile[bed.X, bed.Y].HasTile && Main.tile[bed.X, bed.Y].TileType == TileID.Beds;

	public static Point? ArenaBedFor(Factions.Faction faction)
	{
		if (faction.ArenaBed is not Point bed)
			return null;
		var at = bed.ToWorldCoordinates();
		bool serving = faction.Rally is { } rally ? Vector2.Distance(rally.point, at) < ArenaBedReach : Bosses.BossCatalog.NearestActiveBoss(at, ArenaFightReach) != null;
		return serving && BedStands(bed) ? bed : null;
	}

	public static bool NearArenaSite(Bot bot) =>
		bot.Faction.Rally is { } rally && Vector2.Distance(bot.Center, rally.point) < ArenaSiteReach;

	public static bool WantsArenaOutpost(Bot bot) =>
		NearArenaSite(bot) && bot.Faction.Rally is { } rally && Vector2.Distance(rally.point, bot.Faction.BaseCenter) > ArenaOutpostDistance
		&& ArenaBedFor(bot.Faction) == null && CarriesKit(bot.Avatar) && Count(bot.Avatar, IsBlock) >= BlocksNeeded;

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
			{
				yield return new Point(x, Floor);
				yield return new Point(x, Ceiling);
			}
			for (int y = Ceiling + 1; y < Floor; y++)
			{
				yield return new Point(Left, y);
				if (!IsDoorway(Right, y))
					yield return new Point(Right, y);
			}
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
					if (World.TileQuery.TypeAt(new Point(x, y)) == TileID.Beds)
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

		public bool Buildable()
		{
			if (!WorldGen.InWorld(Left, Ceiling, 20) || !WorldGen.InWorld(Right, Floor, 20))
				return false;
			foreach (var tile in Interior().Concat(Shell()))
			{
				var here = Main.tile[tile.X, tile.Y];
				if (here.LiquidAmount > 0 || HouseInspector.IsProtected(tile.X, tile.Y) || World.Highway.InAnyClearance(tile.X, tile.Y))
					return false;
				if (here.HasTile && (TileID.Sets.BasicChest[here.TileType] || !WorldGen.CanKillTile(tile.X, tile.Y)))
					return false;
			}
			return true;
		}
	}
}
