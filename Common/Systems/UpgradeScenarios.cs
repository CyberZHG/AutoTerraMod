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
	sealed record UpgradeScenario(int[] AlreadyDefeated, (int item, int count)[] Supplies, Func<Bot, bool> Expectation, string ExpectationText, int[] Focus = null);

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
			new (int, int)[] { (ItemID.Hellstone, 360), (ItemID.Obsidian, 120), (ItemID.Hellforge, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 100 && WearsAny(bot, (ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves)),
			"molten pickaxe and full molten armor"),
		["upgrade-hardmode"] = new(Hardmode,
			new (int, int)[] { (ItemID.LifeCrystal, 15), (ItemID.HealingPotion, 20), (ItemID.Pwnhammer, 1), (ItemID.MoltenPickaxe, 1), (ItemID.Hellforge, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => WorldGen.altarCount >= 3 && Pick(bot) >= 110,
			"three altars smashed and a cobalt/palladium pickaxe from mined ore"),
		["accessories"] = new(Hardmode,
			new (int, int)[]
			{
				(ItemID.LifeCrystal, 15), (ItemID.TinkerersWorkshop, 1), (ItemID.Vitamins, 1), (ItemID.ArmorPolish, 1), (ItemID.Bezoar, 1), (ItemID.AdhesiveBandage, 1),
				(ItemID.Nazar, 1), (ItemID.Megaphone, 1), (ItemID.TrifoldMap, 1), (ItemID.FastClock, 1), (ItemID.CobaltShield, 1), (ItemID.ObsidianSkull, 1), (ItemID.Blindfold, 1), (ItemID.PocketMirror, 1)
			},
			bot => bot.Avatar.armor.Any(item => item.type == ItemID.AnkhShield) || Inventory.Count(bot.Avatar, ItemID.AnkhShield) > 0,
			"an Ankh Shield assembled at a Tinkerer's Workshop"),
		["flight"] = new(AfterMechs,
			new (int, int)[]
			{
				(ItemID.LifeCrystal, 15), (ItemID.GreaterHealingPotion, 30), (ItemID.Megashark, 1), (ItemID.CrystalBullet, 3000),
				(ItemID.AdamantiteMask, 1), (ItemID.AdamantiteBreastplate, 1), (ItemID.AdamantiteLeggings, 1),
				(ItemID.DirtBlock, 400), (ItemID.WoodPlatform, 300), (ItemID.SoulofLight, 25), (ItemID.Feather, 10), (ItemID.MythrilAnvil, 1)
			},
			bot => bot.Avatar.armor.Any(item => item.type is ItemID.AngelWings or ItemID.DemonWings) || Inventory.Count(bot.Avatar, ItemID.AngelWings) > 0,
			"angel wings from souls of flight farmed in the sky", new int[] { ItemID.AngelWings }),
		["summoner"] = new(PreHardmode,
			new (int, int)[] { (ItemID.ImpStaff, 1), (ItemID.ThornWhip, 1), (ItemID.ManaCrystal, 5), (ItemID.HealingPotion, 20), (ItemID.LifeCrystal, 10), (ItemID.MoltenHelmet, 1), (ItemID.MoltenBreastplate, 1), (ItemID.MoltenGreaves, 1) },
			bot => bot.Avatar.ownedProjectileCounts[ProjectileID.FlyingImp] > 0,
			"an imp minion fighting beside every bot"),
		["channel"] = new(Hardmode,
			new (int, int)[] { (ItemID.VortexBeater, 1), (ItemID.LastPrism, 1), (ItemID.ChlorophyteBullet, 2000), (ItemID.ManaCrystal, 9), (ItemID.LifeCrystal, 15), (ItemID.GreaterHealingPotion, 20), (ItemID.GreaterManaPotion, 30) },
			bot => bot.Hands.BestWeapon()?.type is ItemID.VortexBeater or ItemID.LastPrism && bot.Hands.ShotsFired > 20,
			"vortex beater and last prism firing at enemies"),
		["banners"] = new(PreHardmode,
			new (int, int)[] { (ItemID.ZombieBanner, 1), (ItemID.SlimeBanner, 1), (ItemID.DemonEyeBanner, 1) },
			bot => !Inventory.Items(bot.Avatar).Any(item => item.createTile == TileID.Banners),
			"every banner hung under a ceiling at base"),
		["collector"] = new(PreHardmode,
			new (int, int)[] { (ItemID.Mannequin, 1), (ItemID.IronHelmet, 1), (ItemID.IronChainmail, 1), (ItemID.IronGreaves, 1), (ItemID.GoldHelmet, 1), (ItemID.GoldChainmail, 1), (ItemID.GoldGreaves, 1) },
			bot => !Inventory.Items(bot.Avatar).Any(item => item.type is ItemID.IronChainmail or ItemID.GoldChainmail) || bot.Avatar.armor.Take(3).Any(item => item.type is ItemID.IronChainmail or ItemID.GoldChainmail) && !Inventory.Items(bot.Avatar).Any(item => item.type is ItemID.IronChainmail or ItemID.GoldChainmail),
			"spare armor sets dressed on mannequins"),
		["upgrade-hallowed"] = new(AfterMechs,
			new (int, int)[] { (ItemID.LifeCrystal, 15), (ItemID.HealingPotion, 20), (ItemID.HallowedBar, 80), (ItemID.SoulofFright, 1), (ItemID.SoulofMight, 1), (ItemID.SoulofSight, 1), (ItemID.MythrilAnvil, 1), (ItemID.IronOre, 30), (ItemID.Wood, 80), (ItemID.StoneBlock, 60) },
			bot => Pick(bot) >= 200 && WearsAny(bot, (ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHelmet, ItemID.HallowedPlateMail, ItemID.HallowedGreaves), (ItemID.HallowedHeadgear, ItemID.HallowedPlateMail, ItemID.HallowedGreaves)),
			"pickaxe axe/drax and full hallowed armor")
	};

	public static bool GearFirst => Active != null && HeadlessTestMode.Scenario is not ("banners" or "collector");

	public static int[] Focus => Active?.Focus;

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
			if (HeadlessTestMode.Scenario == "summoner")
				bot.Personality.Style = CombatStyle.Summon;
			if (HeadlessTestMode.Scenario == "collector")
				bot.Personality.Aspiration = Aspiration.Collector;
			if (HeadlessTestMode.Scenario == "banners")
				bot.Personality.Aspiration = Aspiration.MonsterHunter;
			if (HeadlessTestMode.Scenario == "channel")
				bot.Personality.Style = bot.Id % 2 == 0 ? CombatStyle.Ranged : CombatStyle.Magic;
			HeadlessTestMode.Give(bot, ItemID.GrapplingHook, 1);
			HeadlessTestMode.Give(bot, ItemID.MagicMirror, 1);
			foreach (var (item, count) in scenario.Supplies)
				HeadlessTestMode.Give(bot, item, count);
		}
		Main.dayTime = true;
		Main.time = 0;
		if (HeadlessTestMode.Scenario == "accessories")
			RemoveTiles(TileID.TinkerersWorkbench);
		RevealAltars();
		LogOre(TileID.Cobalt, TileID.Palladium);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"upgrade scenario '{HeadlessTestMode.Scenario}' prepared; expecting {scenario.ExpectationText}; altars smashed so far {WorldGen.altarCount}");
	}

	static void LogOre(params int[] oreTiles)
	{
		var found = new List<int>();
		for (int x = 10; x < Main.maxTilesX - 10; x++)
			for (int y = 10; y < Main.maxTilesY - 10; y++)
				if (Main.tile[x, y].HasTile && oreTiles.Contains(Main.tile[x, y].TileType))
					found.Add(y);
		var bases = string.Join(", ", BotWorldSystem.Factions.Select(faction => faction.BaseTile.ToString()));
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"upgrade scenario ore: {found.Count} tiles, depth {(found.Count > 0 ? $"{found.Min()}..{found.Max()} median {found.OrderBy(y => y).ElementAt(found.Count / 2)}" : "-")}, rockLayer {(int)Main.rockLayer}, bases {bases}");
	}

	static void RemoveTiles(int tileType)
	{
		for (int x = 10; x < Main.maxTilesX - 10; x++)
			for (int y = 10; y < Main.UnderworldLayer; y++)
				if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
					WorldGen.KillTile(x, y, noItem: true);
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
			logger.Info($"upgrade report {bot.Faction.Name}/{bot.Name}: pick={Pick(bot)} weapon={bot.Hands.BestWeapon()?.Name ?? "-"} armor=[{string.Join(", ", bot.Avatar.armor.Take(3).Where(item => !item.IsAir).Select(item => item.Name))}] defense={bot.Avatar.statDefense} shots={bot.Hands.ShotsFired} flightSouls={Inventory.Count(bot.Avatar, ItemID.SoulofFlight)} met={scenario.Expectation(bot)}");
		if (BotWorldSystem.Bots.All(scenario.Expectation))
		{
			passed = true;
			logger.Info($"upgrade scenario '{HeadlessTestMode.Scenario}' PASSED after {Main.GameUpdateCount / 60}s: every bot has {scenario.ExpectationText}");
		}
	}
}
