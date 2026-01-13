using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Spectating;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace AutoTerraMod.Common.UI;

public sealed class SpectatorHud : ModSystem
{
	const float LineHeight = 22f;
	const float PanelLeft = 24f;
	const float PanelTop = 110f;

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (!BotWorldSystem.IsRunning)
			return;
		int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
		if (index < 0)
			index = layers.Count;
		layers.Insert(index, new LegacyGameInterfaceLayer("AutoTerraMod: Bot Inventory", BotInventoryPanel.Draw, InterfaceScaleType.UI));
		layers.Insert(index, new LegacyGameInterfaceLayer("AutoTerraMod: Spectator Panel", DrawPanel, InterfaceScaleType.UI));
		layers.Insert(index, new LegacyGameInterfaceLayer("AutoTerraMod: Bot Labels", DrawLabels, InterfaceScaleType.Game));
	}

	static string Text(string key, params object[] args) => Language.GetTextValue($"Mods.AutoTerraMod.{key}", args);

	static bool DrawLabels()
	{
		foreach (var bot in BotWorldSystem.Bots)
		{
			var body = bot.Npc;
			if (body == null)
				continue;
			var anchor = body.Top - Main.screenPosition + new Vector2(0f, -30f);
			Utils.DrawBorderString(Main.spriteBatch, bot.Name, anchor, bot.Faction.Color, 0.8f, 0.5f, 1f);
			string task = bot.Brain.Current == null ? "" : Text($"Tasks.{bot.Brain.Current.NameKey}");
			Utils.DrawBorderString(Main.spriteBatch, task, anchor + new Vector2(0f, 14f), Color.LightGray, 0.6f, 0.5f, 1f);
		}
		return true;
	}

	static bool DrawPanel()
	{
		var lines = new List<(string text, Color color)>();
		var bot = SpectatorSystem.Followed;
		if (bot != null)
			DescribeBot(bot, lines);
		else if (SpectatorSystem.IsSpectating)
			lines.Add((Text("UI.FreeCamera"), Color.White));
		lines.Add(("", Color.White));
		DescribeFactions(lines);
		lines.Add((Text("UI.Controls",
			SpectatorSystem.KeysOf(SpectatorSystem.PreviousBotKey),
			SpectatorSystem.KeysOf(SpectatorSystem.NextBotKey),
			SpectatorSystem.KeysOf(SpectatorSystem.FreeCameraKey),
			SpectatorSystem.KeysOf(SpectatorSystem.InventoryKey),
			SpectatorSystem.KeysOf(SpectatorSystem.PlayInPersonKey)), Color.Gray));

		var position = new Vector2(PanelLeft, PanelTop);
		foreach (var (text, color) in lines)
		{
			Utils.DrawBorderString(Main.spriteBatch, text, position, color, 0.85f);
			position.Y += LineHeight;
		}
		return true;
	}

	static void DescribeBot(Bot bot, List<(string, Color)> lines)
	{
		var avatar = bot.Avatar;
		var traits = bot.Personality;
		lines.Add((Text("UI.Watching", bot.Name, bot.Faction.Name, Text($"Archetypes.{traits.Archetype}"), Text($"Styles.{traits.Style}")), bot.Faction.Color));
		if (!bot.IsAlive)
			lines.Add((Text("UI.Respawning", (bot.RespawnTicks + 59) / 60), Color.OrangeRed));
		lines.Add((Text("UI.Vitals", avatar.statLife, avatar.statLifeMax2, avatar.statMana, avatar.statManaMax2, (int)avatar.statDefense), Color.White));
		if (avatar.breath < avatar.breathMax)
			lines.Add((Text("UI.Breath", avatar.breath * 100 / System.Math.Max(1, avatar.breathMax)), Color.LightSkyBlue));
		var task = bot.Brain.Current;
		if (task != null)
			lines.Add((Text("UI.Task", Text($"Tasks.{task.NameKey}"), task.Detail), Color.White));
		lines.Add((Text("UI.Plan", DescribePlan(bot.Brain.Plan)), Color.White));
		if (bot.IsAlive && bot.Watchdog.StalledSeconds >= 10)
			lines.Add((Text("UI.Stalled", bot.Watchdog.StalledSeconds), Color.Orange));
		var held = avatar.inventory[avatar.selectedItem];
		var weapon = bot.Hands.BestWeapon();
		lines.Add((Text("UI.Gear", bot.Hands.BestPickaxe?.Name ?? "-", weapon?.Name ?? "-", held.IsAir ? "-" : held.Name), Color.White));
		lines.Add((Text("UI.Traits",
			Percent(traits.Mining), Percent(traits.Exploring), Percent(traits.Building),
			Percent(traits.Combat), Percent(traits.Courage), Percent(traits.Cooperation)), Color.LightGray));
		lines.Add((Text("UI.Aspiration", Text($"Aspirations.{traits.Aspiration}"), Text($"Tastes.{traits.Taste}")), Color.LightGray));
	}

	static string Percent(float value) => $"{(int)(value * 100)}";

	static string DescribePlan(PlanStep plan) => plan switch
	{
		GatherStep gather => Text("Plans.Gather", Lang.GetItemNameValue(gather.ItemType), gather.Amount),
		CraftStep craft => Text("Plans.Craft", craft.Recipe.createItem.Name),
		PlaceStationStep place => Text("Plans.Place", Lang.GetItemNameValue(place.ItemType)),
		BuildHouseStep build => Text("Plans.Build", Text($"BuildActions.{build.Operation.Action}")),
		WithdrawStep withdraw => Text("Plans.Withdraw", Lang.GetItemNameValue(withdraw.ItemType), withdraw.Amount),
		DeliverStep deliver => Text("Plans.Deliver", Lang.GetItemNameValue(deliver.ItemType), BotWorldSystem.FindBot(deliver.RecipientId)?.Name ?? "?"),
		SummonBossStep summon => Text("Plans.Summon", summon.Challenge.Name),
		ScoutStep scout => Text("Plans.Scout", Text($"Landmarks.{scout.Landmark}")),
		SmashShrineStep => Text("Plans.Smash"),
		TravelStep => Text("Plans.Travel"),
		BuildBridgeStep => Text("Tasks.BuildBridge"),
		_ => Text("Plans.Idle")
	};

	static void DescribeFactions(List<(string, Color)> lines)
	{
		lines.Add((Text($"UI.Relation{BotWorldSystem.Relation}"), Color.Gold));
		var nextBoss = WorldBosses.NextUndefeated;
		lines.Add((Text("UI.BossProgress", WorldBosses.DefeatedCount, WorldBosses.InProgressionOrder.Count, nextBoss?.Name ?? Text("UI.AllBossesDefeated")), Color.Gold));
		foreach (var faction in BotWorldSystem.Factions)
		{
			var members = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id).ToList();
			int alive = members.Count(bot => bot.IsAlive);
			int bestPick = members.Select(bot => Equipment.BestPower(bot.Avatar, GearKind.Pickaxe)).DefaultIfEmpty(0).Max();
			string house = faction.Settlement == null ? "-" : Text("UI.SettlementProgress",
				faction.Settlement.Structures.Count(structure => Building.HouseInspector.IsComplete(faction, structure)),
				faction.Settlement.Structures.Count,
				Text($"Roofs.{faction.HouseDesign.Roof}"),
				Text($"Tastes.{faction.HouseDesign.Taste}"));
			lines.Add((Text("UI.FactionLine", faction.Name, alive, members.Count, bestPick, house), faction.Color));
			lines.Add((Text("UI.FactionBosses", faction.DefeatedBosses.Count, WorldBosses.InProgressionOrder.Count), faction.Color));
			var challenge = Bosses.BossCredit.NextChallengeFor(faction);
			var summoner = members.Where(bot => bot.IsAlive).MaxBy(bot => bot.Personality.Combat);
			if (challenge != null && summoner != null)
			{
				var report = Bosses.BossReadiness.Evaluate(faction, challenge, summoner);
				lines.Add((Text("UI.BossReadiness", challenge.Name, (int)(report.Confidence * 100)), report.IsReady ? Color.LightGreen : Color.LightGray));
			}
		}
	}
}
