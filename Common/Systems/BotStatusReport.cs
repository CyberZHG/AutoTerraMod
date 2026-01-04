using System.Linq;
using System.Text;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Planning;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class BotStatusReport : ModSystem
{
	const int ReportIntervalTicks = 1800;

	int ticksSinceReport;

	public override void PostUpdateEverything()
	{
		if (!BotWorldSystem.IsRunning || ++ticksSinceReport < ReportIntervalTicks)
			return;
		ticksSinceReport = 0;
		if (!ModContent.GetInstance<AutoTerraConfig>().DiagnosticLogging)
			return;
		var report = new StringBuilder($"status (day={Main.dayTime}, time={(int)Main.time}):");
		foreach (var faction in BotWorldSystem.Factions)
		{
			var settlement = faction.Settlement;
			int built = settlement?.Structures.Count(structure => Building.HouseInspector.IsComplete(faction, structure)) ?? 0;
			report.Append($"\n  [{faction.Name}] base={faction.BaseTile} buildings={built}/{settlement?.Structures.Count ?? 0} chests={Building.BaseStorage.ChestIndices(faction).Count}");
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				report.Append($"\n    {Describe(bot)}");
		}
		Mod.Logger.Info(report.ToString());
	}

	static string Describe(Bot bot)
	{
		if (!bot.IsAlive)
			return $"{bot.Name}: dead, respawn in {bot.RespawnTicks / 60}s";
		var avatar = bot.Avatar;
		var task = bot.Brain.Current;
		return $"{bot.Name}: at {bot.CenterTile} hp={avatar.statLife}/{avatar.statLifeMax2} slots={Inventory.UsedSlots(avatar)} " +
			$"pick={bot.Hands.BestPickaxe?.pick ?? 0} task={task?.NameKey ?? "none"}({task?.Detail}) plan={DescribePlan(bot.Brain.Plan)} stalled={bot.Watchdog.StalledSeconds}s";
	}

	static string DescribePlan(PlanStep plan) => plan switch
	{
		GatherStep gather => $"gather {Lang.GetItemNameValue(gather.ItemType)} x{gather.Amount}",
		CraftStep craft => $"craft {craft.Recipe.createItem.Name}",
		PlaceStationStep place => $"place {Lang.GetItemNameValue(place.ItemType)}",
		BuildHouseStep build => $"build {build.Operation.Action} {build.Operation.Tile} in #{build.Operation.Structure}",
		WithdrawStep withdraw => $"withdraw {Lang.GetItemNameValue(withdraw.ItemType)} x{withdraw.Amount}",
		_ => plan.GetType().Name
	};
}
