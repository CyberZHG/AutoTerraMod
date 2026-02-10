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
		if (!Bots.BotDiagnostics.Enabled)
			return;
		var report = new StringBuilder($"status (day={Main.dayTime}, time={(int)Main.time}, bosses={WorldBosses.DefeatedCount}/{WorldBosses.InProgressionOrder.Count}, next={WorldBosses.NextUndefeated?.Name}):");
		foreach (var faction in BotWorldSystem.Factions)
		{
			var settlement = faction.Settlement;
			int built = settlement?.Structures.Count(structure => Building.HouseInspector.IsComplete(faction, structure)) ?? 0;
			report.Append($"\n  [{faction.Name}] base={faction.BaseTile} buildings={built}/{settlement?.Structures.Count ?? 0} chests={Building.BaseStorage.ChestIndices(faction).Count}{DescribeConstruction(faction)}{DescribeBossReadiness(faction) + DescribeBridge(faction)}");
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				report.Append($"\n    {Describe(bot)}");
		}
		Mod.Logger.Info(report.ToString());
	}

	static string DescribeBridge(Factions.Faction faction)
	{
		var shaft = faction.Hellevator;
		string shaftText = shaft == null ? "" : $"\n    hellevator x={shaft.X}: dug to {shaft.FirstUnfinishedRow()} of {Main.UnderworldLayer}";
		var bridge = faction.Hellbridge;
		return shaftText + (bridge == null ? "" : $"\n    hellbridge y={bridge.Y}: built {bridge.Frontier(-1)}..{bridge.Frontier(1)} of {bridge.LeftEnd}..{bridge.RightEnd}");
	}

	static string DescribeBossReadiness(Factions.Faction faction)
	{
		var challenge = Bosses.BossCredit.NextChallengeFor(faction);
		var summoner = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id && bot.IsAlive).MaxBy(bot => bot.Personality.Combat);
		if (challenge == null || summoner == null)
			return "";
		var report = Bosses.BossReadiness.Evaluate(faction, challenge, summoner);
		return $"\n    next boss {challenge.Name}: kill~{report.KillSeconds:0}s survive~{report.SurviveSeconds:0}s need x{report.RequiredRatio:0.0} ready={report.IsReady}";
	}

	static string DescribeConstruction(Factions.Faction faction)
	{
		var settlement = faction.Settlement;
		var structure = settlement?.Structures.FirstOrDefault(candidate => !Building.HouseInspector.IsFinished(faction, candidate));
		if (structure == null)
			return "";
		var materials = Building.HouseInspector.MaterialsFor(faction);
		var bill = Building.ConstructionBill.For(structure, materials);
		string needs = string.Join(", ", bill.Select(line => $"{Lang.GetItemNameValue(line.ItemType)} x{line.Amount}"));
		var next = Building.HouseInspector.NextOperation(faction, structure);
		return $"\n    building {structure.Purpose}#{structure.Index}: next={next?.Action} needs=[{needs}]";
	}

	static string Describe(Bot bot)
	{
		if (!bot.IsAlive)
			return $"{bot.Name}: dead, respawn in {bot.RespawnTicks / 60}s";
		var avatar = bot.Avatar;
		var task = bot.Brain.Current;
		return $"{bot.Name}: at {bot.CenterTile} hp={avatar.statLife}/{avatar.statLifeMax2} slots={Inventory.UsedSlots(avatar)} " +
			$"pick={bot.Hands.BestPickaxe?.pick ?? 0} task={task?.NameKey ?? "none"}({task?.Detail}) plan={DescribePlan(bot.Brain.Plan)} goal={bot.Brain.GoalName} stalled={bot.Watchdog.StalledSeconds}s";
	}

	static string DescribePlan(PlanStep plan) => plan switch
	{
		GatherStep gather => $"gather {Lang.GetItemNameValue(gather.ItemType)} x{gather.Amount}",
		CraftStep craft => $"craft {craft.Recipe.createItem.Name}",
		PlaceStationStep place => $"place {Lang.GetItemNameValue(place.ItemType)}",
		BuildHouseStep build => $"build {build.Operation.Action} {build.Operation.Tile} in #{build.Operation.Structure}",
		WithdrawStep withdraw => $"withdraw {Lang.GetItemNameValue(withdraw.ItemType)} x{withdraw.Amount}",
		DeliverStep deliver => $"deliver {Lang.GetItemNameValue(deliver.ItemType)} to #{deliver.RecipientId}",
		_ => plan.GetType().Name
	};
}
