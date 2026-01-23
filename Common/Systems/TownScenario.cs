using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class TownScenario : ModSystem
{
	const int CaptiveOffset = 15;
	const int ReportIntervalTicks = 3600;

	static bool Active => HeadlessTestMode.Scenario == "town";

	public static void Prepare()
	{
		if (!Active)
			return;
		SpawnCaptives();
		foreach (var bot in BotWorldSystem.Bots)
			HeadlessTestMode.Give(bot, ItemID.GoldCoin, 30);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"town scenario prepared: a bound Goblin Tinkerer waits near each base; bots carry 30 gold; savedGoblin={NPC.savedGoblin}");
	}

	static void SpawnCaptives()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			int x = faction.BaseTile.X + CaptiveOffset;
			if (World.Grounds.StandingSpotNear(new Point(x, World.TileQuery.SurfaceY(x) - 1)) is Point feet)
			{
				int index = NPC.NewNPC(new EntitySource_WorldEvent(), feet.X * 16 + 8, (feet.Y + 1) * 16, NPCID.BoundGoblin);
				ModContent.GetInstance<AutoTerraMod>().Logger.Info($"town scenario: bound goblin #{index} at {feet} for {faction.Name}");
			}
		}
	}

	public override void PostUpdateWorld()
	{
		if (!Active || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		if (!NPC.savedGoblin && !NPC.AnyNPCs(NPCID.BoundGoblin) && !NPC.AnyNPCs(NPCID.GoblinTinkerer))
			SpawnCaptives();
		var captives = Main.npc.Where(npc => npc.active && Bots.Tasks.RescueTask.RescuedForms.ContainsKey(npc.type)).Select(npc => $"{npc.TypeName} at {npc.Center.ToTileCoordinates()}");
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"town captives: [{string.Join(", ", captives)}]");
		var towns = Main.npc.Where(npc => npc.active && npc.townNPC).Select(npc => $"{npc.TypeName}{(npc.homeless ? " (homeless)" : $" home {npc.homeTileX},{npc.homeTileY}")}");
		var workshops = BotWorldSystem.Factions.Select(faction => $"{faction.Name} workshop={Planning.Stations.BaseHas(faction, TileID.TinkerersWorkbench)}");
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"town report: [{string.Join(", ", towns)}] {string.Join(", ", workshops)}");
	}
}
