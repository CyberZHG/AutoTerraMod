using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Factions;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.World;

public sealed record SkyArena(int CenterX, int DeckRow)
{
	public const int HalfLength = 60;
	const int Height = 55;
	const int ExtraHeights = 4;
	const int HeightStep = 10;
	const int Headroom = 6;
	const int CampMargin = 15;
	const int ShiftStep = 30;
	const int Shifts = 6;
	const int FightBelow = 3;
	const int FightAbove = 30;
	const int FightSideSlack = 8;

	static readonly Dictionary<int, SkyArena> planned = new();

	public static void Forget() => planned.Clear();

	public int Left => CenterX - HalfLength;
	public int Right => CenterX + HalfLength;
	public int Surface => TileQuery.SurfaceY(CenterX);
	public Vector2 Center => new((CenterX + 2) * 16f + 8f, DeckRow * 16f);

	public static SkyArena For(Faction faction)
	{
		if (planned.TryGetValue(faction.Id, out var known))
			return known;
		int leftOrigin = (faction.Settlement?.Left ?? faction.BaseTile.X) - CampMargin - HalfLength;
		int rightOrigin = (faction.Settlement?.Right ?? faction.BaseTile.X) + CampMargin + HalfLength;
		SkyArena found = null;
		for (int attempt = 0; attempt < Shifts * 2 && found == null; attempt++)
		{
			int shift = attempt % Shifts;
			int x = attempt < Shifts ? leftOrigin - shift * ShiftStep : rightOrigin + shift * ShiftStep;
			if (x - HalfLength < 50 || x + HalfLength > Main.maxTilesX - 50)
				continue;
			for (int lift = 0; lift < ExtraHeights && found == null; lift++)
			{
				int deck = TileQuery.SurfaceY(x) - Height - lift * HeightStep;
				if (deck > 60 && Clear(x, deck) && new SkyArena(x, deck) is { Endangered: false } candidate)
					found = candidate;
			}
		}
		planned[faction.Id] = found;
		return found;
	}

	public bool Endangered =>
		Bosses.BossRituals.InPillarZone(new Vector2(Left * 16f, DeckRow * 16f)) || Bosses.BossRituals.InPillarZone(new Vector2(Right * 16f, DeckRow * 16f));

	public static SkyArena Safe(Faction faction)
	{
		if (For(faction) is { Endangered: true })
			planned.Remove(faction.Id);
		return For(faction);
	}

	static bool Ours(int x, int y) => TileQuery.IsPlatform(x, y) || Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType];

	static bool Clear(int center, int deck)
	{
		for (int x = center - HalfLength; x <= center + HalfLength; x++)
		{
			for (int y = deck - Headroom; y <= deck; y++)
			{
				if (Main.tile[x, y].HasTile && !Ours(x, y))
					return false;
			}
		}
		int ground = TileQuery.SurfaceY(center);
		for (int y = deck; y < ground; y++)
		{
			if (TileQuery.IsSolid(center, y) && !Ours(center, y))
				return false;
		}
		return true;
	}

	public bool DeckBuilt(int x) => x == CenterX ? TileQuery.HasTile(new Point(x, DeckRow)) : TileQuery.IsPlatform(x, DeckRow);

	public Point? NextGap()
	{
		for (int distance = 1; distance <= HalfLength; distance++)
		{
			foreach (int side in new[] { 1, -1 })
			{
				int x = CenterX + side * distance;
				if (!DeckBuilt(x) && DeckBuilt(x - side))
					return new Point(x, DeckRow);
			}
		}
		return null;
	}

	public bool RopeReady => Bots.Tasks.RopeColumnTask.MissingRow(CenterX, DeckRow, Surface - 1) == null;

	public bool IsComplete => RopeReady && NextGap() == null;

	public bool Contains(Point tile) =>
		tile.X >= Left - FightSideSlack && tile.X <= Right + FightSideSlack && tile.Y >= DeckRow - FightAbove && tile.Y <= DeckRow + FightBelow;

	public static bool FightingOn(Bots.Bot bot) =>
		For(bot.Faction) is { } arena && arena.Contains(bot.CenterTile) && arena.DeckBuilt(arena.Left) && Main.npc.Any(npc => npc.active && npc.boss);

	public static bool Wanted => Main.hardMode && (NPC.downedPlantBoss || NPC.downedMechBossAny);
}
