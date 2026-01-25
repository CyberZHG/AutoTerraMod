using System.Linq;
using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class FishingScenario : ModSystem
{
	const int ReportIntervalTicks = 3600;

	static bool Active => HeadlessTestMode.Scenario == "fishing";

	public static void Prepare()
	{
		if (!Active)
			return;
		foreach (var bot in BotWorldSystem.Bots)
		{
			bot.Personality.Aspiration = Aspiration.Angler;
			HeadlessTestMode.Give(bot, ItemID.ReinforcedFishingPole, 1);
			HeadlessTestMode.Give(bot, ItemID.MasterBait, 30);
		}
		foreach (var faction in BotWorldSystem.Factions)
		{
			var knowledge = BotWorldSystem.KnowledgeOf(faction);
			for (int x = 40; x < Main.maxTilesX - 40; x++)
			{
				int y = World.TileQuery.SurfaceY(x);
				if (WorldGen.oceanDepths(x, y) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Sand && (x < Main.maxTilesX / 2) == (faction.BaseTile.X < Main.maxTilesX / 2))
					knowledge.RememberLandmark(World.Landmark.Ocean, new Microsoft.Xna.Framework.Point(x, y));
			}
		}
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (Bosses.BossRituals.Shoreline(bot) is Microsoft.Xna.Framework.Vector2 shore)
				bot.SavedFeetPosition = shore;
		}
		NPC.savedAngler = true;
		if (!NPC.AnyNPCs(NPCID.Angler))
			NPC.NewNPC(new EntitySource_WorldEvent(), Main.spawnTileX * 16, Main.spawnTileY * 16, NPCID.Angler);
		var first = BotWorldSystem.Bots.FirstOrDefault();
		if (first != null)
		{
			Main.anglerWhoFinishedToday.Remove(first.Avatar.name);
			HeadlessTestMode.Give(first, Main.anglerQuestItemNetIDs[Main.anglerQuest], 1);
		}
		Main.dayTime = true;
		Main.time = 0;
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"fishing scenario prepared: every bot is an angler with a pole and bait; {first?.Name} holds today's quest fish {Lang.GetItemNameValue(Main.anglerQuestItemNetIDs[Main.anglerQuest])}");
	}

	public override void PostUpdateWorld()
	{
		if (!Active || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"fishing report: angler present={NPC.AnyNPCs(NPCID.Angler)}");
		var catches = BotWorldSystem.Bots.Select(bot => $"{bot.Name}: quests={bot.Avatar.anglerQuestsFinished} bait={Inventory.Items(bot.Avatar).Where(item => item.bait > 0).Sum(item => item.stack)}");
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"fishing report: {string.Join(", ", catches)}");
	}
}
