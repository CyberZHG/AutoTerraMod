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
		new(0, 1), new(-1, 1), new(1, 1), new(0, -1), new(0, 0), new(-1, -1), new(1, -1), new(-1, 0), new(1, 0), new(0, -2)
	};

	public static void LightUpIfDark(Bot bot)
	{
		var center = bot.CenterTile;
		int keep = TorchesToKeep(bot, center);
		if (keep < 0 || HasLightNearby(center))
			return;
		var torch = Inventory.Items(bot.Avatar).FirstOrDefault(item => item.createTile >= 0 && TileID.Sets.Torch[item.createTile]);
		if (torch == null || Inventory.Count(bot.Avatar, torch.type) <= keep)
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

	const int BelowGroundTiles = 3;
	const int BaseLightMargin = 40;
	const int CaveTorchesKept = 0;
	const int AmbientTorchesKept = 10;

	static int TorchesToKeep(Bot bot, Point center)
	{
		if (center.Y >= Main.worldSurface)
			return CaveTorchesKept;
		if (center.Y > World.TileQuery.SurfaceY(center.X) + BelowGroundTiles)
			return AmbientTorchesKept;
		var settlement = bot.Faction.Settlement;
		bool nearBaseAtNight = !Main.dayTime && settlement != null && center.X >= settlement.Left - BaseLightMargin && center.X <= settlement.Right + BaseLightMargin;
		return nearBaseAtNight ? AmbientTorchesKept : -1;
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
