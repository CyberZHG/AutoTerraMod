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
	const float PanelMargin = 24f;
	const float PanelTop = 110f;
	const float MinimapGap = 60f;
	const float PanelTextScale = 0.85f;

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (!BotWorldSystem.IsRunning)
			return;
		int index = layers.FindIndex(IsMouseText);
		if (index < 0)
			index = layers.Count;
		layers.Insert(index, inventoryLayer);
		if (ModContent.GetInstance<Config.AutoTerraConfig>().ShowInfoPanel)
			layers.Insert(index, panelLayer);
		layers.Insert(index, labelLayer);
		layers.Insert(index, hookLayer);
	}

	static readonly System.Predicate<GameInterfaceLayer> IsMouseText = layer => layer.Name == "Vanilla: Mouse Text";

	static readonly GameInterfaceLayer inventoryLayer = new LegacyGameInterfaceLayer("AutoTerraMod: Bot Inventory", BotInventoryPanel.Draw, InterfaceScaleType.UI);
	static readonly GameInterfaceLayer panelLayer = new LegacyGameInterfaceLayer("AutoTerraMod: Spectator Panel", DrawPanel, InterfaceScaleType.UI);
	static readonly GameInterfaceLayer hookLayer = new LegacyGameInterfaceLayer("AutoTerraMod: Bot Hooks", DrawHooks, InterfaceScaleType.Game);

	static bool DrawHooks()
	{
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (bot.Npc is not NPC body || bot.Grapple.Anchor is not Vector2 anchor)
				continue;
			int type = bot.Grapple.HookProjectile;
			if (type <= 0)
				continue;
			Main.instance.LoadProjectile(type);
			var delta = anchor - body.Center;
			float length = delta.Length();
			if (length < 1f)
				continue;
			var direction = delta / length;
			float rotation = delta.ToRotation() + MathHelper.PiOver2;
			var chain = Terraria.GameContent.TextureAssets.Chain.Value;
			for (float distance = 0f; distance < length; distance += chain.Height)
			{
				var link = body.Center + direction * distance;
				Main.spriteBatch.Draw(chain, link - Main.screenPosition, null, Lighting.GetColor(link.ToTileCoordinates()), rotation, new Vector2(chain.Width / 2f, chain.Height / 2f), 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
			}
			var head = Terraria.GameContent.TextureAssets.Projectile[type].Value;
			Main.spriteBatch.Draw(head, anchor - Main.screenPosition, null, Lighting.GetColor(anchor.ToTileCoordinates()), rotation, head.Size() / 2f, 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
		}
		return true;
	}

	static readonly GameInterfaceLayer labelLayer = new LegacyGameInterfaceLayer("AutoTerraMod: Bot Labels", DrawLabels, InterfaceScaleType.Game);

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
			string task = bot.Brain.Current == null ? "" : TaskName(bot.Brain.Current.NameKey);
			Utils.DrawBorderString(Main.spriteBatch, task, anchor + new Vector2(0f, 14f), Color.LightGray, 0.6f, 0.5f, 1f);
		}
		return true;
	}

	static readonly Dictionary<string, string> taskNames = new();

	static string TaskName(string nameKey)
	{
		if (!taskNames.TryGetValue(nameKey, out string name))
			taskNames[nameKey] = name = Text($"Tasks.{nameKey}");
		return name;
	}

	public override void OnWorldUnload() => taskNames.Clear();

	const int PanelRefreshFrames = 15;

	static readonly List<(string text, Color color)> lines = new();
	static float panelWidth;
	static int framesUntilRefresh;
	static Bot describedBot;

	static bool DrawPanel()
	{
		if (--framesUntilRefresh <= 0 || describedBot != SpectatorSystem.Followed)
		{
			framesUntilRefresh = PanelRefreshFrames;
			describedBot = SpectatorSystem.Followed;
			RebuildPanel();
		}
		float top = Main.mapEnabled && Main.mapStyle == 1 ? Main.miniMapY + Main.miniMapHeight + MinimapGap : PanelTop;
		var position = new Vector2(Main.screenWidth - PanelMargin - panelWidth, top);
		foreach (var (text, color) in lines)
		{
			Utils.DrawBorderString(Main.spriteBatch, text, position, color, PanelTextScale);
			position.Y += LineHeight;
		}
		return true;
	}

	static void RebuildPanel()
	{
		lines.Clear();
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
		panelWidth = lines.Select(line => Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(line.text).X * PanelTextScale).DefaultIfEmpty(0f).Max();
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
	}

	static string DescribePlan(PlanStep plan) => plan switch
	{
		GatherStep gather => Text("Plans.Gather", Lang.GetItemNameValue(gather.ItemType), gather.Amount),
		CraftStep craft => Text("Plans.Craft", craft.Recipe.createItem.Name),
		PlaceStationStep place => Text("Plans.Place", Lang.GetItemNameValue(place.ItemType)),
		BuildHouseStep build => Text("Plans.Build", Text($"BuildActions.{build.Operation.Action}")),
		WithdrawStep withdraw => Text("Plans.Withdraw", Lang.GetItemNameValue(withdraw.ItemType), withdraw.Amount),
		BuyStep buy => Text("Plans.Buy", Lang.GetItemNameValue(buy.ItemType), buy.Amount, Lang.GetNPCNameValue(buy.SellerType)),
		DeliverStep deliver => Text("Plans.Deliver", Lang.GetItemNameValue(deliver.ItemType), BotWorldSystem.FindBot(deliver.RecipientId)?.Name ?? "?"),
		SummonBossStep summon => Text("Plans.Summon", summon.Challenge.Name),
		ScoutStep scout => Text("Plans.Scout", Text($"Landmarks.{scout.Landmark}")),
		SmashShrineStep => Text("Plans.Smash"),
		SurveyStep => Text("Plans.Survey"),
		TravelStep => Text("Plans.Travel"),
		BuildBridgeStep { Bridge: World.Highway } => Text("Plans.BuildHighway"),
		BuildBridgeStep => Text("Tasks.BuildBridge"),
		HangRopeStep => Text("Plans.HangRope"),
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
				Text($"Roofs.{faction.HouseDesign.Roof}"));
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
