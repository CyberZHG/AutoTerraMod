using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public sealed record BossChallenge(int BossType, int SummonItem, Func<Player, bool> CanSummonNow, Func<bool> IsDefeated, string ConditionKey)
{
	public string Name => Lang.GetNPCNameValue(BossType);
}

public static class BossCatalog
{
	public static readonly IReadOnlyList<BossChallenge> Challenges = new BossChallenge[]
	{
		new(NPCID.KingSlime, ItemID.SlimeCrown, _ => true, () => NPC.downedSlimeKing, "Anytime"),
		new(NPCID.EyeofCthulhu, ItemID.SuspiciousLookingEye, _ => !Main.dayTime, () => NPC.downedBoss1, "AtNight")
	};

	public static BossChallenge NextChallenge => Challenges.FirstOrDefault(challenge => !challenge.IsDefeated());

	public static bool IsBossAlive(int bossType) => Main.npc.Any(npc => npc.active && npc.type == bossType);

	public static NPC NearestActiveBoss(Microsoft.Xna.Framework.Vector2 from, float range)
	{
		NPC nearest = null;
		float nearestDistance = range;
		foreach (var npc in Main.npc)
		{
			if (!npc.active || !npc.boss)
				continue;
			float distance = Microsoft.Xna.Framework.Vector2.Distance(from, npc.Center);
			if (distance < nearestDistance)
			{
				nearest = npc;
				nearestDistance = distance;
			}
		}
		return nearest;
	}
}
