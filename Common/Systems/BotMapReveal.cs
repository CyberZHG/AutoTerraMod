using System.Collections.Generic;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class BotMapReveal : ModSystem
{
	const int Radius = 14;
	const int FullLightRadius = 8;
	const int EdgeLight = 110;

	readonly Dictionary<int, Point> lastRevealed = new();
	int cursor;

	public override void OnWorldUnload() => lastRevealed.Clear();

	public override void PostUpdateEverything()
	{
		if (Main.dedServ || !Main.mapEnabled || Main.Map == null)
			return;
		for (int step = 0; step < Main.maxNPCs; step++)
		{
			cursor = (cursor + 1) % Main.maxNPCs;
			var npc = Main.npc[cursor];
			if (!npc.active || npc.ModNPC is not BotNPC)
				continue;
			var center = npc.Center.ToTileCoordinates();
			if (lastRevealed.TryGetValue(npc.whoAmI, out var previous) && previous == center)
				continue;
			if (Reveal(center))
				lastRevealed[npc.whoAmI] = center;
			return;
		}
	}

	static bool Reveal(Point center)
	{
		const int radiusSq = Radius * Radius;
		const int fullSq = FullLightRadius * FullLightRadius;
		bool changed = false;
		for (int dx = -Radius; dx <= Radius; dx++)
		{
			for (int dy = -Radius; dy <= Radius; dy++)
			{
				int distanceSq = dx * dx + dy * dy;
				int x = center.X + dx, y = center.Y + dy;
				if (distanceSq > radiusSq || !WorldGen.InWorld(x, y, 1))
					continue;
				if (MapHelper.numUpdateTile >= MapHelper.maxUpdateTile)
				{
					Main.updateMap |= changed;
					return false;
				}
				int light = distanceSq <= fullSq ? 255 : 255 - (255 - EdgeLight) * (distanceSq - fullSq) / (radiusSq - fullSq);
				if (!Main.Map.UpdateLighting(x, y, (byte)light))
					continue;
				MapHelper.updateTileX[MapHelper.numUpdateTile] = (short)x;
				MapHelper.updateTileY[MapHelper.numUpdateTile] = (short)y;
				MapHelper.numUpdateTile++;
				changed = true;
			}
		}
		Main.updateMap |= changed;
		return true;
	}
}
