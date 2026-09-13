using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class BotEmotes : ModSystem
{
	const int BubbleTicks = 180;
	const uint RallyRepeatTicks = 1200;

	const uint Cooldown = 1800;
	const uint PollTicks = 30;
	const uint GearPollTicks = 300;
	const int StuckSeconds = 30;
	const float AlertRange = 16f * 150f;
	const float EnemyRange = 16f * 20f;

	static readonly Dictionary<int, (int boss, uint shownAt)> rallies = new();
	static readonly Dictionary<int, uint> lastShown = new();
	static readonly Dictionary<int, Snapshot> snapshots = new();
	static readonly Dictionary<int, HashSet<World.Landmark>> knownLandmarks = new();
	static readonly HashSet<int> seenBosses = new();
	static (int invasion, bool blood, bool frost, bool pumpkin) events;

	sealed class Snapshot
	{
		public bool Alive = true;
		public bool Fleeing;
		public bool Stuck;
		public bool NearEnemy;
		public readonly HashSet<int> Debuffs = new();
		public int Weapon = -1;
		public int Pick = -1;
		public HashSet<int> Accessories;
	}

	public override void OnWorldLoad()
	{
		rallies.Clear();
		lastShown.Clear();
		snapshots.Clear();
		knownLandmarks.Clear();
		seenBosses.Clear();
		events = (Main.invasionType, Main.bloodMoon, Main.snowMoon, Main.pumpkinMoon);
	}

	static readonly (int buff, int emote)[] Debuffs =
	{
		(BuffID.OnFire, EmoteID.DebuffBurn), (BuffID.OnFire3, EmoteID.DebuffBurn), (BuffID.Burning, EmoteID.DebuffBurn), (BuffID.Frostburn, EmoteID.DebuffBurn), (BuffID.Frostburn2, EmoteID.DebuffBurn),
		(BuffID.Poisoned, EmoteID.DebuffPoison), (BuffID.Venom, EmoteID.DebuffPoison),
		(BuffID.Cursed, EmoteID.DebuffCurse), (BuffID.CursedInferno, EmoteID.DebuffCurse), (BuffID.Ichor, EmoteID.DebuffCurse),
		(BuffID.Silenced, EmoteID.DebuffSilence)
	};

	static int ForLandmark(World.Landmark landmark) => landmark switch
	{
		World.Landmark.Corruption => EmoteID.BiomeCorruption,
		World.Landmark.Crimson => EmoteID.BiomeCrimson,
		World.Landmark.Jungle => EmoteID.BiomeJungle,
		World.Landmark.Snow => EmoteID.BiomeSnow,
		World.Landmark.Underworld => EmoteID.BiomeLavalayer,
		World.Landmark.Hallow => EmoteID.BiomeHallow,
		World.Landmark.Ocean => EmoteID.BiomeBeach,
		World.Landmark.Dungeon => EmoteID.CritterSkeleton,
		_ => -1
	};

	public static void Casual(Bots.Bot bot, int emote)
	{
		if (lastShown.TryGetValue(bot.Id, out uint at) && Main.GameUpdateCount - at < Cooldown)
			return;
		Show(bot, emote);
	}

	public static void Gift(Bots.Bot donor, Factions.Faction recipient)
	{
		Casual(donor, EmoteID.EmotionLove);
		foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == recipient.Id && bot.IsAlive))
			Casual(bot, EmoteID.EmoteHappiness);
	}

	public static int ForBoss(int npcType) => npcType switch
	{
		NPCID.KingSlime => EmoteID.BossKingSlime,
		NPCID.EyeofCthulhu => EmoteID.BossEoC,
		NPCID.EaterofWorldsHead => EmoteID.BossEoW,
		NPCID.BrainofCthulhu => EmoteID.BossBoC,
		NPCID.QueenBee => EmoteID.BossQueenBee,
		NPCID.SkeletronHead => EmoteID.BossSkeletron,
		NPCID.Deerclops => EmoteID.BossDeerclops,
		NPCID.WallofFlesh => EmoteID.BossWoF,
		NPCID.QueenSlimeBoss => EmoteID.BossQueenSlime,
		NPCID.TheDestroyer => EmoteID.BossDestroyer,
		NPCID.Retinazer or NPCID.Spazmatism => EmoteID.BossTwins,
		NPCID.SkeletronPrime => EmoteID.BossSkeletronPrime,
		NPCID.Plantera => EmoteID.BossPlantera,
		NPCID.Golem => EmoteID.BossGolem,
		NPCID.DukeFishron => EmoteID.BossFishron,
		NPCID.HallowBoss => EmoteID.BossEmpressOfLight,
		NPCID.CultistBoss => EmoteID.BossCultist,
		NPCID.MoonLordCore => EmoteID.BossMoonmoon,
		_ => -1
	};

	public static void Show(Bots.Bot bot, int emote)
	{
		if (emote < 0 || !bot.IsAlive || Main.netMode == NetmodeID.MultiplayerClient)
			return;
		EmoteBubble.NewBubble(emote, new WorldUIAnchor(bot.Npc), BubbleTicks);
		lastShown[bot.Id] = Main.GameUpdateCount;
	}

	public static void BossOver(int bossType, IEnumerable<int> fighters, IReadOnlyCollection<Factions.Faction> credited)
	{
		foreach (int botId in fighters)
		{
			if (BotWorldSystem.FindBot(botId) is not { } bot)
				continue;
			bool won = credited.Any(faction => faction.Id == bot.FactionId);
			Show(bot, !won ? EmoteID.EmotionAnger : Main.rand.NextBool() || ForBoss(bossType) < 0 ? EmoteID.EmotionLove : ForBoss(bossType));
		}
	}

	public override void PostUpdateWorld()
	{
		if (!BotWorldSystem.IsRunning || Main.GameUpdateCount % PollTicks != 0)
			return;
		PollBots();
		PollBosses();
		PollEvents();
		PollLandmarks();
		PollRallies();
	}

	static void PollBots()
	{
		bool gear = Main.GameUpdateCount % GearPollTicks == 0;
		foreach (var bot in BotWorldSystem.Bots)
		{
			if (!snapshots.TryGetValue(bot.Id, out var last))
				snapshots[bot.Id] = last = new Snapshot();
			if (!bot.IsAlive)
			{
				last.Alive = false;
				continue;
			}
			if (!last.Alive)
			{
				last.Alive = true;
				Show(bot, KilledByEnemy(bot) ? EmoteID.EmotionAnger : EmoteID.EmotionCry);
			}
			bool fleeing = bot.Brain.Current is Bots.Tasks.FleeTask;
			if (fleeing && !last.Fleeing)
				Casual(bot, EmoteID.EmoteFear);
			last.Fleeing = fleeing;
			bool stuck = bot.Watchdog.StalledSeconds >= StuckSeconds;
			if (stuck && !last.Stuck)
				Casual(bot, EmoteID.EmoteConfused);
			last.Stuck = stuck;
			bool nearEnemy = BotWorldSystem.Relation == Config.FactionRelation.War
				&& BotWorldSystem.Bots.Any(other => other.FactionId != bot.FactionId && other.IsAlive && Microsoft.Xna.Framework.Vector2.Distance(other.Center, bot.Center) < EnemyRange);
			if (nearEnemy && !last.NearEnemy)
				Casual(bot, Main.rand.NextBool() ? EmoteID.EmoteFight : EmoteID.EmoteKick);
			last.NearEnemy = nearEnemy;
			foreach (var (buff, emote) in Debuffs)
			{
				bool has = bot.Avatar.HasBuff(buff);
				if (has && last.Debuffs.Add(buff))
					Casual(bot, emote);
				else if (!has)
					last.Debuffs.Remove(buff);
			}
			if (gear)
				PollGear(bot, last);
		}
	}

	static void PollGear(Bots.Bot bot, Snapshot last)
	{
		int weapon = bot.Hands.BestWeapon()?.type ?? 0;
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
		var accessories = bot.Avatar.armor.Skip(3).Take(7).Where(item => !item.IsAir).Select(item => item.type).ToHashSet();
		if (last.Weapon >= 0 && weapon != last.Weapon && weapon > 0)
			Casual(bot, EmoteID.ItemSword);
		else if (last.Pick >= 0 && pick > last.Pick)
			Casual(bot, EmoteID.ItemPickaxe);
		else if (last.Accessories != null && accessories.Except(last.Accessories).Any())
			Casual(bot, EmoteID.ItemRing);
		last.Weapon = weapon;
		last.Pick = pick;
		last.Accessories = accessories;
	}

	static bool KilledByEnemy(Bots.Bot bot) =>
		BotWorldSystem.Relation == Config.FactionRelation.War
		&& BotWorldSystem.Bots.Any(other => other.FactionId != bot.FactionId && bot.Vitals.LastDamageSource.Contains(other.Name));

	static void PollBosses()
	{
		seenBosses.RemoveWhere(index => !Main.npc[index].active);
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			var npc = Main.npc[index];
			if (!npc.active || !npc.boss || ForBoss(npc.type) < 0 || !seenBosses.Add(index))
				continue;
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.IsAlive && Microsoft.Xna.Framework.Vector2.Distance(bot.Center, npc.Center) < AlertRange))
				Show(bot, EmoteID.EmotionAlert);
		}
	}

	static void PollEvents()
	{
		var now = (Main.invasionType, Main.bloodMoon, Main.snowMoon, Main.pumpkinMoon);
		int emote = now.invasionType != events.invasion && now.invasionType > 0 ? now.invasionType switch
		{
			InvasionID.GoblinArmy => EmoteID.CritterGoblin,
			InvasionID.PirateInvasion => EmoteID.CritterPirate,
			InvasionID.MartianMadness => EmoteID.CritterMartian,
			InvasionID.SnowLegion => EmoteID.CritterSnowman,
			_ => -1
		}
			: now.bloodMoon && !events.blood ? EmoteID.CritterZombie
			: (now.snowMoon && !events.frost) || (now.pumpkinMoon && !events.pumpkin) ? EmoteID.CritterSnowman
			: -1;
		events = now;
		if (emote < 0)
			return;
		foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.IsAlive))
			Show(bot, emote);
	}

	static void PollLandmarks()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var knowledge = BotWorldSystem.KnowledgeOf(faction);
			var known = System.Enum.GetValues<World.Landmark>().Where(landmark => knowledge.LandmarkOf(landmark) != null).ToHashSet();
			if (knownLandmarks.TryGetValue(faction.Id, out var before))
			{
				foreach (var landmark in known.Except(before))
				{
					var spot = knowledge.LandmarkOf(landmark).Value.ToWorldCoordinates();
					if (BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id && bot.IsAlive).MinBy(bot => Microsoft.Xna.Framework.Vector2.DistanceSquared(bot.Center, spot)) is { } finder)
						Show(finder, ForLandmark(landmark));
				}
			}
			knownLandmarks[faction.Id] = known;
		}
	}

	static void PollRallies()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until || NPC.AnyNPCs(faction.RallyBossType))
			{
				rallies.Remove(faction.Id);
				continue;
			}
			if (rallies.TryGetValue(faction.Id, out var shown) && shown.boss == faction.RallyBossType && Main.GameUpdateCount - shown.shownAt < RallyRepeatTicks)
				continue;
			rallies[faction.Id] = (faction.RallyBossType, Main.GameUpdateCount);
			foreach (var bot in BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id))
				Show(bot, ForBoss(faction.RallyBossType));
		}
	}
}
