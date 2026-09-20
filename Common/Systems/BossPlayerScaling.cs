using System.Linq;
using AutoTerraMod.Common.Bots;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class BossPlayerScaling : ModSystem
{
	static int? playerCountOverride;

	public override void Load()
	{
		On_NPC.NewNPC += ScaleBossForSummoningFaction;
		On_NPC.GetActivePlayerCount += UseSummoningFactionCount;
	}

	static int UseSummoningFactionCount(On_NPC.orig_GetActivePlayerCount orig) => playerCountOverride ?? orig();

	static int ScaleBossForSummoningFaction(On_NPC.orig_NewNPC orig, IEntitySource source, int x, int y, int type, int start, float ai0, float ai1, float ai2, float ai3, int target)
	{
		if (Main.netMode == NetmodeID.SinglePlayer || !Main.expertMode || playerCountOverride != null || !BotWorldSystem.IsRunning || PlayerCountFor(source, x, y, type) is not int count)
			return orig(source, x, y, type, start, ai0, ai1, ai2, ai3, target);
		playerCountOverride = count;
		try
		{
			return orig(source, x, y, type, start, ai0, ai1, ai2, ai3, target);
		}
		finally
		{
			playerCountOverride = null;
		}
	}

	public static bool IsBossType(int type) =>
		type >= 0 && type < NPCLoader.NPCCount && (ContentSamples.NpcsByNetId.TryGetValue(type, out var sample) && sample.boss || NPCID.Sets.ShouldBeCountedAsBoss[type]);

	static int? PlayerCountFor(IEntitySource source, int x, int y, int type)
	{
		if (source is EntitySource_Parent { Entity: NPC parent } && parent.active && (parent.boss || IsBossType(parent.type)))
			return parent.statsAreScaledForThisManyPlayers > 0 ? parent.statsAreScaledForThisManyPlayers : null;
		if (!IsBossType(type))
			return null;
		var summoner = source is EntitySource_BossSpawn { Target: Player player } ? BotOf(player) : null;
		summoner ??= NearestBot(new Vector2(x, y));
		return summoner == null ? null : FactionPlayerCount(summoner.FactionId);
	}

	public static int FactionPlayerCount(int factionId) =>
		System.Math.Max(1, BotWorldSystem.Bots.Count(bot => bot.FactionId == factionId && bot.IsAlive) + RealPlayerCount());

	static int RealPlayerCount() =>
		Main.player.Count(player => player != null && player.active && BotOf(player) == null);

	static Bot BotOf(Player player) => BotWorldSystem.Bots.FirstOrDefault(bot => bot.Avatar == player);

	static Bot NearestBot(Vector2 position) =>
		BotWorldSystem.Bots.Where(bot => bot.IsAlive).MinBy(bot => Vector2.DistanceSquared(bot.Center, position));
}
