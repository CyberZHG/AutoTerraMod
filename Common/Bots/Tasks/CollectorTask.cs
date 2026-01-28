using System.Linq;
using System.Reflection;
using AutoTerraMod.Common.Planning;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class CollectorTask : BotTask
{
	const float PlaceReach = 16f * 3f;
	const int DressingSlots = 3;

	static readonly FieldInfo DollItems = typeof(TEDisplayDoll).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

	(Item head, Item body, Item legs)? outfit;
	Point? stand;

	public CollectorTask(Bot bot) : base(bot) { }

	public override string NameKey => "Display";

	public static bool CollectsArmor(Bot bot) => bot.Personality.Aspiration == Aspiration.Collector;

	public override float Evaluate()
	{
		if (!CollectsArmor(Bot) || !DecorateTask.Leisure(Bot))
			return 0f;
		outfit = SpareSet();
		if (outfit == null)
			return 0f;
		stand = EmptyMannequin();
		if (stand == null && Inventory.Count(Avatar, ItemID.Mannequin) == 0)
			return 0f;
		Detail = outfit.Value.body.Name;
		return 65f;
	}

	(Item, Item, Item)? SpareSet()
	{
		var items = Inventory.Items(Avatar).ToList();
		foreach (var body in items.Where(item => item.bodySlot >= 0 && !item.vanity))
			foreach (var head in items.Where(item => item.headSlot >= 0 && !item.vanity))
				foreach (var legs in items.Where(item => item.legSlot >= 0 && !item.vanity))
				{
					if (FormsSet(head, body, legs))
						return (head, body, legs);
				}
		return null;
	}

	static bool FormsSet(Item head, Item body, Item legs)
	{
		var dummy = new Player();
		dummy.armor[0] = head.Clone();
		dummy.armor[1] = body.Clone();
		dummy.armor[2] = legs.Clone();
		dummy.head = head.headSlot;
		dummy.body = body.bodySlot;
		dummy.legs = legs.legSlot;
		dummy.UpdateArmorSets(Main.maxPlayers - 1);
		return !string.IsNullOrEmpty(dummy.setBonus);
	}

	Point? EmptyMannequin()
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		foreach (var (id, entity) in TileEntity.ByID)
		{
			if (entity is TEDisplayDoll doll && settlement.Contains(doll.Position.X, doll.Position.Y) && ((Item[])DollItems.GetValue(doll)).Take(DressingSlots).All(item => item.IsAir))
				return new Point(doll.Position.X, doll.Position.Y);
		}
		return null;
	}

	public override void Tick()
	{
		if (outfit is not var (head, body, legs) || head.IsAir || body.IsAir || legs.IsAir)
		{
			IsFinished = true;
			return;
		}
		if (stand == null)
		{
			PlaceMannequin();
			return;
		}
		var at = stand.Value.ToWorldCoordinates();
		if (Vector2.Distance(Bot.Center, at) > PlaceReach)
		{
			if (!TravelOrTunnel(at, PlaceReach * 0.5f))
				IsFinished = true;
			return;
		}
		int id = TEDisplayDoll.Find(stand.Value.X, stand.Value.Y);
		if (id < 0 || TileEntity.ByID[id] is not TEDisplayDoll doll)
		{
			IsFinished = true;
			return;
		}
		var slots = (Item[])DollItems.GetValue(doll);
		slots[0] = head.Clone();
		slots[1] = body.Clone();
		slots[2] = legs.Clone();
		BotDiagnostics.Log(Bot, $"dressed a mannequin in {head.Name}, {body.Name}, {legs.Name}");
		head.TurnToAir();
		body.TurnToAir();
		legs.TurnToAir();
		IsFinished = true;
	}

	void PlaceMannequin()
	{
		var mannequin = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.Mannequin);
		if (mannequin == null)
		{
			IsFinished = true;
			return;
		}
		var spot = Stations.PlacementSpots(Bot.Faction, mannequin).Cast<Point?>().FirstOrDefault(candidate => Bot.Hands.CanPlaceAt(mannequin, candidate.Value));
		if (spot is not Point target)
		{
			BotDiagnostics.Log(Bot, "no room at base for a mannequin");
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(mannequin, target))
		{
			if (!TravelOrTunnel(target.ToWorldCoordinates(), PlaceReach * 0.5f))
			{
				BotDiagnostics.Log(Bot, $"cannot reach mannequin spot {target}");
				IsFinished = true;
			}
			return;
		}
		if (!Bot.Hands.Place(mannequin, target))
			return;
		var topLeft = Terraria.ObjectData.TileObjectData.TopLeft(target.X, target.Y);
		if (TEDisplayDoll.Find(topLeft.X, topLeft.Y) < 0)
			TEDisplayDoll.Place(topLeft.X, topLeft.Y);
		BotDiagnostics.Log(Bot, $"placed a mannequin at {topLeft}");
		stand = EmptyMannequin();
	}
}
