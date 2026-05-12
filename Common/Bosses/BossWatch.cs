using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bosses;

public sealed class BossWatch : ModSystem
{
	readonly record struct Sighting(int Type, int Life, int LifeMax, int TimeLeft, int Target, Vector2 Center, string Ai, string LocalAi);

	readonly Dictionary<int, Sighting> sightings = new();

	public override void OnWorldUnload() => sightings.Clear();

	const int ProgressLogTicks = 1800;
	const int DespawnTimeLeft = 1;

	public override void PostUpdateNPCs()
	{
		if (!BotWorldSystem.IsRunning)
			return;
		if (Main.GameUpdateCount % ProgressLogTicks == 0)
			LogFightProgress();
		if (Main.GameUpdateCount % BulbCensusTicks == 0 && !Main.npc.Any(npc => npc.active && npc.boss))
			BossCredit.FloorDefeatedCalibrations();
		if (Main.GameUpdateCount % BulbCensusTicks == 0 && NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3 && !NPC.downedPlantBoss)
			LogBulbs();
		foreach (var (slot, last) in sightings.ToList())
		{
			var npc = Main.npc[slot];
			if (npc.active && npc.type == last.Type)
				continue;
			sightings.Remove(slot);
			if (BossCatalog.ChallengeOf(last.Type) is BossChallenge challenge)
				BossCredit.ForgetIfOver(challenge);
			string outcome = last.TimeLeft > DespawnTimeLeft && Defeated(last.Type) ? "defeated" : "despawned";
			BotDiagnostics.LogWorld($"boss {Lang.GetNPCNameValue(last.Type)} {outcome}: life={last.Life}/{last.LifeMax} timeLeft={last.TimeLeft} target={DescribeTarget(last)} lastTile={last.Center.ToTileCoordinates()} nowTile={npc.Center.ToTileCoordinates()} ai={last.Ai} local={last.LocalAi}");
		}
		foreach (var npc in Main.npc)
		{
			if (!npc.active || !(npc.boss || BossCatalog.ChallengeOf(npc.type)?.BossType == npc.type))
				continue;
			if (!sightings.ContainsKey(npc.whoAmI))
				BotDiagnostics.LogWorld($"boss {npc.FullName} appeared at tile {npc.Center.ToTileCoordinates()} life={npc.lifeMax}");
			sightings[npc.whoAmI] = new Sighting(npc.type, npc.life, npc.lifeMax, npc.timeLeft, npc.target, npc.Center, string.Join(",", npc.ai), string.Join(",", npc.localAI));
		}
	}

	const int BulbCensusTicks = 5 * 3600;

	static void LogBulbs()
	{
		var bulbs = new List<Point>();
		for (int x = 10; x < Main.maxTilesX - 10; x++)
		{
			for (int y = (int)Main.worldSurface; y < Main.UnderworldLayer; y++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && tile.TileType == Terraria.ID.TileID.PlanteraBulb && tile.TileFrameX % 36 == 0 && tile.TileFrameY == 0)
					bulbs.Add(new Point(x, y));
			}
		}
		var known = BotWorldSystem.Factions.Select(faction => $"{faction.Name} knows {bulbs.Count(bulb => KnowsBulb(BotWorldSystem.KnowledgeOf(faction), bulb))}");
		BotDiagnostics.LogWorld($"plantera bulbs in the world: {bulbs.Count} [{string.Join(" ", bulbs.Take(8))}]; {string.Join(", ", known)}");
	}

	static bool KnowsBulb(Factions.FactionKnowledge knowledge, Point bulb) =>
		Enumerable.Range(0, 4).Any(part => knowledge.Knows(new Point(bulb.X + part % 2, bulb.Y + part / 2), Terraria.ID.TileID.PlanteraBulb));

	static void LogFightProgress()
	{
		foreach (var challenge in BossCatalog.Challenges.Where(BossCatalog.IsBossAlive))
		{
			BossCredit.CheckCalibration(challenge, over: false);
			var parts = Main.npc.Where(npc => npc.active && challenge.IsPart(npc.type)).ToList();
			BotDiagnostics.LogWorld($"boss fight {challenge.Name}: parts [{string.Join(", ", parts.Select(npc => $"{npc.TypeName} {npc.life}{(npc.dontTakeDamage ? " shielded" : "")}"))}], life left {parts.Where(npc => npc.realLife < 0 || npc.realLife == npc.whoAmI).Sum(npc => npc.life)} at {parts.FirstOrDefault()?.Center.ToTileCoordinates()}; damage {BossCredit.DescribeFight(challenge)}");
		}
	}

	static bool Defeated(int type) => BossCatalog.ChallengeOf(type)?.IsDefeated() == true;

	static string DescribeTarget(Sighting sighting)
	{
		if (sighting.Target < 0 || sighting.Target >= Main.maxPlayers + 1)
			return $"#{sighting.Target}";
		var bot = BotWorldSystem.Bots.FirstOrDefault(candidate => candidate.Avatar.whoAmI == sighting.Target);
		var player = Main.player[sighting.Target];
		string name = bot != null ? $"{bot.Faction.Name}/{bot.Name} alive={bot.IsAlive}" : $"player {player.name} active={player.active}";
		float distance = bot != null ? Vector2.Distance(bot.Center, sighting.Center) : Vector2.Distance(player.Center, sighting.Center);
		return $"#{sighting.Target} {name} distance={distance / 16f:0} tiles";
	}
}
