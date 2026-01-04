using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public static class TorchPlacer
{
	const int SearchHalfWidth = 9;
	const int SearchHalfHeight = 6;

	static readonly Point[] CandidateOffsets =
	{
		new(0, -1), new(0, 0), new(-1, -1), new(1, -1), new(-1, 0), new(1, 0), new(0, -2)
	};

	public static void LightUpIfDark(Bot bot)
	{
		var center = bot.CenterTile;
		if (center.Y < Main.worldSurface || HasLightNearby(center))
			return;
		var torch = Inventory.Items(bot.Avatar).FirstOrDefault(item => item.createTile >= 0 && TileID.Sets.Torch[item.createTile]);
		if (torch == null)
			return;
		foreach (var offset in CandidateOffsets)
		{
			var spot = new Point(center.X + offset.X, center.Y + offset.Y);
			if (!Main.tile[spot.X, spot.Y].HasTile && bot.Hands.Place(torch, spot))
			{
				BotDiagnostics.Log(bot, $"placed torch at {spot}");
				return;
			}
		}
	}

	static bool HasLightNearby(Point center)
	{
		for (int x = center.X - SearchHalfWidth; x <= center.X + SearchHalfWidth; x++)
		{
			for (int y = center.Y - SearchHalfHeight; y <= center.Y + SearchHalfHeight; y++)
			{
				if (!WorldGen.InWorld(x, y, 2))
					continue;
				var tile = Main.tile[x, y];
				if (tile.HasTile && Main.tileLighted[tile.TileType])
					return true;
			}
		}
		return false;
	}
}
