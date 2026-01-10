using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public abstract record BossTrigger;

public sealed record SummonItemTrigger(int Item, Landmark? Place = null, Func<Player, bool> CanUseHere = null) : BossTrigger;

public sealed record SmashOrbsTrigger : BossTrigger;

public sealed record CurseOldManTrigger : BossTrigger;

public sealed record VoodooDollTrigger : BossTrigger;

public sealed record ClothierDollTrigger : BossTrigger;

public sealed record BossChallenge(int BossType, int[] Parts, Func<bool> IsDefeated, bool NightOnly, float TotalLife, BossTrigger[] Triggers)
{
	public string Name => Lang.GetNPCNameValue(BossType);

	public bool IsPart(int npcType) => npcType == BossType || Parts.Contains(npcType);

	public bool CanSummonNow => !NightOnly || !Main.dayTime;

	public Landmark? Arena => BossType switch
	{
		NPCID.EaterofWorldsHead => Landmark.Corruption,
		NPCID.BrainofCthulhu => Landmark.Crimson,
		_ => null
	};

	public static bool InArena(Player player, Landmark arena) => arena switch
	{
		Landmark.Corruption => player.ZoneCorrupt,
		Landmark.Crimson => player.ZoneCrimson,
		_ => true
	};
}

public static class BossCatalog
{
	static readonly int[] NoParts = Array.Empty<int>();

	public static IReadOnlyList<BossChallenge> Challenges { get; } = Build();

	static BossChallenge[] Build() => new BossChallenge[]
	{
		new(NPCID.KingSlime, NoParts, () => NPC.downedSlimeKing, false, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.SlimeCrown) }),
		new(NPCID.EyeofCthulhu, NoParts, () => NPC.downedBoss1, true, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.SuspiciousLookingEye) }),
		new(NPCID.EaterofWorldsHead, new int[] { NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail }, () => NPC.downedBoss2, false, 7500f,
			new BossTrigger[] { new SmashOrbsTrigger(), new SummonItemTrigger(ItemID.WormFood, Landmark.Corruption, player => player.ZoneCorrupt) }),
		new(NPCID.BrainofCthulhu, NoParts, () => NPC.downedBoss2, false, 3000f,
			new BossTrigger[] { new SmashOrbsTrigger(), new SummonItemTrigger(ItemID.BloodySpine, Landmark.Crimson, player => player.ZoneCrimson) }),
		new(NPCID.QueenBee, NoParts, () => NPC.downedQueenBee, false, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.Abeemination, Landmark.Jungle, player => player.ZoneJungle) }),
		new(NPCID.SkeletronHead, new int[] { NPCID.SkeletronHand }, () => NPC.downedBoss3, true, 0f,
			new BossTrigger[] { new CurseOldManTrigger(), new ClothierDollTrigger() }),
		new(NPCID.Deerclops, NoParts, () => NPC.downedDeerclops, false, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.DeerThing, Landmark.Snow, player => player.ZoneSnow) }),
		new(NPCID.WallofFlesh, new int[] { NPCID.WallofFleshEye }, () => Main.hardMode, false, 0f,
			new BossTrigger[] { new VoodooDollTrigger() })
	};

	public static bool AppliesToThisWorld(BossChallenge challenge) => challenge.BossType switch
	{
		NPCID.EaterofWorldsHead => !WorldGen.crimson,
		NPCID.BrainofCthulhu => WorldGen.crimson,
		_ => true
	};

	public static BossChallenge ChallengeOf(int npcType) => Challenges.FirstOrDefault(challenge => challenge.IsPart(npcType));

	public static bool IsBossPart(NPC npc) => npc.boss || ChallengeOf(npc.type) != null;

	public static bool IsBossAlive(BossChallenge challenge) => Main.npc.Any(npc => npc.active && challenge.IsPart(npc.type));

	public static NPC NearestActiveBoss(Vector2 from, float range)
	{
		NPC nearest = null;
		float nearestDistance = range;
		foreach (var npc in Main.npc)
		{
			if (!npc.active || !IsBossPart(npc))
				continue;
			float distance = Vector2.Distance(from, npc.Center);
			if (distance < nearestDistance)
			{
				nearest = npc;
				nearestDistance = distance;
			}
		}
		return nearest;
	}
}
