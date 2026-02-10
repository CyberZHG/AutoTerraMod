using System.Linq;
using AutoTerraMod.Common.Config;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class SettingsScenario : ModSystem
{
	const uint ChangeAtTick = 20 * 60;
	const uint ReportAtTick = 40 * 60;

	static bool Active => HeadlessTestMode.Scenario == "settings";

	public override void PostUpdateWorld()
	{
		if (!Active || !BotWorldSystem.IsRunning || BotWorldSystem.Settings is not WorldFactionSettings settings)
			return;
		if (Main.GameUpdateCount == 60)
			Report("loaded");
		if (Main.GameUpdateCount == ChangeAtTick && settings.FactionCount < WorldFactionSettings.MaxFactions)
		{
			BotWorldSystem.ChangeSettings(settings with { FactionCount = settings.FactionCount + 1, Relation = FactionRelation.Cooperate });
			Report("changed");
		}
		if (Main.GameUpdateCount == ReportAtTick)
			Report("later");
	}

	static void Report(string stage)
	{
		var settings = BotWorldSystem.Settings;
		string factions = string.Join(", ", BotWorldSystem.Factions.Select(faction =>
			$"{faction.Name}@{faction.BaseTile.X}: {BotWorldSystem.Bots.Count(bot => bot.FactionId == faction.Id)} bots, {BotWorldSystem.Bots.Count(bot => bot.FactionId == faction.Id && bot.IsAlive)} alive"));
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"settings scenario {stage}: {settings} relation={BotWorldSystem.Relation} [{factions}]");
	}
}
