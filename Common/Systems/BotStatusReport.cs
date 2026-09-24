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
		var report = new StringBuilder($"status (day={Main.dayTime}, time={(int)Main.time}, bosses={WorldBosses.DefeatedCount}/{WorldBosses.InProgressionOrder.Count}, next={WorldBosses.NextUndefeated?.Name}, invasion={Main.invasionType}/{Main.invasionSize}, danger={NPC.AnyDanger()}, cultistDelay={(int)Terraria.GameContent.Events.CultistRitual.delay}, ritual={DescribeRitualSite()}, shields=S{NPC.ShieldStrengthTowerSolar}/V{NPC.ShieldStrengthTowerVortex}/N{NPC.ShieldStrengthTowerNebula}/D{NPC.ShieldStrengthTowerStardust}{DescribePillars()}):");
		foreach (var faction in BotWorldSystem.Factions)
		{
			var settlement = faction.Settlement;
			int built = settlement?.Structures.Count(structure => Building.HouseInspector.IsComplete(faction, structure)) ?? 0;
			report.Append($"\n  [{faction.Name}] base={faction.BaseTile} buildings={built}/{settlement?.Structures.Count ?? 0} chests={Building.BaseStorage.ChestIndices(faction).Count} known={BotWorldSystem.KnowledgeOf(faction).ResourceCount}{DescribeConstruction(faction)}{DescribeBossReadiness(faction) + DescribeBridge(faction)}");
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				report.Append($"\n    {Describe(bot)}");
		}
		report.Append("\n  pylons:");
		foreach (var pylon in Main.PylonSystem.Pylons)
			report.Append($" {pylon.TypeOfPylon}@{pylon.PositionInTiles} biome={Bots.PylonTravel.MeetsBiome(pylon.TypeOfPylon, pylon.PositionInTiles)} npcs={Bots.PylonTravel.HasEnoughNpcs(pylon)} tidy={DescribePylonTidy(pylon)};");
		Mod.Logger.Info(report.ToString());
	}

	static string DescribePylonTidy(Terraria.GameContent.TeleportPylonInfo pylon)
	{
		var owner = BotWorldSystem.Factions.Where(faction => faction.Settlement != null).MinBy(faction => System.Math.Abs(faction.BaseTile.X - pylon.PositionInTiles.X));
		if (owner == null)
			return "none";
		var settlement = owner.Settlement;
		int x = pylon.PositionInTiles.X, y = pylon.PositionInTiles.Y;
		return $"{owner.Name} misplaced={Planning.Stations.IsMisplaced(owner, x, y)} protected={Building.HouseInspector.IsProtected(x, y)} camp={settlement.Left}..{settlement.Right}/{settlement.Top}..{settlement.Floor}";
	}

	static string Column(World.Hellbridge bridge, int x) =>
		string.Join(",", Enumerable.Range(bridge.Y - 2, 4).Select(y => $"{World.TileQuery.TypeAt(new Microsoft.Xna.Framework.Point(x, y))}{(Main.tile[x, y].LiquidAmount > 0 ? "~" : "")}"));

	static string DescribeBridge(Factions.Faction faction)
	{
		var shaft = faction.Hellevator;
		string shaftText = shaft == null ? "" : $"\n    hellevator x={shaft.X}: dug to {shaft.FirstImpassableRow()} (tidy to {shaft.FirstUnfinishedRow()}) of {Main.UnderworldLayer}, rope missing at {Bots.Tasks.RopeColumnTask.MissingRow(shaft.RopeX, shaft.RopeTop, shaft.RopeBottom())?.ToString() ?? "none"}";
		foreach (var highway in World.Highway.For(faction))
			shaftText += $"\n    highway {(highway.Direction > 0 ? "east" : "west")} y={highway.Y} {highway.StartX}->{highway.EndX}: frontier {highway.Frontier(highway.Direction)}{(highway.BlockReason is string reason ? " blocked by " + reason : "")}, link row missing {highway.MissingLinkRow()?.ToString() ?? "none"}";
		if (World.SkyArena.Wanted && World.SkyArena.For(faction) is { } arena)
			shaftText += $"\n    sky arena x={arena.CenterX} deck={arena.DeckRow}: rope missing at {Bots.Tasks.RopeColumnTask.MissingRow(arena.CenterX, arena.DeckRow, arena.Surface - 1)?.ToString() ?? "none"}, next gap {arena.NextGap()?.ToString() ?? "none"}, usable={arena.Usable}";
		var bridge = faction.Hellbridge;
		return shaftText + (bridge == null ? "" : $"\n    hellbridge y={bridge.Y}: built {bridge.Frontier(-1)}..{bridge.Frontier(1)} of {bridge.LeftEnd}..{bridge.RightEnd} ends [{Column(bridge, bridge.Frontier(-1))}] [{Column(bridge, bridge.Frontier(1))}]");
	}

	static string DescribeBossReadiness(Factions.Faction faction)
	{
		if (Bosses.BossCredit.CurrentFight(faction) is { } fight)
			return $"\n    fighting {fight.Name}: life={Bosses.BossCredit.LifePercent(fight)?.ToString() ?? "?"}% damage=[{Bosses.BossCredit.DescribeFight(fight)}]";
		var challenge = Bosses.BossCredit.NextChallengeFor(faction);
		var summoner = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id && bot.IsAlive).MaxBy(bot => bot.Personality.Combat);
		if (challenge == null || summoner == null)
			return "";
		var report = Bosses.BossReadiness.Evaluate(faction, challenge, summoner);
		var site = challenge.Arena ?? challenge.DistantArena;
		string siteText = site is World.Landmark landmark ? $" site={landmark}:{(BotWorldSystem.KnowledgeOf(faction).LandmarkOf(landmark) != null ? "found" : "unknown")}" : " site=camp";
		string rallyText = faction.Rally is { } rally && Main.GameUpdateCount < rally.until
			? $" rally={Lang.GetNPCNameValue(faction.RallyBossType)}@{rally.point.ToTileCoordinates()} assembled={faction.RallyAssembled} leader={BotWorldSystem.FindBot(faction.RallyLeaderId)?.Name} building={faction.RallyBuilding}"
			: "";
		return $"\n    next boss {challenge.Name}: kill~{report.KillSeconds:0}s survive~{report.SurviveSeconds:0}s need x{report.RequiredRatio:0.0} ready={report.IsReady}{siteText}{rallyText}";
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
		var unfinished = settlement.Structures.Where(candidate => !Building.HouseInspector.IsFinished(faction, candidate))
			.Select(candidate => $"#{candidate.Index}{(Building.HouseInspector.IsComplete(faction, candidate) ? "" : " room-check-failed " + Building.HouseInspector.RoomCheckFailure(candidate))} next={Building.HouseInspector.NextOperation(faction, candidate)?.ToString() ?? "none"}");
		return $"\n    building {structure.Purpose}#{structure.Index}: next={next?.Action} needs=[{needs}]\n    unfinished: {string.Join("; ", unfinished)}";
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

	static string DescribePillars() =>
		string.Concat(Main.npc.Where(npc => npc.active && Bosses.BossRituals.Pillars.Contains(npc.type)).Select(npc => $" {Lang.GetNPCNameValue(npc.type)}@{(int)(npc.Center.X / 16)}={npc.life}/{npc.lifeMax}"));

	static string DescribeRitualSite()
	{
		int x = Main.dungeonX, y = Main.dungeonY;
		if (x <= 0 || y < 8)
			return "none";
		bool blockedAbove = WorldGen.SolidTile(Main.tile[x, y - 7]);
		bool floor = Terraria.GameContent.Events.CultistRitual.CheckFloor(new Microsoft.Xna.Framework.Vector2(x * 16 + 8, y * 16 - 64 - 8 - 27), out _);
		return $"{x},{y} above={(blockedAbove ? "solid" : "open")} floor={floor} los={WorldGen.PlayerLOS(x - 6, y) || WorldGen.PlayerLOS(x + 6, y)}";
	}
}
