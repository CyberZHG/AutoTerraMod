using System.Linq;
using AutoTerraMod.Common.Building;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class DeliverTask : BotTask
{
	const float HandOverDistance = 16f * 3f;

	DeliverStep step;

	public DeliverTask(Bot bot) : base(bot) { }

	public override string NameKey => "Deliver";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not DeliverStep deliver)
			return 0f;
		step = deliver;
		Detail = $"{Lang.GetItemNameValue(deliver.ItemType)} x{Inventory.Count(Avatar, deliver.ItemType) - deliver.Keep}";
		return 60f;
	}

	public override void Tick()
	{
		if (step == null || Inventory.Count(Avatar, step.ItemType) <= step.Keep)
		{
			IsFinished = true;
			return;
		}
		var chest = step.HandOver ? null : BaseStorage.ChestWithRoom(Bot.Faction, Bot.Center);
		if (chest != null)
		{
			DeliverToChest(chest);
			return;
		}
		var builder = BotWorldSystem.FindBot(step.RecipientId);
		if (builder == Bot || builder?.Npc is not NPC recipient)
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, recipient.Center) > HandOverDistance)
		{
			Bot.Motor.MoveTo(recipient.Center, HandOverDistance * 0.5f);
			if (Bot.Motor.IsStalled)
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		var sample = Inventory.Items(Avatar).First(item => item.type == step.ItemType);
		if (Inventory.HasRoomFor(builder.Avatar, sample))
			DropFor(builder, recipient.Center);
		else
			BotDiagnostics.Log(Bot, $"{builder.Name} has no room for {sample.Name}, keeping it");
		IsFinished = true;
	}

	void DeliverToChest(Chest chest)
	{
		var tile = BaseStorage.TileOf(chest);
		if (!Bot.Hands.CanReach(null, tile))
		{
			Bot.Motor.MoveTo(tile.ToWorldCoordinates(16f, 16f), 24f);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
			return;
		}
		int stored = 0;
		int keep = step.Keep;
		foreach (var item in Inventory.Items(Avatar).Where(item => item.type == step.ItemType).ToList())
		{
			var portion = SparePortion(item, ref keep);
			if (portion == null)
				continue;
			var leftover = BaseStorage.Deposit(chest, portion);
			int moved = portion.stack - (leftover.IsAir ? 0 : leftover.stack);
			stored += moved;
			Take(item, moved);
		}
		SoundEngine.PlaySound(SoundID.MenuClose, tile.ToWorldCoordinates());
		Bot.Watchdog.RecordProgress();
		BotDiagnostics.Log(Bot, $"stored {Lang.GetItemNameValue(step.ItemType)} x{stored} at base");
		IsFinished = true;
	}

	static Item SparePortion(Item item, ref int keep)
	{
		int spare = item.stack - keep;
		keep = System.Math.Max(0, keep - item.stack);
		if (spare <= 0)
			return null;
		var portion = item.Clone();
		portion.stack = spare;
		return portion;
	}

	static void Take(Item item, int amount)
	{
		item.stack -= amount;
		if (item.stack <= 0)
			item.TurnToAir();
	}

	void DropFor(Bot builder, Vector2 where)
	{
		int delivered = 0;
		int keep = step.Keep;
		foreach (var item in Inventory.Items(Avatar).Where(item => item.type == step.ItemType).ToList())
		{
			var portion = SparePortion(item, ref keep);
			if (portion == null)
				continue;
			int index = Item.NewItem(new EntitySource_Misc("AutoTerraMod:Delivery"), where, portion);
			if (index < Main.maxItems)
			{
				Main.item[index].noGrabDelay = 0;
				ItemReservations.Reserve(index, builder);
			}
			delivered += portion.stack;
			Take(item, portion.stack);
		}
		Bot.Watchdog.RecordProgress();
		BotDiagnostics.Log(Bot, $"handed {Lang.GetItemNameValue(step.ItemType)} x{delivered} to {builder.Name}");
	}
}
