using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class GardenTask : BotTask
{
	enum Job
	{
		Harvest,
		Plant,
		PlaceBox
	}

	const float Priority = 56f;
	const float NearBase = 16f * 120f;
	const uint RetryTicks = 1800;
	const int TakeAmount = 10;

	Job job;
	Point? target;
	uint nothingToDoAt;

	public GardenTask(Bot bot) : base(bot) { }

	public override string NameKey => "Garden";

	Item Held(int type) => Inventory.Items(Avatar).FirstOrDefault(item => item.type == type);

	public override float Evaluate()
	{
		if (Bot.Faction.Settlement == null || Main.GameUpdateCount - nothingToDoAt < RetryTicks || Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) > NearBase)
			return 0f;
		var boxes = HerbGarden.Boxes(Bot.Faction);
		if (Nearest(boxes.Where(HerbGarden.IsRipe)) is Point ripe)
			return Choose(Job.Harvest, ripe);
		if (Nearest(boxes.Where(HerbGarden.IsEmpty)) is Point empty && (Held(HerbGarden.Seed) != null || Take(HerbGarden.Seed)))
			return Choose(Job.Plant, empty);
		if (boxes.Count < HerbGarden.Size && HerbGarden.HasSeeds(Bot) && (Held(HerbGarden.Box) != null || Take(HerbGarden.Box)) && HerbGarden.FindBoxSpot(Bot) is Point spot)
			return Choose(Job.PlaceBox, spot);
		nothingToDoAt = Main.GameUpdateCount;
		return 0f;
	}

	Point? Nearest(System.Collections.Generic.IEnumerable<Point> boxes) =>
		boxes.Cast<Point?>().MinBy(box => World.WorldScan.DistanceSq(Bot.CenterTile, box.Value));

	float Choose(Job chosen, Point tile)
	{
		job = chosen;
		target = tile;
		return Priority;
	}

	bool Take(int type)
	{
		var chest = Building.BaseStorage.ChestWith(Bot.Faction, type, Bot.Center);
		if (chest == null || !Bot.Hands.CanReach(null, Building.BaseStorage.TileOf(chest)))
			return false;
		return Building.BaseStorage.Withdraw(Avatar, chest, type, TakeAmount) > 0;
	}

	bool ClearPlatform(Point tile)
	{
		var current = Main.tile[tile.X, tile.Y];
		if (current.HasTile && (job == Job.PlaceBox ? current.TileType == TileID.Platforms || HerbGarden.OwnHighwayDeck(tile) : HerbGarden.IsWeed(current.TileType)))
			WorldGen.KillTile(tile.X, tile.Y);
		return true;
	}

	bool Sow(Item seed, Point tile)
	{
		if (!WorldGen.PlaceAlch(tile.X, tile.Y, HerbGarden.HerbStyle))
			return false;
		WorldGen.SquareTileFrame(tile.X, tile.Y);
		Inventory.ConsumeOne(Avatar, seed);
		Bot.Watchdog.RecordProgress();
		return true;
	}

	public override void Tick()
	{
		if (target is not Point box)
		{
			IsFinished = true;
			return;
		}
		var tile = job == Job.PlaceBox ? box : new Point(box.X, box.Y - 1);
		var item = job switch
		{
			Job.Plant => Held(HerbGarden.Seed),
			Job.PlaceBox => Held(HerbGarden.Box),
			_ => null
		};
		bool stillWanted = job switch
		{
			Job.Harvest => HerbGarden.IsRipe(box),
			Job.Plant => item != null && HerbGarden.IsEmpty(box),
			_ => item != null && HerbGarden.IsFreeSpot(tile)
		};
		if (!stillWanted)
		{
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(item, tile))
		{
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(new Point(box.X, box.Y - 1)), 24f);
			if (Bot.Motor.IsStuck)
			{
				BotDiagnostics.Log(Bot, $"garden {job} at {tile}: cannot get within reach");
				nothingToDoAt = Main.GameUpdateCount;
				IsFinished = true;
			}
			return;
		}
		Bot.Motor.Stop();
		if (!Bot.Hands.Ready)
			return;
		if (job == Job.Harvest)
		{
			WorldGen.KillTile(tile.X, tile.Y);
			Accomplished = true;
			BotDiagnostics.Log(Bot, $"harvested a garden herb at {tile}");
		}
		else if (ClearPlatform(tile) && (job == Job.Plant ? Sow(item, tile) : Bot.Hands.Place(item, tile)))
		{
			Accomplished = true;
			HerbGarden.Forget(Bot.Faction);
			BotDiagnostics.Log(Bot, job == Job.Plant ? $"planted a herb seed at {tile}" : $"placed a planter box at {tile}");
		}
		else
		{
			BotDiagnostics.Log(Bot, $"garden {job} at {tile} failed: tile {Main.tile[tile.X, tile.Y].TileType} active={Main.tile[tile.X, tile.Y].HasTile}, below {Main.tile[tile.X, tile.Y + 1].TileType}");
			nothingToDoAt = Main.GameUpdateCount;
		}
		IsFinished = true;
	}
}
