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
		x = FlatColumnNear(x);
		var ground = new Point(x, TileQuery.SurfaceY(x) - 1);
		return (Grounds.StandingSpotNear(ground) ?? ground).ToWorldCoordinates(8f, 0f);
	}

	const int FlatSearchSpan = 40;
	const int FlatMargin = 3;
	const int FlatTolerance = 2;
	static readonly System.Collections.Generic.Dictionary<int, (int flat, uint at)> flatCache = new();
	const uint FlatCacheTicks = 120;
	const int FlatCacheLimit = 64;

	static int FlatColumnNear(int x)
	{
		if (flatCache.TryGetValue(x, out var cached) && Main.GameUpdateCount - cached.at < FlatCacheTicks)
			return cached.flat;
		if (flatCache.Count > FlatCacheLimit)
			flatCache.Clear();
		int flat = x;
		for (int distance = 0; distance <= FlatSearchSpan; distance++)
		{
			if (IsFlat(x - distance))
			{
				flat = x - distance;
				break;
			}
			if (IsFlat(x + distance))
			{
				flat = x + distance;
				break;
			}
		}
		flatCache[x] = (flat, Main.GameUpdateCount);
		return flat;
	}

	static bool IsFlat(int x)
	{
		if (x - FlatMargin < 50 || x + 1 + FlatMargin > Main.maxTilesX - 50)
			return false;
		int surface = TileQuery.SurfaceY(x);
		for (int column = x - FlatMargin; column <= x + 1 + FlatMargin; column++)
		{
			if (Math.Abs(TileQuery.SurfaceY(column) - surface) > FlatTolerance)
				return false;
		}
		return true;
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
