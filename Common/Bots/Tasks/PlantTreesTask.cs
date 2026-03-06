using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class PlantTreesTask : BotTask
{
	const float Priority = 55f;
	const float CommittedPriority = 70f;
	bool committed;
	const float NearBase = 16f * 60f;
	const uint RetryTicks = 1800;

	Point? spot;
	uint nothingToPlantAt;

	public PlantTreesTask(Bot bot) : base(bot) { }

	public override string NameKey => "PlantTrees";

	Item Acorn => Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.Acorn);

	public override void Start()
	{
		base.Start();
		committed = true;
	}

	public override void Stop()
	{
		committed = false;
		base.Stop();
	}

	public override float Evaluate()
	{
		if (committed && spot != null && Acorn != null)
			return CommittedPriority;
		var settlement = Bot.Faction.Settlement;
		if (settlement == null || Main.GameUpdateCount - nothingToPlantAt < RetryTicks
			|| Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) > NearBase || !TreePlanter.WantsSaplings(settlement))
			return 0f;
		if (Acorn == null && !TakeStoredAcorns())
			return 0f;
		var acorn = Acorn;
		spot = TreePlanter.FindSpot(Bot, settlement, acorn);
		if (spot == null)
		{
			nothingToPlantAt = Main.GameUpdateCount;
			return 0f;
		}
		return Priority;
	}

	const int AcornsToTake = 5;

	bool TakeStoredAcorns()
	{
		var chest = Building.BaseStorage.ChestWith(Bot.Faction, ItemID.Acorn, Bot.Center);
		if (chest == null || !Bot.Hands.CanReach(null, Building.BaseStorage.TileOf(chest)))
			return false;
		return Building.BaseStorage.Withdraw(Avatar, chest, ItemID.Acorn, AcornsToTake) > 0;
	}

	public override void Tick()
	{
		var acorn = Acorn;
		if (spot is not Point tile || acorn == null || !Bot.Hands.CanPlaceAt(acorn, tile))
		{
			Done();
			return;
		}
		if (!Bot.Hands.CanReach(acorn, tile))
		{
			Bot.Motor.MoveTo(Navigation.PathFinder.BodyCenterAt(tile), 24f);
			if (Bot.Motor.IsStuck)
			{
				nothingToPlantAt = Main.GameUpdateCount;
				Done();
			}
			return;
		}
		Bot.Motor.Stop();
		if (!Bot.Hands.Ready)
			return;
		if (Bot.Hands.Place(acorn, tile))
		{
			Accomplished = true;
			BotDiagnostics.Log(Bot, $"planted an acorn at {tile}");
		}
		else
		{
			nothingToPlantAt = Main.GameUpdateCount;
		}
		Done();
	}

	void Done()
	{
		committed = false;
		spot = null;
		IsFinished = true;
	}
}
