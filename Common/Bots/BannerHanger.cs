using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class BannerHanger
{
	const int ReachX = 4;
	const int ReachUp = 6;

	public static void HangWhereFound(Bot bot)
	{
		if (bot.Personality.Aspiration == Aspiration.MonsterHunter || !bot.Hands.Ready)
			return;
		var banner = Inventory.Items(bot.Avatar).FirstOrDefault(item => item.createTile == TileID.Banners);
		if (banner == null)
			return;
		string name = banner.Name;
		var here = bot.CenterTile;
		for (int dy = 0; dy <= ReachUp; dy++)
		{
			for (int dx = 0; dx <= ReachX * 2; dx++)
			{
				var spot = new Point(here.X + (dx % 2 == 0 ? dx / 2 : -(dx + 1) / 2), here.Y - dy);
				if (bot.Hands.InOtherBase(spot) || !bot.Hands.CanPlaceAt(banner, spot) || !bot.Hands.Place(banner, spot))
					continue;
				BotDiagnostics.Log(bot, $"hung {name} at {spot}");
				return;
			}
		}
	}
}
