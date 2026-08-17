using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Factions;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public sealed record Highway(int FactionId, int Direction, int StartX, int EndX, int Y, int? LinkX, int? LinkY = null) : ICorridor
{
	public const int Clearance = 5;

	public int Headroom => Clearance;
	const int PlatformEvery = 20;
	const int OceanMargin = 20;
	const int EvilScanRows = 8;

	static readonly Dictionary<int, Highway[]> planned = new();

	public static void Forget() => planned.Clear();

	public static Highway[] For(Faction faction)
	{
		if (planned.TryGetValue(faction.Id, out var known))
			return known;
		if (faction.Settlement is not { } camp || Systems.BotWorldSystem.Factions.Any(other => other.Settlement == null))
			return System.Array.Empty<Highway>();
		var highways = new[] { Plan(faction, camp, -1), Plan(faction, camp, 1) }.Where(highway => highway != null).ToArray();
		planned[faction.Id] = highways;
		return highways;
	}

	public static bool ProtectsAnyDeck(int x, int y)
	{
		foreach (var highways in planned.Values)
			foreach (var highway in highways)
				if (highway.ProtectsDeck(x, y))
					return true;
		return false;
	}

	public static bool ProtectsOthersDeck(int factionId, int x, int y)
	{
		foreach (var (owner, highways) in planned)
			if (owner != factionId)
				foreach (var highway in highways)
					if (highway.ProtectsDeck(x, y) || highway.LinkX == x && highway.LinkRows is var (top, bottom) && y >= top && y <= bottom + 1)
						return true;
		return false;
	}

	public static bool InAnyClearance(int x, int y)
	{
		foreach (var highways in planned.Values)
			foreach (var highway in highways)
				if (highway.InClearance(x, y))
					return true;
		return false;
	}

	static Highway Plan(Faction faction, Building.Settlement camp, int direction)
	{
		int start = direction > 0 ? camp.Right + Building.Settlement.ApronWidth + 1 : camp.Left - Building.Settlement.ApronWidth - 1;
		var neighbour = Systems.BotWorldSystem.Factions
			.Where(other => other != faction && System.Math.Sign(other.BaseTile.X - faction.BaseTile.X) == direction)
			.MinBy(other => System.Math.Abs(other.BaseTile.X - faction.BaseTile.X));
		int end;
		int? link = null;
		int? linkY = null;
		if (neighbour?.Settlement is { } theirs)
		{
			int theirStart = direction > 0 ? theirs.Left - Building.Settlement.ApronWidth - 1 : theirs.Right + Building.Settlement.ApronWidth + 1;
			link = (Math.Min(start, theirStart) + Math.Max(start, theirStart)) / 2;
			linkY = theirs.Floor - 1;
			end = link.Value - direction;
		}
		else
		{
			end = direction > 0 ? Main.maxTilesX - TileQuery.OceanWidth - OceanMargin : TileQuery.OceanWidth + OceanMargin;
		}
		if ((end - start) * direction <= 0)
			return null;
		return new Highway(faction.Id, direction, start, end, camp.Floor - 1, link, linkY);
	}

	public int End(int direction) => EndX;

	public bool Spans(int x) => Direction > 0 ? x >= StartX && x <= EndX : x <= StartX && x >= EndX;

	public bool IsDeck(int x) => IsFlat(x) && !ICorridor.Scorches(x, Y + 1) && (RequiresPlatform(x) ? TileQuery.IsPlatform(x, Y + 1) : TileQuery.IsPlatform(x, Y + 1) || TileQuery.IsSolid(x, Y + 1));

	bool IsFlat(int x) => !Main.tile[x, Y + 1].IsHalfBlock && Main.tile[x, Y + 1].Slope == SlopeType.Solid;

	public bool HeadroomClear(int x)
	{
		for (int y = Y - Headroom + 1; y <= Y; y++)
		{
			if (TileQuery.IsSolid(x, y) && !tolerated.Contains((x, y)) || IsClutter(x, y))
				return false;
		}
		return true;
	}

	public static bool IsClutter(int x, int y)
	{
		var tile = Main.tile[x, y];
		return tile.HasTile && (TileQuery.IsPlatform(x, y) || Main.tileRope[tile.TileType] && !Bots.Tasks.TidyCampTask.KeptRope(x, y)) && WorldGen.CanKillTile(x, y);
	}

	public int UsableEnd => IsComplete ? EndX : Frontier(Direction) - Direction;

	public static IEnumerable<Highway> Toward(int fromX, int toX)
	{
		int direction = System.Math.Sign(toX - fromX);
		int low = System.Math.Min(fromX, toX), high = System.Math.Max(fromX, toX);
		return Systems.BotWorldSystem.Factions.SelectMany(For)
			.Where(highway => highway.Direction == direction && System.Math.Max(highway.StartX, highway.EndX) >= low && System.Math.Min(highway.StartX, highway.EndX) <= high)
			.OrderBy(highway => System.Math.Abs(highway.StartX - fromX));
	}

	public (int low, int high)? BuiltSpan
	{
		get
		{
			int end = UsableEnd;
			if ((end - StartX) * Direction < 0)
				return null;
			return (System.Math.Min(StartX, end), System.Math.Max(StartX, end));
		}
	}

	public static IEnumerable<Highway> All => Systems.BotWorldSystem.Factions.SelectMany(For);

	public bool IsBuilt(int x) => Skipped(x) || (IsDeck(x) || ICorridor.HoldsChest(x, Y + 1, Y + 1)) && HeadroomClear(x);

	readonly System.Collections.Generic.Dictionary<int, uint> skipped = new();
	const uint SkipRetryTicks = 10 * 3600;

	bool Skipped(int x) => skipped.TryGetValue(x, out uint at) && Terraria.Main.GameUpdateCount - at < SkipRetryTicks;
	readonly HashSet<(int, int)> tolerated = new();

	public bool IsTolerated(int x, int y) => tolerated.Contains((x, y));

	public void Tolerate(int x, int y)
	{
		tolerated.Add((x, y));
		frontierAt = 0;
	}

	public void Skip(int x)
	{
		skipped[x] = Terraria.Main.GameUpdateCount;
		frontierAt = 0;
	}

	public bool WantsPlatform(int x) => x != StartX && (x - StartX) % PlatformEvery == 0 || !TileQuery.IsSolid(x, Y + 2);

	public bool RequiresPlatform(int x) => WantsPlatform(x);

	uint frontierAt;
	int frontierCache;
	const uint FrontierRecheckTicks = 300;

	public int Frontier(int direction)
	{
		bool fresh = Main.GameUpdateCount - frontierAt < FrontierRecheckTicks;
		int from = fresh ? frontierCache : StartX;
		if (!fresh)
			frontierAt = Main.GameUpdateCount;
		for (int x = from; x != EndX; x += Direction)
		{
			if (!IsBuilt(x))
				return frontierCache = x;
		}
		return frontierCache = EndX;
	}

	public bool IsComplete => Frontier(Direction) == EndX && IsBuilt(EndX);

	int? cursor;

	public int WorkFrontier()
	{
		int frontier = Frontier(Direction);
		if (cursor is not int at || (frontier - at) * Direction >= 0)
			return (cursor = frontier).Value;
		for (int x = at; x != EndX; x += Direction)
		{
			if (!IsBuilt(x))
				return (cursor = x).Value;
		}
		return (cursor = frontier).Value;
	}

	public string BlockReason { get; private set; }

	public bool BlockedAt(int x, int pickPower)
	{
		BlockReason = null;
		for (int y = Y - Headroom + 1; y <= Y + 1; y++)
		{
			var tile = Main.tile[x, y];
			if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava)
			{
				BlockReason = $"lava at {x},{y}";
				return true;
			}
			if (!TileQuery.IsSolid(x, y) || y == Y + 1 && !RequiresPlatform(x))
				continue;
			if (Navigation.PickRequirement.MinimumPick(tile.TileType, x, y) > pickPower || Building.HouseInspector.IsProtected(x, y) && !ProtectsDeck(x, y))
			{
				BlockReason = $"tile {tile.TileType} at {x},{y} (pick {pickPower})";
				return true;
			}
		}
		for (int y = Y + 1; y <= Y + EvilScanRows; y++)
		{
			if (Main.tile[x, y].HasTile && Building.PlacementRules.IsEvil(Main.tile[x, y].TileType) && Navigation.PickRequirement.MinimumPick(Main.tile[x, y].TileType, x, y) > pickPower)
			{
				BlockReason = $"evil ground at {x},{y}";
				return true;
			}
		}
		return false;
	}

	public bool ProtectsDeck(int x, int y) => y == Y + 1 && Spans(x) && IsDeck(x);

	public bool InClearance(int x, int y) => y > Y - Headroom && y <= Y && Spans(x) || LinkX == x && LinkRows is var (top, bottom) && y >= top && y <= bottom;

	const int JumpableRise = 4;

	public (int top, int bottom)? LinkRows
	{
		get
		{
			if (LinkX is not int x)
				return null;
			int ground = LinkY ?? TileQuery.SurfaceY(x) - 1;
			if (System.Math.Abs(ground - Y) <= (LinkY == null ? 1 : JumpableRise))
				return null;
			return LinkY == null ? (System.Math.Min(ground, Y), System.Math.Max(ground, Y)) : (System.Math.Min(ground, Y) + 1, System.Math.Max(ground, Y));
		}
	}

	public int? MissingLinkRow()
	{
		if (LinkX is not int x || LinkRows is not var (top, bottom))
			return null;
		for (int y = top; y <= bottom; y++)
		{
			if (!Main.tile[x, y].HasTile || !Main.tileRope[Main.tile[x, y].TileType])
				return y;
		}
		return null;
	}
}
