using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bosses;

public abstract record BossTrigger;

public sealed record SummonItemTrigger(int Item, Landmark? Place = null, Func<Player, bool> CanUseHere = null, int[] AlsoSpawns = null) : BossTrigger;

public sealed record AltarTrigger(int Item, int AltarTile, Landmark Place) : BossTrigger;

public sealed record BulbTrigger : BossTrigger;

public sealed record BaitTrigger(int Bait) : BossTrigger;

public sealed record LacewingTrigger : BossTrigger;

public sealed record CultistRitualTrigger : BossTrigger;

public sealed record PillarsTrigger : BossTrigger;

public sealed record SmashOrbsTrigger : BossTrigger;

public sealed record SmashLarvaTrigger : BossTrigger;

public sealed record CurseOldManTrigger : BossTrigger;

public sealed record VoodooDollTrigger : BossTrigger;

public sealed record ClothierDollTrigger : BossTrigger;

public sealed record BossChallenge(int BossType, int[] Parts, Func<bool> IsDefeated, bool NightOnly, float TotalLife, BossTrigger[] Triggers, Func<bool> Unlocked = null)
{
	public string Name => Lang.GetNPCNameValue(BossType);

	public bool IsUnlocked => Unlocked?.Invoke() ?? true;

	public bool IsPart(int npcType) => npcType == BossType || Parts.Contains(npcType);

	const double LatestSummonTime = Main.nightLength * 0.5;

	public bool CanSummonNow => !NightOnly || (!Main.dayTime && Main.time < LatestSummonTime);

	const float TravelTicksPerTile = 20f;

	public bool WindowStillOpen => !NightOnly || Main.dayTime || Main.time < LatestSummonTime;

	public bool CanSetOut(float tilesToTravel)
	{
		if (!NightOnly)
			return true;
		float lead = tilesToTravel * TravelTicksPerTile;
		return Main.dayTime ? Main.time >= Main.dayLength - lead : Main.time + lead < LatestSummonTime;
	}

	public float ThreatMultiplier => BossType switch
	{
		NPCID.QueenBee => 2.5f,
		NPCID.EaterofWorldsHead or NPCID.BrainofCthulhu or NPCID.SkeletronHead => 2f,
		_ => 1f
	};

	public float Exposure => BossType switch
	{
		NPCID.TheDestroyer => 3f,
		_ => 1f
	};

	public Landmark? Arena => BossType switch
	{
		NPCID.EaterofWorldsHead => Landmark.Corruption,
		NPCID.BrainofCthulhu => Landmark.Crimson,
		NPCID.QueenBee => Landmark.Jungle,
		NPCID.WallofFlesh => Landmark.Underworld,
		NPCID.Plantera => Landmark.Jungle,
		NPCID.Golem => Landmark.Temple,
		NPCID.DukeFishron => Landmark.Ocean,
		_ => null
	};

	public Landmark? DistantArena => BossType switch
	{
		NPCID.SkeletronHead when !NPC.downedBoss3 => Landmark.Dungeon,
		NPCID.DukeFishron => Landmark.Ocean,
		_ => null
	};

	public static bool InArena(Player player, Landmark arena) => arena switch
	{
		Landmark.Corruption => player.ZoneCorrupt,
		Landmark.Crimson => player.ZoneCrimson,
		Landmark.Jungle => player.ZoneJungle,
		Landmark.Underworld => player.ZoneUnderworldHeight,
		Landmark.Hallow => player.ZoneHallow,
		Landmark.Ocean => player.ZoneBeach,
		Landmark.Temple => player.ZoneLihzhardTemple,
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
			new BossTrigger[] { new SmashLarvaTrigger(), new SummonItemTrigger(ItemID.Abeemination, Landmark.Jungle, player => player.ZoneJungle) }),
		new(NPCID.SkeletronHead, new int[] { NPCID.SkeletronHand }, () => NPC.downedBoss3, true, 0f,
			new BossTrigger[] { new CurseOldManTrigger(), new ClothierDollTrigger() }),
		new(NPCID.Deerclops, NoParts, () => NPC.downedDeerclops, false, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.DeerThing, Landmark.Snow, player => player.ZoneSnow) }),
		new(NPCID.WallofFlesh, new int[] { NPCID.WallofFleshEye }, () => Main.hardMode, false, 0f,
			new BossTrigger[] { new VoodooDollTrigger() }),
		new(NPCID.QueenSlimeBoss, NoParts, () => NPC.downedQueenSlime, false, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.QueenSlimeCrystal, Landmark.Hallow, player => player.ZoneHallow) }, Hardmode),
		new(NPCID.TheDestroyer, new int[] { NPCID.TheDestroyerBody, NPCID.TheDestroyerTail }, () => NPC.downedMechBoss1, true, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.MechanicalWorm) }, Hardmode),
		new(NPCID.Retinazer, new int[] { NPCID.Spazmatism }, () => NPC.downedMechBoss2, true, 43000f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.MechanicalEye, AlsoSpawns: new int[] { NPCID.Spazmatism }) }, Hardmode),
		new(NPCID.SkeletronPrime, new int[] { NPCID.PrimeCannon, NPCID.PrimeSaw, NPCID.PrimeVice, NPCID.PrimeLaser }, () => NPC.downedMechBoss3, true, 0f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.MechanicalSkull) }, Hardmode),
		new(NPCID.Plantera, NoParts, () => NPC.downedPlantBoss, false, 0f,
			new BossTrigger[] { new BulbTrigger() }, () => NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3),
		new(NPCID.Golem, new int[] { NPCID.GolemHead, NPCID.GolemFistLeft, NPCID.GolemFistRight, NPCID.GolemHeadFree }, () => NPC.downedGolemBoss, false, 55000f,
			new BossTrigger[] { new AltarTrigger(ItemID.LihzahrdPowerCell, TileID.LihzahrdAltar, Landmark.Temple) }, () => NPC.downedPlantBoss),
		new(NPCID.DukeFishron, NoParts, () => NPC.downedFishron, false, 0f,
			new BossTrigger[] { new BaitTrigger(ItemID.TruffleWorm) }, Hardmode),
		new(NPCID.HallowBoss, NoParts, () => NPC.downedEmpressOfLight, true, 0f,
			new BossTrigger[] { new LacewingTrigger() }, () => NPC.downedPlantBoss),
		new(NPCID.CultistBoss, NoParts, () => NPC.downedAncientCultist, false, 0f,
			new BossTrigger[] { new CultistRitualTrigger() }, () => NPC.downedGolemBoss),
		new(NPCID.MoonLordCore, new int[] { NPCID.MoonLordHead, NPCID.MoonLordHand }, () => NPC.downedMoonlord, false, 140000f,
			new BossTrigger[] { new SummonItemTrigger(ItemID.CelestialSigil), new PillarsTrigger() }, () => NPC.downedAncientCultist)
	};

	static bool Hardmode() => Main.hardMode;

	public static bool AppliesToThisWorld(BossChallenge challenge) => challenge.BossType switch
	{
		NPCID.EaterofWorldsHead => !WorldGen.crimson,
		NPCID.BrainofCthulhu => WorldGen.crimson,
		_ => true
	};

	static readonly Dictionary<int, BossChallenge> challengeByType = new();

	public static BossChallenge ChallengeOf(int npcType)
	{
		if (!challengeByType.TryGetValue(npcType, out var challenge))
			challengeByType[npcType] = challenge = FindChallenge(npcType);
		return challenge;
	}

	static BossChallenge FindChallenge(int npcType) => Challenges.FirstOrDefault(candidate => candidate.IsPart(npcType));

	public static bool IsBossPart(NPC npc) => npc.boss || ChallengeOf(npc.type) != null;

	public static bool IsBossAlive(BossChallenge challenge)
	{
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (npc.active && challenge.IsPart(npc.type))
				return true;
		}
		return false;
	}

	public static NPC NearestActiveBoss(Vector2 from, float range) =>
		NearestActiveBoss(from, range, false) ?? NearestActiveBoss(from, range, true);

	static NPC NearestActiveBoss(Vector2 from, float range, bool includeInvulnerable)
	{
		NPC nearest = null;
		float nearestDistance = range;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (!npc.active || !IsBossPart(npc) || (!includeInvulnerable && (npc.dontTakeDamage || npc.immortal)))
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
