using AutoTerraMod.Common.Bots.Tasks;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public static class BotDiagnostics
{
	static bool Enabled => ModContent.GetInstance<AutoTerraConfig>().DiagnosticLogging;

	public static void Log(Bot bot, string message)
	{
		if (Enabled)
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"[{bot.Faction.Name}/{bot.Name}] {message}");
	}

	public static void LogDecision(Bot bot, BotTask task, PlanStep plan)
	{
		if (Enabled)
			Log(bot, $"task={task?.NameKey ?? "none"} ({task?.Detail}) plan={Describe(plan)} at tile {bot.CenterTile}");
	}

	static string Describe(PlanStep plan) => plan switch
	{
		GatherStep gather => $"Gather {Lang.GetItemNameValue(gather.ItemType)} x{gather.Amount}",
		CraftStep craft => $"Craft {craft.Recipe.createItem.Name}",
		PlaceStationStep place => $"Place {Lang.GetItemNameValue(place.ItemType)}",
		BuildHouseStep build => $"Build {build.Operation.Action} at {build.Operation.Tile}",
		WithdrawStep withdraw => $"Withdraw {Lang.GetItemNameValue(withdraw.ItemType)} x{withdraw.Amount}",
		_ => plan.GetType().Name
	};
}
