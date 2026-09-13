using AutoTerraMod.Common.Planning;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class BuyTask : BotTask
{
	const float ShoppingRadius = 16f * 5f;

	BuyStep step;

	public BuyTask(Bot bot) : base(bot) { }

	public override string NameKey => "Buy";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not BuyStep buy)
			return 0f;
		step = buy;
		Detail = $"{Lang.GetItemNameValue(buy.ItemType)} x{buy.Amount}";
		return 60f;
	}

	public override void Tick()
	{
		int index = NPC.FindFirstNPC(step.SellerType);
		if (index < 0 || Shops.Offer(Bot, step.ItemType) is not var (seller, price))
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, seller.Center) > ShoppingRadius)
		{
			if (!TravelOrTunnel(seller.Center, ShoppingRadius * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		int bought = 0;
		while (bought < step.Amount && Avatar.BuyItem(price))
		{
			var item = new Item();
			item.SetDefaults(step.ItemType);
			Inventory.Add(Avatar, item);
			bought++;
		}
		if (bought > 0)
			Systems.BotEmotes.Casual(Bot, Terraria.GameContent.UI.EmoteID.ItemGoldpile);
		if (bought > 0)
			BotDiagnostics.Log(Bot, $"bought {Lang.GetItemNameValue(step.ItemType)} x{bought} from {seller.GivenOrTypeName} for {price * bought} copper");
		Bot.Watchdog.RecordProgress();
		IsFinished = true;
	}
}
