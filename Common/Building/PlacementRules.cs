using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public static class PlacementRules
{
	const uint CacheTicks = 60;

	static readonly Dictionary<int, (uint at, int pick)> weakestPick = new();

	public static bool IsEvil(int tileType) => TileID.Sets.Corrupt[tileType] || TileID.Sets.Crimson[tileType];

	public static bool Allows(Faction faction, Item item) =>
		item.createTile < 0 || !IsEvil(item.createTile) && Navigation.PickRequirement.MinimumPick(item.createTile, 0, Main.maxTilesY / 2) <= WeakestPick(faction);

	public static bool Allows(Bot bot, Item item) => Allows(bot.Faction, item);

	static int WeakestPick(Faction faction)
	{
		if (weakestPick.TryGetValue(faction.Id, out var cached) && Main.GameUpdateCount - cached.at < CacheTicks)
			return cached.pick;
		int pick = BotWorldSystem.Bots.Where(member => member.FactionId == faction.Id && member.IsAlive).Select(member => member.Hands.BestPickaxe?.pick ?? 0).DefaultIfEmpty(0).Min();
		weakestPick[faction.Id] = (Main.GameUpdateCount, pick);
		return pick;
	}
}
