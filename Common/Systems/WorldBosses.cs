using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Systems;

public static class WorldBosses
{
	public readonly record struct Boss(int[] NpcTypes, Func<bool> IsDefeated)
	{
		public string Name => string.Join("/", NpcTypes.Select(type => Lang.GetNPCNameValue(type)));
	}

	public static readonly IReadOnlyList<Boss> InProgressionOrder = new Boss[]
	{
		new(new[] { (int)NPCID.KingSlime }, () => NPC.downedSlimeKing),
		new(new[] { (int)NPCID.EyeofCthulhu }, () => NPC.downedBoss1),
		new(new[] { (int)NPCID.EaterofWorldsHead, (int)NPCID.BrainofCthulhu }, () => NPC.downedBoss2),
		new(new[] { (int)NPCID.QueenBee }, () => NPC.downedQueenBee),
		new(new[] { (int)NPCID.SkeletronHead }, () => NPC.downedBoss3),
		new(new[] { (int)NPCID.Deerclops }, () => NPC.downedDeerclops),
		new(new[] { (int)NPCID.WallofFlesh }, () => Main.hardMode),
		new(new[] { (int)NPCID.QueenSlimeBoss }, () => NPC.downedQueenSlime),
		new(new[] { (int)NPCID.TheDestroyer }, () => NPC.downedMechBoss1),
		new(new[] { (int)NPCID.Retinazer }, () => NPC.downedMechBoss2),
		new(new[] { (int)NPCID.SkeletronPrime }, () => NPC.downedMechBoss3),
		new(new[] { (int)NPCID.Plantera }, () => NPC.downedPlantBoss),
		new(new[] { (int)NPCID.Golem }, () => NPC.downedGolemBoss),
		new(new[] { (int)NPCID.DukeFishron }, () => NPC.downedFishron),
		new(new[] { (int)NPCID.HallowBoss }, () => NPC.downedEmpressOfLight),
		new(new[] { (int)NPCID.CultistBoss }, () => NPC.downedAncientCultist),
		new(new[] { (int)NPCID.MoonLordCore }, () => NPC.downedMoonlord)
	};

	public static int DefeatedCount => InProgressionOrder.Count(boss => boss.IsDefeated());

	public static Boss? NextUndefeated => InProgressionOrder.Where(boss => !boss.IsDefeated()).Cast<Boss?>().FirstOrDefault();
}
