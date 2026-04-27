using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class OutpostTask : BotTask
{
	const float Priority = 75f;
	const float ArenaPriority = 110f;
	const float StandRadius = 12f;
	const int FailuresBeforeGivingUp = 40;
	const uint SiteRetryTicks = 600;
	const float AbandonDistance = 16f * 60f;
	const int SiteShiftX = 8;
	const int SiteShiftY = 4;

	static System.Collections.Generic.IEnumerable<(int, int)> SiteOffsets()
	{
		for (int distance = 0; distance <= SiteShiftX + SiteShiftY; distance++)
			for (int dy = -SiteShiftY; dy <= SiteShiftY; dy++)
				for (int dx = -SiteShiftX; dx <= SiteShiftX; dx++)
					if (System.Math.Abs(dx) + System.Math.Abs(dy) == distance)
						yield return (dx, dy);
	}

	static bool StandableGround(Point feet)
	{
		for (int y = feet.Y - 2; y <= feet.Y; y++)
		{
			if (TileQuery.IsSolid(feet.X, y) || TileQuery.IsSolid(feet.X + 1, y) || IsRope(new Point(feet.X, y)))
				return false;
		}
		return TileQuery.IsSolid(feet.X, feet.Y + 1) && TileQuery.IsSolid(feet.X + 1, feet.Y + 1);
	}

	Outpost.Site site;
	int failures;
	uint siteRejectedAt;

	public OutpostTask(Bot bot) : base(bot) { }

	bool SafeBlock(Item item) => Outpost.IsBlock(item) && Building.PlacementRules.Allows(Bot, item);

	public override string NameKey => "Outpost";

	public override float Evaluate()
	{
		float priority = Outpost.NearArenaSite(Bot) ? ArenaPriority : Priority;
		if (site != null)
			return priority;
		return Bot.Brain.Plan is BuildOutpostStep && Main.GameUpdateCount - siteRejectedAt > SiteRetryTicks ? priority : 0f;
	}

	public override void Tick()
	{
		if (site == null && !ChooseSite())
		{
			IsFinished = true;
			return;
		}
		if (Outpost.NearArenaSite(Bot) && Outpost.ArenaBedFor(Bot.Faction) is Point built && FindBed() != built)
		{
			BotDiagnostics.Log(Bot, $"another arena outpost is ready at {built}, leaving the one at {site.Left},{site.Floor}");
			ReleaseSite();
			IsFinished = true;
			return;
		}
		if (failures >= FailuresBeforeGivingUp || Vector2.Distance(Bot.Center, Navigation.PathFinder.BodyCenterAt(site.StandingNear(new Point(site.WallColumn, site.Floor)))) > AbandonDistance)
		{
			BotDiagnostics.Log(Bot, $"gave up the outpost at {site.Left},{site.Floor} (failures={failures}, at {Navigation.PathFinder.FeetOf(Bot.Npc)})");
			RejectSite();
			return;
		}
		if (NextJob() is not var (tile, tool, work))
		{
			Finish();
			return;
		}
		var feet = Navigation.PathFinder.FeetOf(Bot.Npc);
		bool fromBridge = bridgeFeet is int deckFeet && tile.Y >= site.Floor;
		if (fromBridge && !Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.MoveTo(new Vector2(tile.X * 16f + 8f, (bridgeFeet.Value + 1) * 16f - 21f), StandRadius);
			if (Bot.Motor.IsStuck)
				NoteStuck();
			return;
		}
		if (!fromBridge && (feet.Y >= site.Floor || feet.Y <= site.Ceiling || feet.X < site.Left || feet.X > site.Right))
		{
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(site.StandingNear(tile)), StandRadius);
			if (Bot.Motor.IsStuck || Bot.Motor.HasArrived)
				NoteStuck();
			return;
		}
		if (Bot.Npc.Hitbox.Intersects(new Rectangle(tile.X * 16, tile.Y * 16, 16, 16)))
		{
			int aside = tile.X <= site.WallColumn ? tile.X + 2 : tile.X - 2;
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(site.StandingNear(new Point(aside, tile.Y))), StandRadius / 2f);
			if (Bot.Motor.IsStuck)
				NoteStuck();
			return;
		}
		if (tool == null)
		{
			BotDiagnostics.Log(Bot, $"outpost is missing materials for {tile}: beds={Outpost.Count(Avatar, Outpost.IsBed)} doors={Outpost.Count(Avatar, Outpost.IsDoor)} walls={Outpost.Count(Avatar, Outpost.IsHouseWall)} blocks={Outpost.Count(Avatar, Outpost.IsBlock)} bedInRoom={FindAnyBed()}");
			RejectSite();
			return;
		}
		if (!Bot.Hands.CanReach(tool, tile))
		{
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(site.StandingNear(tile)), StandRadius);
			if (Bot.Motor.IsStuck)
				NoteStuck();
			return;
		}
		Bot.Motor.Stop();
		if (!Bot.Hands.Ready)
			return;
		if (work())
			Bot.Watchdog.RecordProgress();
		else if (++failures >= FailuresBeforeGivingUp)
			BotDiagnostics.Log(Bot, $"outpost job keeps failing at {tile}: tile={TileQuery.TypeAt(tile)} wall={Main.tile[tile.X, tile.Y].WallType} bot={Navigation.PathFinder.FeetOf(Bot.Npc)} door={Find(Outpost.IsDoor)?.Name} above={TileQuery.TypeAt(new Point(tile.X, tile.Y - 2))} below={TileQuery.TypeAt(new Point(tile.X, tile.Y + 2))} liquid={Main.tile[tile.X, tile.Y].LiquidAmount}");
	}

	const int StuckPenalty = 10;

	void NoteStuck()
	{
		failures += StuckPenalty;
		Bot.Motor.Stop();
	}

	const int BridgeSiteSlack = 4;

	bool ChooseSite()
	{
		var feet = Navigation.PathFinder.FeetOf(Bot.Npc);
		var bridge = Outpost.NearArenaSite(Bot) ? Bot.Faction.Hellbridge : null;
		bool aboveBridge = bridge != null && System.Math.Abs(feet.Y - bridge.Y) <= BridgeSiteSlack;
		bridgeFeet = aboveBridge ? bridge.Y : null;
		if (aboveBridge)
			feet = new Point(feet.X, bridge.Y - bridge.Headroom - 1);
		Outpost.Site found = null;
		foreach (var (dx, dy) in SiteOffsets())
		{
			var spot = new Point(feet.X + dx, feet.Y + (aboveBridge ? 0 : dy));
			if (!aboveBridge && !StandableGround(spot))
				continue;
			var candidate = new Outpost.Site(spot);
			if (!ActiveSites.Any(other => other.Overlaps(candidate)) && candidate.Buildable() && (aboveBridge || candidate.HasGround()) && !candidate.NearBed())
			{
				found = candidate;
				break;
			}
		}
		if (found == null)
		{
			BotDiagnostics.Log(Bot, $"no room for an outpost near {feet}");
			siteRejectedAt = Main.GameUpdateCount;
			return false;
		}
		site = found;
		ActiveSites.Add(site);
		failures = 0;
		BotDiagnostics.Log(Bot, $"building an outpost at {site.Left},{site.Floor}");
		return true;
	}

	static readonly System.Collections.Generic.List<Outpost.Site> ActiveSites = new();

	void ReleaseSite()
	{
		bridgeFeet = null;
		ActiveSites.Remove(site);
		site = null;
	}

	void RejectSite()
	{
		ReleaseSite();
		siteRejectedAt = Main.GameUpdateCount;
		IsFinished = true;
	}

	(Point, Item, System.Func<bool>)? NextJob()
	{
		foreach (var tile in site.FloorRow())
		{
			if (ShellJob(tile) is { } floorJob)
				return floorJob;
		}
		if (bridgeFeet is int bridgeRow && AccessJob(bridgeRow) is { } access)
			return access;
		foreach (var tile in site.Interior().Concat(site.Doorway()))
		{
			if (TileQuery.HasTile(tile) && !IsRope(tile) && !IsFurnishing(tile))
				return (tile, Bot.Hands.BestPickaxe, () => Clear(tile));
		}
		foreach (var tile in site.Shell())
		{
			if (ShellJob(tile) is { } shellJob)
				return shellJob;
		}
		foreach (var tile in site.WallSpots())
		{
			int wall = Main.tile[tile.X, tile.Y].WallType;
			if (wall == WallID.None)
				return (tile, Find(Outpost.IsHouseWall), () => Bot.Hands.PlaceWall(Find(Outpost.IsHouseWall), tile));
			if (!Main.wallHouse[wall])
				return (tile, Bot.Hands.BestPickaxe, () => KnockOutWall(tile));
		}
		foreach (var tile in site.Interior().Concat(site.Doorway()))
		{
			if (IsRope(tile))
				return (tile, Bot.Hands.BestPickaxe, () => StandingInside() && Clear(tile));
		}
		if (!site.Doorway().Any(IsDoor))
			return (site.DoorTile, Find(Outpost.IsDoor), () => Bot.Hands.Place(Find(Outpost.IsDoor), site.DoorTile));
		if (FindBed() == null)
		{
			var spot = new Point(site.Left + 2, site.Floor - 1);
			return (spot, Find(item => item.createTile == TileID.Beds), PlaceBed);
		}
		return null;
	}

	int? bridgeFeet;

	(Point, Item, System.Func<bool>)? AccessJob(int bridgeRow)
	{
		foreach (int x in new[] { site.Right + 1, site.Right + 2 })
		{
			var porch = new Point(x, site.Floor);
			if (!TileQuery.HasTile(porch))
				return (porch, Find(item => item.createTile >= 0 && TileID.Sets.Platforms[item.createTile]) ?? Find(SafeBlock), () => Bot.Hands.PlaceBridge(porch));
		}
		var ropeItem = Find(item => item.createTile >= 0 && Main.tileRope[item.createTile]);
		for (int y = site.Floor + 1; y <= bridgeRow; y++)
		{
			var cell = new Point(site.Right + 2, y);
			if (IsRope(cell))
				continue;
			if (TileQuery.HasTile(cell))
				return (cell, Bot.Hands.BestPickaxe, () => Clear(cell));
			return (cell, ropeItem, () => Bot.Hands.PlaceRope(cell));
		}
		return null;
	}

	(Point, Item, System.Func<bool>)? ShellJob(Point tile)
	{
		if (TileQuery.HasTile(tile) && !TileQuery.IsSolid(tile))
			return (tile, Bot.Hands.BestPickaxe, () => Clear(tile));
		if (!TileQuery.HasTile(tile))
			return (tile, Find(SafeBlock), () => Bot.Hands.Place(Find(SafeBlock), tile));
		return null;
	}

	static bool IsDoor(Point tile) => TileQuery.TypeAt(tile) is TileID.ClosedDoor or TileID.OpenDoor;

	static bool IsFurnishing(Point tile) => IsDoor(tile) || TileQuery.TypeAt(tile) == TileID.Beds || TileID.Sets.Torch[System.Math.Max(0, TileQuery.TypeAt(tile))];

	static bool IsRope(Point tile) => Navigation.PathFinder.IsRope(tile.X, tile.Y);

	bool StandingInside()
	{
		var feet = Navigation.PathFinder.FeetOf(Bot.Npc);
		return Bot.Npc.velocity.Y == 0f && feet.Y == site.Floor - 1 && feet.X > site.Left && feet.X < site.Right;
	}

	bool Clear(Point tile)
	{
		if (TileQuery.IsSolid(tile) || TileQuery.IsPlatform(tile.X, tile.Y))
			return Bot.Hands.Dig(tile) is ToolResult.Working or ToolResult.Done;
		WorldGen.KillTile(tile.X, tile.Y);
		return !TileQuery.HasTile(tile);
	}

	static bool KnockOutWall(Point tile)
	{
		WorldGen.KillWall(tile.X, tile.Y);
		return Main.tile[tile.X, tile.Y].WallType == WallID.None;
	}

	bool PlaceBed()
	{
		var bed = Find(item => item.createTile == TileID.Beds);
		for (int x = site.Left + 2; x <= site.Right - 3; x++)
		{
			var spot = new Point(x, site.Floor - 1);
			if (Bot.Hands.CanReach(bed, spot) && Bot.Hands.Place(bed, spot))
				return true;
		}
		return false;
	}

	static readonly int[] StoneMaterials = { ItemID.StoneBlock, ItemID.GrayBrick, ItemID.StoneWall, ItemID.GrayBrickWall };

	Item Find(System.Func<Item, bool> kind)
	{
		Item fallback = null;
		foreach (var item in Inventory.Items(Avatar))
		{
			if (!kind(item))
				continue;
			if (StoneMaterials.Contains(item.type))
				return item;
			fallback ??= item;
		}
		return fallback;
	}

	Point? FindAnyBed()
	{
		for (int x = site.Left - 2; x <= site.Right + 2; x++)
			for (int y = site.Ceiling - 2; y <= site.Floor + 2; y++)
				if (TileQuery.TypeAt(new Point(x, y)) == TileID.Beds)
					return new Point(x, y);
		return null;
	}

	Point? FindBed()
	{
		foreach (var tile in site.Interior())
		{
			if (Main.tile[tile.X, tile.Y].HasTile && Main.tile[tile.X, tile.Y].TileType == TileID.Beds && tile.Y == site.Floor - 1)
				return tile;
		}
		return null;
	}

	void Finish()
	{
		var bed = FindBed().Value;
		bool usable = Outpost.IsUsable(bed);
		if (Outpost.NearArenaSite(Bot))
			Bot.Faction.ArenaBed = bed;
		else if (usable)
		{
			Bot.OutpostBed = bed;
			if (!Bot.Faction.Outposts.Contains(bed))
				Bot.Faction.Outposts.Add(bed);
			Bot.Faction.OutpostFrames[bed] = new Point(site.Left, site.Floor);
		}
		Outpost.Invalidate();
		BotDiagnostics.Log(Bot, usable ? $"outpost ready, respawning at the bed {bed}" : $"outpost at {bed} is not a valid room: {WorldGen.roomCheckFailureReason}");
		ReleaseSite();
		IsFinished = true;
	}
}

public sealed class ClaimOutpostTask : BotTask
{
	const float BedsideReach = 16f * 3f;

	ClaimOutpostStep step;

	public ClaimOutpostTask(Bot bot) : base(bot) { }

	public override string NameKey => "Outpost";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not ClaimOutpostStep claim)
			return 0f;
		step = claim;
		return 75f;
	}

	public override void Tick()
	{
		var bed = step.Bed;
		if (!Outpost.BedStands(bed))
		{
			IsFinished = true;
			return;
		}
		var spot = new Vector2(bed.X * 16f + 8f, bed.Y * 16f - 13f);
		if (Vector2.Distance(Bot.Center, spot) > BedsideReach)
		{
			if (!TravelOrTunnel(spot, 16f))
				IsFinished = true;
			return;
		}
		Bot.OutpostBed = bed;
		BotDiagnostics.Log(Bot, $"respawning at the faction outpost bed {bed}");
		Bot.Watchdog.RecordProgress();
		IsFinished = true;
	}
}
