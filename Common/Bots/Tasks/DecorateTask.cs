using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class DecorateTask : BotTask
{
	const float PlaceReach = 16f * 3f;
	const int DoorSwing = 1;

	Item decor;
	Point? spot;
	bool hanging;
	int failedPlacements;
	const int PlacementAttempts = 30;
	const uint FailedSpotTicks = 3600;
	static readonly System.Collections.Generic.Dictionary<Point, uint> failedSpots = new();

	static bool Usable(Point tile) => !failedSpots.TryGetValue(tile, out uint at) || Main.GameUpdateCount - at >= FailedSpotTicks;

	public override void Start()
	{
		base.Start();
		failedPlacements = 0;
	}

	public DecorateTask(Bot bot) : base(bot) { }

	public override string NameKey => "Decorate";

	const float NearBase = 16f * 30f;

	public static bool Leisure(Bot bot) =>
		bot.Brain.Plan is Planning.IdleStep || !Main.dayTime || Vector2.Distance(bot.Center, bot.Faction.BaseCenter) < NearBase;

	public static bool IsWantedDecor(Bot bot, Item item) =>
		((bot.Personality.Aspiration == Aspiration.MonsterHunter && item.createTile == TileID.Banners)
		|| (NaturalistTask.StudiesCritters(bot) && NaturalistTask.IsCage(item)))
		&& !Displayed(bot.Faction).Contains(item.type);

	public static void DisplaysChanged() => displayed.Clear();

	public static bool IsDisplayTile(int type) => type == TileID.Banners || TileID.Sets.CritterCageLidStyle[type] != -1;
	static readonly System.Collections.Generic.Dictionary<int, (uint at, System.Collections.Generic.HashSet<int> items, System.Collections.Generic.List<Point> duplicates)> displayed = new();

	public static System.Collections.Generic.HashSet<int> Displayed(Factions.Faction faction) => Survey(faction).items;

	static (uint at, System.Collections.Generic.HashSet<int> items, System.Collections.Generic.List<Point> duplicates) Survey(Factions.Faction faction)
	{
		if (displayed.TryGetValue(faction.Id, out var cached))
			return cached;
		var items = new System.Collections.Generic.HashSet<int>();
		var duplicates = new System.Collections.Generic.List<Point>();
		var bannerSeen = new System.Collections.Generic.HashSet<int>();
		void Scan(int left, int right, int top, int bottom)
		{
			for (int x = System.Math.Max(left, 1); x <= System.Math.Min(right, Main.maxTilesX - 2); x++)
			{
				for (int y = System.Math.Max(top, 1); y <= System.Math.Min(bottom, Main.maxTilesY - 2); y++)
				{
					var tile = Main.tile[x, y];
					if (!tile.HasTile || tile.TileType != TileID.Banners && TileID.Sets.CritterCageLidStyle[tile.TileType] == -1)
						continue;
					int item = PlacedItem(tile);
					if (item <= 0)
						continue;
					items.Add(item);
					if (tile.TileType == TileID.Banners && tile.TileFrameY % BannerFrameHeight == 0 && !bannerSeen.Add(item))
						duplicates.Add(new Point(x, y));
				}
			}
		}
		if (faction.Settlement is { } camp)
			Scan(camp.Left, camp.Right, camp.Top, camp.Floor);
		foreach (var highway in World.Highway.For(faction))
			Scan(System.Math.Min(highway.StartX, highway.EndX), System.Math.Max(highway.StartX, highway.EndX), highway.Y - highway.Headroom, highway.Y + 1);
		return displayed[faction.Id] = (Main.GameUpdateCount, items, duplicates);
	}

	const float DuplicatePriority = 55f;
	const float DuplicateReach = 16f * 80f;
	const int BannerFrameHeight = 54;
	const int BannerStylesPerRow = 111;
	static System.Collections.Generic.Dictionary<(int tile, int style), int> placedItems;

	static int PlacedItem(Tile tile)
	{
		placedItems ??= ContentSamples.ItemsByType.Values.Where(item => item.createTile == TileID.Banners || item.createTile >= 0 && TileID.Sets.CritterCageLidStyle[item.createTile] != -1)
			.GroupBy(item => (item.createTile, item.placeStyle)).ToDictionary(group => group.Key, group => group.First().type);
		int style = tile.TileType == TileID.Banners
			? tile.TileFrameX / 18 + tile.TileFrameY / BannerFrameHeight * BannerStylesPerRow
			: Terraria.ObjectData.TileObjectData.GetTileStyle(tile);
		return placedItems.TryGetValue((tile.TileType, style), out int item) ? item : 0;
	}
	Point? duplicate;

	bool DiscardDuplicates()
	{
		foreach (var item in Inventory.Items(Avatar).ToList())
		{
			if ((item.createTile == TileID.Banners || NaturalistTask.IsCage(item)) && Displayed(Bot.Faction).Contains(item.type))
			{
				BotDiagnostics.Log(Bot, $"threw away a duplicate {item.Name}");
				item.TurnToAir();
			}
		}
		duplicate = Survey(Bot.Faction).duplicates.Where(tile => TileQuery.TypeAt(tile) == TileID.Banners)
			.OrderBy(tile => Vector2.DistanceSquared(tile.ToWorldCoordinates(), Bot.Center)).Cast<Point?>().FirstOrDefault();
		return duplicate != null;
	}

	public override float Evaluate()
	{
		if (DiscardDuplicates() && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < DuplicateReach)
		{
			Detail = "duplicate banner";
			return DuplicatePriority;
		}
		duplicate = null;
		decor = Inventory.Items(Avatar).FirstOrDefault(item => IsWantedDecor(Bot, item));
		if (decor == null || !Leisure(Bot))
			return 0f;
		var floor = decor.createTile == TileID.Banners ? null : FloorSpot(decor);
		hanging = floor == null;
		spot = floor ?? CeilingSpot(decor);
		if (spot == null)
			return 0f;
		Detail = decor.Name;
		return Aspire(65f);
	}

	Point? CeilingSpot(Item item)
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		foreach (var structure in settlement.Structures.Where(structure => structure.Purpose != Building.StructurePurpose.Storage))
		{
			for (int x = structure.Left + DoorSwing + 1; x < structure.Right - DoorSwing; x++)
			{
				var candidate = new Point(x, structure.Ceiling + 1);
				if (World.TileQuery.IsSolid(x, structure.Ceiling) && Usable(candidate) && Bot.Hands.CanPlaceAt(item, candidate))
					return candidate;
			}
		}
		return null;
	}

	Point? FloorSpot(Item item) =>
		Planning.Stations.PlacementSpots(Bot.Faction, item).Cast<Point?>().FirstOrDefault(candidate => Usable(candidate.Value) && Bot.Hands.CanPlaceAt(item, candidate.Value));

	public override void Tick()
	{
		if (duplicate is Point extra)
		{
			if (TileQuery.TypeAt(extra) != TileID.Banners)
			{
				duplicate = null;
				DisplaysChanged();
				IsFinished = true;
				return;
			}
			var reachSpot = new Vector2(extra.X * 16f + 8f, (extra.Y + 3) * 16f);
			if (Vector2.Distance(Bot.Center, reachSpot) > PlaceReach)
			{
				if (!TravelOrTunnel(reachSpot, PlaceReach * 0.5f))
					IsFinished = true;
				return;
			}
			Bot.Motor.Stop();
			WorldGen.KillTile(extra.X, extra.Y, noItem: true);
			BotDiagnostics.Log(Bot, $"took down a duplicate banner at {extra}");
			return;
		}
		if (spot is not Point target || decor == null || decor.IsAir)
		{
			IsFinished = true;
			return;
		}
		var below = hanging ? new Vector2(target.X * 16f + 8f, (target.Y + 3) * 16f) : target.ToWorldCoordinates();
		if (Vector2.Distance(Bot.Center, below) > PlaceReach)
		{
			if (!TravelOrTunnel(below, PlaceReach * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		string name = decor.Name;
		if (Bot.Hands.Place(decor, target))
		{
			BotDiagnostics.Log(Bot, $"put up {name} at {target}");
			DisplaysChanged();
			IsFinished = true;
		}
		else if (Bot.Hands.Ready && ++failedPlacements >= PlacementAttempts)
		{
			if (failedSpots.Count > 256)
				failedSpots.Clear();
			failedSpots[target] = Main.GameUpdateCount;
			BotDiagnostics.Log(Bot, $"cannot put up {name} at {target}");
			IsFinished = true;
		}
	}
}
