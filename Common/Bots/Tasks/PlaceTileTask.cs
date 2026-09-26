using System.Linq;
using AutoTerraMod.Common.Planning;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class PlaceTileTask : BotTask
{
	const float PlacePriority = 90f;
	const int MaxAttempts = 1200;

	int attempts;

	public PlaceTileTask(Bot bot) : base(bot) { }

	public override string NameKey => "Build";

	public override float Evaluate() => Bot.Brain.Plan is PlaceTileStep ? PlacePriority : 0f;

	public override void Start()
	{
		base.Start();
		attempts = 0;
	}

	public override void Tick()
	{
		if (Bot.Brain.Plan is not PlaceTileStep plan || ++attempts > MaxAttempts)
		{
			IsFinished = true;
			return;
		}
		var item = Inventory.Items(Avatar).FirstOrDefault(held => held.type == plan.ItemType);
		if (item == null || Main.tile[plan.Tile.X, plan.Tile.Y].HasTile && Main.tile[plan.Tile.X, plan.Tile.Y].TileType == item.createTile)
		{
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(item, plan.Tile))
		{
			if (!TravelOrTunnel(plan.Tile.ToWorldCoordinates(), 32f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		string name = item.Name;
		if (Bot.Hands.Place(item, plan.Tile))
		{
			BotDiagnostics.Log(Bot, $"placed {name} at {plan.Tile}");
			IsFinished = true;
		}
	}
}
