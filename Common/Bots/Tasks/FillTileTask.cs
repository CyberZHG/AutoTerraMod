using System.Linq;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class FillTileTask : BotTask
{
	const float FillPriority = 90f;
	const int MaxAttempts = 600;

	int attempts;

	public FillTileTask(Bot bot) : base(bot) { }

	public override string NameKey => "Build";

	public static bool IsFiller(Item item) =>
		item.createTile >= 0 && Main.tileSolid[item.createTile] && !Main.tileSolidTop[item.createTile] && !Main.tileFrameImportant[item.createTile] && !Main.tileSand[item.createTile] && item.createTile != Terraria.ID.TileID.HellstoneBrick && !Building.PlacementRules.IsEvil(item.createTile);

	public override float Evaluate() => Bot.Brain.Plan is FillTileStep ? FillPriority : 0f;

	public override void Start()
	{
		base.Start();
		attempts = 0;
	}

	public override void Tick()
	{
		if (Bot.Brain.Plan is not FillTileStep plan || ++attempts > MaxAttempts)
		{
			IsFinished = true;
			return;
		}
		var tile = plan.Tile;
		if (TileQuery.IsSolid(tile.X, tile.Y))
		{
			IsFinished = true;
			return;
		}
		var block = Inventory.Items(Avatar).Where(IsFiller).OrderByDescending(item => item.type is Terraria.ID.ItemID.StoneBlock or Terraria.ID.ItemID.DirtBlock).FirstOrDefault();
		if (block == null)
		{
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(block, tile))
		{
			if (!TravelOrTunnel(tile.ToWorldCoordinates(), 32f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		if (Bot.Hands.Place(block, tile))
		{
			BotDiagnostics.Log(Bot, $"filled {tile} with {block.Name}");
			Bot.Watchdog.RecordProgress();
		}
	}
}
