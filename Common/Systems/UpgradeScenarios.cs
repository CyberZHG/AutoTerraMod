using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class UpgradeScenarios : ModSystem
{
	sealed record UpgradeScenario(int[] AlreadyDefeated, (int item, int count)[] Supplies, Func<Bot, bool> Expectation, string ExpectationText);

	static int Pick(Bot bot) => Equipment.BestPower(bot.Avatar, GearKind.Pickaxe);

	static bool Wears(Bot bot, params int[] items) => items.All(item => bot.Avatar.armor.Take(3).Any(worn => worn.type == item) || Inventory.Count(bot.Avatar, item) > 0);

	static bool WearsAny(Bot bot, params (int head, int body, int legs)[] sets) => sets.Any(set => Wears(bot, set.head, set.body, set.legs));

	static readonly int[] PreHardmode = { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead, NPCID.BrainofCthulhu, NPCID.QueenBee, NPCID.SkeletronHead, NPCID.Deerclops };
	static readonly int[] Hardmode = PreHardmode.Append(NPCID.WallofFlesh).ToArray();
	static readonly int[] AfterMechs = Hardmode.Concat(new int[] { NPCID.QueenSlimeBoss, NPCID.TheDestroyer, NPCID.Retinazer, NPCID.SkeletronPrime }).ToArray();

	static readonly Dictionary<string, UpgradeScenario> Scenarios = new()
	{
		["upgrade-ore"] = new(Array.Empty<int>(),
			new (int, int)[] { (ItemID.IronOre, 330), (ItemID.Wood, 120), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 40 && WearsAny(bot, (ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves)),
			"iron pickaxe and full iron armor"),
		["upgrade-evil"] = new(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.EaterofWorldsHead },
			new (int, int)[] { (ItemID.DemoniteOre, 330), (ItemID.ShadowScale, 80), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 65 && WearsAny(bot, (ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves)),
			"nightmare pickaxe and full shadow armor"),
		["upgrade-hell"] = new(PreHardmode,
			new (int, int)[] { (ItemID.Hellstone, 240), (ItemID.Obsidian, 80), (ItemID.Hellforge, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 100 && WearsAny(bot, (ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves)),
			"molten pickaxe and full molten armor"),
		["upgrade-hardmode"] = new(Hardmode,
			new (int, int)[] { (ItemID.Pwnhammer, 1), (ItemID.MoltenPickaxe, 1), (ItemID.Hellforge, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => WorldGen.altarCount >= 3 && Pick(bot) >= 110,
			"three altars smashed and a cobalt/palladium pickaxe from mined ore"),
		["upgrade-hallowed"] = new(AfterMechs,
			new (int, int)[] { (ItemID.HallowedBar, 80), (ItemID.SoulofFright, 1), (ItemID.SoulofMight, 1), (ItemID.SoulofSight, 1), (ItemID.MythrilAnvil, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 200 && WearsAny(bot, (ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHelmet, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHeadgear, ItemID.HallowedPlateMail, ItemID.HallowedGreaves)),
			"pickaxe axe/drax and full hallowed armor")
	};

	public static bool GearFirst => Active != null;

	static UpgradeScenario Active => HeadlessTestMode.Scenario is string name && Scenarios.TryGetValue(name, out var scenario) ? scenario : null;

	static bool passed;

	public static void Prepare()
	{
		if (Active is not UpgradeScenario scenario)
			return;
		passed = false;
		foreach (var faction in BotWorldSystem.Factions)
		{
			faction.DefeatedBosses.Clear();
			foreach (int boss in scenario.AlreadyDefeated)
				faction.DefeatedBosses.Add(boss);
		}
		foreach (int boss in scenario.AlreadyDefeated)
			HeadlessTestMode.MarkWorldDefeated(boss);
		foreach (var bot in BotWorldSystem.Bots)
		{
			HeadlessTestMode.Give(bot, ItemID.GrapplingHook, 1);
			HeadlessTestMode.Give(bot, ItemID.MagicMirror, 1);
			foreach (var (item, count) in scenario.Supplies)
				HeadlessTestMode.Give(bot, item, count);
		}
		Main.dayTime = true;
		Main.time = 0;
		RevealAltars();
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"upgrade scenario '{HeadlessTestMode.Scenario}' prepared; expecting {scenario.ExpectationText}");
	}

	static void RevealAltars()
	{
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = 10; y < Main.UnderworldLayer; y++)
			{
				if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.DemonAltar)
					continue;
				foreach (var faction in BotWorldSystem.Factions)
					BotWorldSystem.KnowledgeOf(faction).Remember(new Microsoft.Xna.Framework.Point(x, y), TileID.DemonAltar);
			}
		}
	}

	const int ReportIntervalTicks = 3600;

	public override void PostUpdateWorld()
	{
		if (Active is not UpgradeScenario scenario || passed || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		var logger = ModContent.GetInstance<AutoTerraMod>().Logger;
		foreach (var bot in BotWorldSystem.Bots)
			logger.Info($"upgrade report {bot.Faction.Name}/{bot.Name}: pick={Pick(bot)} weapon={bot.Hands.BestWeapon()?.Name ?? "-"} armor=[{string.Join(", ", bot.Avatar.armor.Take(3).Where(item => !item.IsAir).Select(item => item.Name))}] defense={bot.Avatar.statDefense} met={scenario.Expectation(bot)}");
		if (BotWorldSystem.Bots.All(scenario.Expectation))
		{
			passed = true;
			logger.Info($"upgrade scenario '{HeadlessTestMode.Scenario}' PASSED after {Main.GameUpdateCount / 60}s: every bot has {scenario.ExpectationText}");
		}
	}
}
