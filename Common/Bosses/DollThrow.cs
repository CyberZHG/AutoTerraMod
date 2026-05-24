using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class DollThrow : ModSystem
{
	const float ItemGravity = 0.1f;
	const float FlightTicks = 40f;
	const uint LandingGraceTicks = 120;

	static readonly List<(int item, Vector2 target, uint deadline)> inFlight = new();

	public override void OnWorldUnload() => inFlight.Clear();

	public static void Throw(int itemType, Vector2 from, Vector2 target, int grabDelay)
	{
		var velocity = new Vector2((target.X - from.X) / FlightTicks, (target.Y - from.Y) / FlightTicks - 0.5f * ItemGravity * FlightTicks);
		int thrown = Item.NewItem(new EntitySource_Misc("AutoTerraMod:VoodooThrow"), from, itemType);
		var item = Main.item[thrown];
		item.velocity = velocity;
		item.noGrabDelay = grabDelay;
		SoundEngine.PlaySound(SoundID.Item1, from);
		inFlight.Add((thrown, target, Main.GameUpdateCount + LandingGraceTicks));
	}

	public override void PostUpdateItems()
	{
		for (int i = inFlight.Count - 1; i >= 0; i--)
		{
			var (index, target, deadline) = inFlight[i];
			var item = Main.item[index];
			if (!item.active || item.lavaWet)
			{
				inFlight.RemoveAt(i);
				continue;
			}
			if (Main.GameUpdateCount < deadline)
				continue;
			item.Center = target;
			item.velocity = Vector2.Zero;
			inFlight.RemoveAt(i);
		}
	}
}
