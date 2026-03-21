using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.World;

public static class SharedAltar
{
	const int Width = 3;
	const int Height = 2;
	const int FrameSize = 18;

	public static Point? Origin { get; set; }

	public static void Claim(int x, int y)
	{
		if (Origin != null)
			return;
		var tile = Main.tile[x, y];
		Origin = new Point(x - tile.TileFrameX % (Width * FrameSize) / FrameSize, y - tile.TileFrameY % (Height * FrameSize) / FrameSize);
		Bots.BotDiagnostics.LogWorld($"first altar found at {Origin}, shared with every faction and spared");
	}

	public static bool Stands => Origin is Point origin && Main.tile[origin.X, origin.Y].HasTile && Main.tile[origin.X, origin.Y].TileType == TileID.DemonAltar;

	public static bool IsReserved(int x, int y) =>
		Origin is Point origin && x >= origin.X && x < origin.X + Width && y >= origin.Y && y < origin.Y + Height;

	public static IEnumerable<Point> Tiles()
	{
		if (Origin is not Point origin)
			yield break;
		for (int dx = 0; dx < Width; dx++)
		{
			for (int dy = 0; dy < Height; dy++)
				yield return new Point(origin.X + dx, origin.Y + dy);
		}
	}
}
