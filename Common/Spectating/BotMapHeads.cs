using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Spectating;

public sealed class BotMapHeads : ModMapLayer
{
	const float HoverPixels = 10f;

	public override void Draw(ref MapOverlayDrawContext context, ref string text)
	{
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (bot.Npc is not NPC body || !body.active || bot.Avatar == null)
				continue;
			var tile = new Vector2(body.Center.X / 16f, body.position.Y / 16f);
			var screen = (tile - context.MapPosition) * context.MapScale + context.MapOffset;
			if (context.ClippingRectangle is Rectangle clip && !clip.Contains(screen.ToPoint()))
				continue;
			Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, bot.Avatar, screen, 1f, context.DrawScale, bot.Faction.Color);
			if (Vector2.Distance(Main.MouseScreen, screen) < HoverPixels * context.DrawScale)
				text = $"{bot.Name} ({bot.Faction.Name})";
		}
	}
}
