using System;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public static class MoonLordFoothold
{
	const int SideOffset = 14;
	const int DeckReach = 70;
	const int DeckInset = 3;

	public static bool IsPart(NPC npc) => npc.type is NPCID.MoonLordCore or NPCID.MoonLordHand or NPCID.MoonLordHead or NPCID.MoonLordFreeEye;

	public static Vector2 For(Bot bot, NPC part)
	{
		var core = Core() ?? part;
		int coreX = (int)(core.Center.X / 16f);
		int side = bot.Center.X >= core.Center.X ? 1 : -1;
		int x = Utils.Clamp(coreX + side * SideOffset, 50, Main.maxTilesX - 50);
		if (NearestDeck(coreX, x) is Point deck)
			return deck.ToWorldCoordinates(8f, 0f);
		var ground = new Point(x, TileQuery.SurfaceY(x) - 1);
		return (Grounds.StandingSpotNear(ground) ?? ground).ToWorldCoordinates(8f, 0f);
	}

	static NPC Core()
	{
		foreach (var npc in Main.ActiveNPCs)
		{
			if (npc.type == NPCID.MoonLordCore)
				return npc;
		}
		return null;
	}

	static Point? NearestDeck(int coreX, int wantedX)
	{
		Point? best = null;
		int bestGap = int.MaxValue;
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (SkyArena.For(faction) is not { Usable: true } arena || Math.Abs(arena.CenterX - coreX) > DeckReach + SkyArena.HalfLength)
				continue;
			int x = Utils.Clamp(wantedX, arena.Left + DeckInset, arena.Right - DeckInset);
			for (int step = 0; step <= SkyArena.HalfLength && !arena.DeckBuilt(x); step++)
				x += x > arena.CenterX ? -1 : 1;
			if (!arena.DeckBuilt(x))
				continue;
			int gap = Math.Abs(x - coreX);
			if (gap < bestGap)
			{
				best = new Point(x, arena.DeckRow - 1);
				bestGap = gap;
			}
		}
		return best;
	}
}
