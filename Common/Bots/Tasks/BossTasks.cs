using System;
using System.Linq;
using AutoTerraMod.Common.Bosses;
using AutoTerraMod.Common.Navigation;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class SummonTask : BotTask
{
	const int RallyTicks = 2 * 3600;
	const float SummonPriority = 93f;
	const int AssembleTicks = 3 * 3600;
	const int TravelRetries = 3;
	const int RallyCooldownTicks = 2 * 3600;
	const float GatherRadius = 16f * 20f;
	const float BelowCampTolerance = 16f * 3f;
	const float ArrivalRadius = 16f * 10f;
	const float StuckAcceptRadius = 16f * 25f;
	const float CampRecallReach = 16f * 25f;
	const float DistantArenaReach = 16f * 100f;
	const int DistantRallyTicks = 5 * 3600;
	const float OldManSearchRadius = 16f * 150f;
	const float TalkRadius = 16f * 4f;
	const float ThrowRadius = 16f * 8f;
	const int LavaSearchRadius = 60;
	const int WallSpawnDepth = 203;
	const int DollGrabDelay = 300;
	const int FirstAccessorySlot = 3;
	const int AccessorySlots = 7;

	SummonBossStep step;
	int travelFailures;

	const float WallGatherInset = 16f * 40f;

	Vector2 GatherPoint => step.Trigger is VoodooDollTrigger
		? step.Place + new Vector2(step.Place.X / 16f > Main.maxTilesX / 2 ? -WallGatherInset : WallGatherInset, 0f)
		: step.Place;
	string headcount;

	public SummonTask(Bot bot) : base(bot) { }

	public override string NameKey => "Summon";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SummonBossStep summon)
			return 0f;
		step = summon;
		Detail = Bot.Faction.RallyAssembled ? summon.Challenge.Name : headcount ?? summon.Challenge.Name;
		return SummonPriority;
	}

	public override void Start()
	{
		base.Start();
		travelFailures = 0;
		lacewing = null;
		extensions = 0;
		headcount = null;
		var faction = Bot.Faction;
		if (faction.Rally is { } rally && Main.GameUpdateCount < rally.until && faction.RallyLeaderId == Bot.Id)
			return;
		faction.Rally = (GatherPoint, Main.GameUpdateCount + AssembleTicks);
		faction.RallyBossType = step.Challenge.BossType;
		faction.RallyLeaderId = Bot.Id;
		faction.RallyLeaderSeenAt = Main.GameUpdateCount;
		faction.RallyAssembled = false;
	}

	public override void Tick()
	{
		if (!step.Challenge.WindowStillOpen || BossCatalog.IsBossAlive(step.Challenge))
		{
			if (Bot.Faction.RallyLeaderId == Bot.Id)
				Bot.Faction.Rally = null;
			IsFinished = true;
			return;
		}
		if (!Assembled())
			return;
		if (!ReachedPlace())
			return;
		if (!ApproachRitual(out var ritualTarget))
			return;
		if (!step.Challenge.CanSummonNow)
		{
			Bot.Motor.Stop();
			Bot.Faction.Rally = (GatherPoint, Main.GameUpdateCount + RallyTicks);
			Bot.Watchdog.RecordProgress();
			return;
		}
		if (!TeamGathered())
			return;
		if (AwaitingArenaOutpost())
			return;
		if (!Perform(ritualTarget))
			return;
		Bot.Faction.Rally = null;
		Bot.Faction.ArenaPoint = step.Place;
		Bot.Watchdog.RecordProgress();
		BossCredit.RecordSummoner(step.Challenge, Bot.FactionId, BossReadiness.Evaluate(Bot.Faction, step.Challenge, Bot).TeamDps);
		BotDiagnostics.Log(Bot, $"summoned {step.Challenge.Name} via {step.Trigger.GetType().Name}");
		IsFinished = true;
	}

	const uint ArenaOutpostWaitTicks = 5 * 3600;
	uint arenaWaitStarted;

	bool AwaitingArenaOutpost()
	{
		var faction = Bot.Faction;
		if (!Building.Outpost.IsDistantArena(faction, step.Place) || Building.Outpost.ArenaBedFor(faction) != null)
			return false;
		if (arenaWaitStarted == 0 || Main.GameUpdateCount - arenaWaitStarted > ArenaOutpostWaitTicks * 2)
		{
			arenaWaitStarted = Main.GameUpdateCount;
			BotDiagnostics.Log(Bot, $"waiting for an arena outpost before summoning {step.Challenge.Name}");
		}
		if (Main.GameUpdateCount - arenaWaitStarted > ArenaOutpostWaitTicks)
			return false;
		Bot.Motor.Stop();
		faction.Rally = (GatherPoint, Main.GameUpdateCount + RallyTicks);
		Bot.Watchdog.RecordProgress();
		return true;
	}

	bool Assembled()
	{
		var faction = Bot.Faction;
		if (faction.RallyAssembled)
			return true;
		Bot.Watchdog.RecordProgress();
		if (Vector2.Distance(step.Place, faction.BaseCenter) > DistantArenaReach)
		{
			faction.RallyAssembled = true;
			faction.Rally = (GatherPoint, Main.GameUpdateCount + DistantRallyTicks);
			BotDiagnostics.Log(Bot, $"calling the team to {step.Place.ToTileCoordinates()} for {step.Challenge.Name}");
			return true;
		}
		LeaveOutpostIfBedside();
		bool awayFromCamp = BelowOwnCamp() && Bot.Center.Y > faction.BaseCenter.Y + BelowCampTolerance
			|| Vector2.Distance(Bot.Center, step.Place) > ArrivalRadius && Vector2.Distance(Bot.Center, faction.BaseCenter) > GatherRadius;
		if (awayFromCamp && Vector2.Distance(Bot.Center, faction.BaseCenter) > Vector2.Distance(CampLanding, faction.BaseCenter) + CampRecallReach && Main.GameUpdateCount - Bot.LastRecallAt > RecallCooldownTicks && CarriesRecall(Avatar) && (RecallLandsAtBase() || Bot.OutpostBed != null) && TryRecallToCamp())
			return false;
		if (!awayFromCamp || !TravelOrTunnel(faction.BaseFeet - new Vector2(0f, 16f * 2f), 16f))
			Bot.Motor.Stop();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId).ToList();
		var gathered = team.Where(member => RallyTask.Joined(member, Bot.Center, GatherRadius)).ToList();
		int present = gathered.Count;
		headcount = $"{step.Challenge.Name} {present}/{team.Count}";
		bool waiting = faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		bool strongEnough = present < team.Count && BossReadiness.Evaluate(faction, step.Challenge, Bot, gathered).IsReady;
		if (present < team.Count && waiting && !strongEnough)
			return false;
		if (present * 2 < team.Count && !strongEnough)
			return GiveUpRally(present, team.Count);
		faction.RallyAssembled = true;
		faction.Rally = (GatherPoint, Main.GameUpdateCount + RallyTicks);
		BotDiagnostics.Log(Bot, $"set out for {step.Challenge.Name} with {present}/{team.Count} teammates");
		return true;
	}

	bool GiveUpRally(int present, int teamSize)
	{
		Bot.Faction.Rally = null;
		Bot.Faction.RallyCooldownUntil = Main.GameUpdateCount + RallyCooldownTicks;
		return Abandon($"only {present}/{teamSize} teammates made it to the rally");
	}

	const float UnderworldAcceptRadius = 16f * 60f;

	bool ReachedPlace()
	{
		float distance = Vector2.Distance(Bot.Center, step.Place);
		bool noWayCloser = Bot.Motor.IsStalled || Bot.Motor.Tunneling || Bot.Motor.HasNoRoute;
		bool inUnderworld = Bot.CenterTile.Y >= Main.UnderworldLayer && step.Place.Y >= Main.UnderworldLayer * 16f;
		if (distance <= ArrivalRadius || noWayCloser && distance <= StuckAcceptRadius || inUnderworld && distance <= UnderworldAcceptRadius)
			return true;
		Bot.Faction.Rally = (GatherPoint, Main.GameUpdateCount + (uint)(Vector2.Distance(step.Place, Bot.Faction.BaseCenter) > DistantArenaReach ? DistantRallyTicks : RallyTicks));
		if (TravelOrTunnel(step.Place, ArrivalRadius * 0.5f))
			return false;
		if (++travelFailures >= TravelRetries)
			return Abandon($"cannot reach {step.Place.ToTileCoordinates()}");
		Bot.Motor.Stop();
		return false;
	}

	bool ApproachRitual(out Vector2 ritualTarget)
	{
		ritualTarget = step.Place;
		switch (step.Trigger)
		{
			case SummonItemTrigger item when item.CanUseHere != null && !item.CanUseHere(Avatar):
				return Vector2.Distance(Bot.Center, step.Place) < 32f ? Abandon("not inside the required biome") : MoveCloser(step.Place, 16f);
			case SmashOrbsTrigger when step.Shrine is Point shrine:
				ritualTarget = shrine.ToWorldCoordinates();
				var hammer = Bot.Hands.BestHammer;
				return hammer != null && (Bot.Hands.CanReach(hammer, shrine) || MoveCloser(ritualTarget, 24f));
			case SmashLarvaTrigger when step.Shrine is Point larva:
				ritualTarget = larva.ToWorldCoordinates();
				var blade = Bot.Hands.BestWeapon();
				return blade != null && (Bot.Hands.CanReach(blade, larva) || MoveCloser(ritualTarget, 24f));
			case CurseOldManTrigger:
				var oldMan = Main.npc.FirstOrDefault(npc => npc.active && npc.type == NPCID.OldMan && Vector2.Distance(npc.Center, step.Place) < OldManSearchRadius);
				if (oldMan == null)
					return Abandon("the Old Man is not near the dungeon entrance");
				ritualTarget = oldMan.Center;
				return Vector2.Distance(Bot.Center, oldMan.Center) <= TalkRadius || MoveCloser(oldMan.Center, TalkRadius * 0.5f);
			case ClothierDollTrigger:
				int clothierIndex = NPC.FindFirstNPC(NPCID.Clothier);
				if (clothierIndex < 0)
					return Abandon("the Clothier is gone");
				var clothier = Main.npc[clothierIndex];
				ritualTarget = clothier.Center;
				var weapon = Bot.Hands.BestWeapon();
				return weapon != null && (Bot.Hands.InMeleeReach(clothier, weapon) || MoveCloser(clothier.Center, 16f));
			case AltarTrigger when step.Shrine is Point altar:
				ritualTarget = altar.ToWorldCoordinates();
				return Vector2.Distance(Bot.Center, ritualTarget) <= TalkRadius || MoveCloser(ritualTarget, TalkRadius * 0.5f);
			case BulbTrigger when step.Shrine is Point bulb:
				ritualTarget = bulb.ToWorldCoordinates();
				var pickaxe = Bot.Hands.BestPickaxe;
				return pickaxe != null && (Bot.Hands.CanReach(pickaxe, bulb) || MoveCloser(ritualTarget, 24f));
			case BaitTrigger:
				var water = WaterFinder.NearestWaterSurface(Bot.CenterTile, LavaSearchRadius);
				if (water is not Point sea)
					return Abandon($"no water found near {Bot.CenterTile}");
				ritualTarget = sea.ToWorldCoordinates();
				return Vector2.Distance(Bot.Center, ritualTarget) <= CastRadius || MoveCloser(ritualTarget, CastRadius * 0.5f);
			case LacewingTrigger:
				return Avatar.ZoneHallow || MoveCloser(step.Place, 16f);
			case CultistRitualTrigger:
				var devotee = Main.npc.FirstOrDefault(npc => npc.active && npc.type == NPCID.CultistDevote && Vector2.Distance(npc.Center, step.Place) < OldManSearchRadius);
				if (devotee == null)
				{
					Bot.Motor.Stop();
					Bot.Watchdog.RecordProgress();
					return false;
				}
				ritualTarget = devotee.Center;
				var blade2 = Bot.Hands.BestWeapon();
				return blade2 != null && (Bot.Hands.InMeleeReach(devotee, blade2) || MoveCloser(devotee.Center, 16f));
			case VoodooDollTrigger:
				int outward = Bot.CenterTile.X > Main.maxTilesX / 2 ? 1 : -1;
				var lava = WaterFinder.NearestLavaSurface(Bot.CenterTile, LavaSearchRadius, Main.maxTilesY - WallSpawnDepth, tile => (tile.X - Bot.CenterTile.X) * outward >= 0)
					?? WaterFinder.NearestLavaSurface(Bot.CenterTile, LavaSearchRadius, Main.maxTilesY - WallSpawnDepth);
				if (lava is not Point surface)
					return Abandon("no lava found nearby");
				ritualTarget = surface.ToWorldCoordinates();
				return Vector2.Distance(Bot.Center, ritualTarget) <= ThrowRadius || MoveCloser(ritualTarget, ThrowRadius * 0.5f);
			default:
				return true;
		}
	}

	bool MoveCloser(Vector2 target, float radius)
	{
		if (!TravelOrTunnel(target, radius))
			Abandon($"stuck approaching ritual spot {target.ToTileCoordinates()}");
		return false;
	}

	bool Abandon(string reason)
	{
		BotDiagnostics.Log(Bot, $"cannot summon {step.Challenge.Name}: {reason}");
		IsFinished = true;
		return false;
	}

	const int MaxRallyExtensions = 5;
	const uint RallyExtensionTicks = 3600;
	int extensions;

	bool TeamGathered()
	{
		Bot.Motor.Stop();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId).ToList();
		var gathered = team.Where(member => member == Bot || RallyTask.Joined(member, GatherPoint, GatherRadius)).ToList();
		int present = gathered.Count;
		bool stillWaiting = Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		if (present == team.Count || BossReadiness.Evaluate(Bot.Faction, step.Challenge, Bot, gathered).IsReady)
			return true;
		if (!stillWaiting && extensions < MaxRallyExtensions && team.Any(member => member.IsAlive && !gathered.Contains(member) && member.Brain.Current is RallyTask))
		{
			extensions++;
			Bot.Faction.Rally = (GatherPoint, Main.GameUpdateCount + RallyExtensionTicks);
			Bot.Watchdog.RecordProgress();
			return false;
		}
		bool hostileGround = PathFinder.InsideMaze(step.Place.ToTileCoordinates());
		if (stillWaiting && !(hostileGround && present * 2 >= team.Count))
		{
			Bot.Watchdog.RecordProgress();
			return false;
		}
		return present * 2 >= team.Count || GiveUpRally(present, team.Count);
	}

	const float CastRadius = 16f * 15f;
	const float LacewingSearchRadius = 16f * 80f;

	NPC lacewing;

	bool ConsumeAndSpawn(int itemType)
	{
		var item = Inventory.Items(Avatar).FirstOrDefault(candidate => candidate.type == itemType);
		if (item == null)
			return Abandon($"{Lang.GetItemNameValue(itemType)} missing");
		using (AvatarPresence.Enter())
			NPC.SpawnOnPlayer(Avatar.whoAmI, step.Challenge.BossType);
		Inventory.ConsumeOne(Avatar, item);
		return true;
	}

	bool FishWithBait(int baitType, Vector2 water)
	{
		var bait = Inventory.Items(Avatar).FirstOrDefault(item => item.type == baitType);
		var pole = Inventory.Items(Avatar).Where(BossRituals.IsFishingPole).MaxBy(item => item.fishingPole);
		if (bait == null || pole == null)
			return Abandon("no bait or fishing pole");
		using (AvatarPresence.Enter())
		{
			int bobber = Projectile.NewProjectile(new EntitySource_ItemUse(Avatar, pole), water, Vector2.Zero, pole.shoot, 0, 0f, Avatar.whoAmI);
			NPC.SpawnOnPlayer(Avatar.whoAmI, step.Challenge.BossType);
			Main.projectile[bobber].Kill();
		}
		Inventory.ConsumeOne(Avatar, bait);
		return true;
	}

	NPC Lacewing() =>
		Main.npc.FirstOrDefault(npc => npc.active && npc.type == NPCID.EmpressButterfly && Vector2.Distance(npc.Center, Bot.Center) < LacewingSearchRadius);

	NPC ReleaseLacewing()
	{
		if (!Avatar.ZoneHallow)
			return null;
		var critter = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.EmpressButterfly);
		if (critter == null)
			return null;
		int index = NPC.NewNPC(new EntitySource_Misc("AutoTerraMod:ReleaseCritter"), (int)Bot.Center.X, (int)Bot.Center.Y, NPCID.EmpressButterfly);
		Inventory.ConsumeOne(Avatar, critter);
		return Main.npc[index];
	}

	bool KillLacewing()
	{
		if (lacewing == null)
		{
			lacewing = Lacewing() ?? ReleaseLacewing();
			if (lacewing == null)
			{
				if (Main.GameUpdateCount % 600 == 0)
					BotDiagnostics.Log(Bot, $"waiting for a Prismatic Lacewing at {Bot.CenterTile}");
				Bot.Watchdog.RecordProgress();
				return false;
			}
		}
		if (!lacewing.active || lacewing.type != NPCID.EmpressButterfly)
		{
			if (!NPC.AnyNPCs(NPCID.HallowBoss))
				using (AvatarPresence.Enter())
					NPC.SpawnOnPlayer(Avatar.whoAmI, NPCID.HallowBoss);
			return true;
		}
		var weapon = Bot.Hands.BestWeapon();
		if (weapon == null)
			return Abandon("no weapon");
		Bot.Motor.Face(lacewing.Center);
		if (BotHands.IsProjectileWeapon(weapon))
			Bot.Hands.Fire(lacewing, weapon);
		else if (Bot.Hands.InMeleeReach(lacewing, weapon))
			using (AvatarPresence.Enter())
				Bot.Hands.Strike(lacewing, weapon);
		else
			Bot.Motor.MoveTo(lacewing.Center, 16f);
		Bot.Watchdog.RecordProgress();
		return false;
	}

	bool StrikeDevotee(Vector2 devoteeCenter)
	{
		var devotee = Main.npc.FirstOrDefault(npc => npc.active && npc.type == NPCID.CultistDevote && Vector2.Distance(npc.Center, devoteeCenter) < 32f);
		if (devotee == null)
			return NPC.AnyNPCs(NPCID.CultistBoss);
		var weapon = Bot.Hands.BestWeapon();
		if (weapon == null)
			return Abandon("no weapon");
		Bot.Motor.Face(devotee.Center);
		using (AvatarPresence.Enter())
			Bot.Hands.Strike(devotee, weapon);
		return NPC.AnyNPCs(NPCID.CultistBoss);
	}

	bool KillClothierWearingDoll()
	{
		int clothierIndex = NPC.FindFirstNPC(NPCID.Clothier);
		if (clothierIndex < 0)
			return true;
		if (!WearDoll())
			return Abandon("no free accessory slot for the Clothier Voodoo Doll");
		var weapon = Bot.Hands.BestWeapon();
		if (weapon == null)
			return Abandon("no weapon");
		using (AvatarPresence.Enter())
			Bot.Hands.Strike(Main.npc[clothierIndex], weapon);
		return !Main.npc[clothierIndex].active || Main.npc[clothierIndex].type != NPCID.Clothier;
	}

	bool WearDoll()
	{
		if (Avatar.armor.Any(item => item.type == ItemID.ClothierVoodooDoll))
			return true;
		var doll = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.ClothierVoodooDoll);
		if (doll == null)
			return false;
		int slot = System.Array.FindIndex(Avatar.armor, FirstAccessorySlot, AccessorySlots, item => item.IsAir);
		if (slot < 0)
		{
			slot = FirstAccessorySlot;
			if (!Inventory.Add(Avatar, Avatar.armor[slot]).IsAir)
				return false;
		}
		Avatar.armor[slot] = doll.Clone();
		doll.TurnToAir();
		return true;
	}

	bool Perform(Vector2 ritualTarget)
	{
		switch (step.Trigger)
		{
			case SummonItemTrigger item:
				var summonItem = Inventory.Items(Avatar).FirstOrDefault(candidate => candidate.type == item.Item);
				if (summonItem == null)
					return Abandon("summon item missing");
				using (AvatarPresence.Enter())
				{
					NPC.SpawnOnPlayer(Avatar.whoAmI, step.Challenge.BossType);
					foreach (int extra in item.AlsoSpawns ?? System.Array.Empty<int>())
						NPC.SpawnOnPlayer(Avatar.whoAmI, extra);
				}
				Inventory.ConsumeOne(Avatar, summonItem);
				return true;
			case AltarTrigger altarTrigger:
				return ConsumeAndSpawn(altarTrigger.Item);
			case BaitTrigger baitTrigger:
				Bot.Motor.Face(ritualTarget);
				return FishWithBait(baitTrigger.Bait, ritualTarget);
			case BulbTrigger when step.Shrine is Point bulbTile:
				Bot.Motor.Face(ritualTarget);
				ToolResult dug;
				using (AvatarPresence.Enter())
				using (ShrineGuard.AllowBulb())
					dug = Bot.Hands.Dig(bulbTile);
				if (dug is ToolResult.Unbreakable or ToolResult.NoTool)
					return Abandon($"cannot break the bulb at {bulbTile}: {dug}");
				return !TileQuery.HasTile(bulbTile);
			case LacewingTrigger:
				return KillLacewing();
			case CultistRitualTrigger:
				return StrikeDevotee(ritualTarget);
			case PillarsTrigger:
				return true;
			case SmashOrbsTrigger when step.Shrine is Point shrine:
				Bot.Motor.Face(ritualTarget);
				var result = Bot.Hands.Smash(shrine);
				if (result is ToolResult.Unbreakable or ToolResult.NoTool)
					return Abandon($"cannot smash shrine at {shrine}: {result}");
				return result == ToolResult.Done;
			case SmashLarvaTrigger when step.Shrine is Point larvaTile:
				Bot.Motor.Face(ritualTarget);
				ToolResult cut;
				using (AvatarPresence.Enter())
					cut = Bot.Hands.Cut(larvaTile);
				if (cut is ToolResult.Unbreakable or ToolResult.NoTool)
					return Abandon($"cannot break larva at {larvaTile}: {cut}");
				return !TileQuery.HasTile(larvaTile);
			case CurseOldManTrigger:
				using (AvatarPresence.Enter())
					NPC.SpawnSkeletron(Avatar.whoAmI);
				return true;
			case ClothierDollTrigger:
				return KillClothierWearingDoll();
			case VoodooDollTrigger:
				var doll = Inventory.Items(Avatar).FirstOrDefault(candidate => candidate.type == ItemID.GuideVoodooDoll);
				if (doll == null)
					return Abandon("Guide Voodoo Doll missing");
				Bot.Motor.Face(ritualTarget);
				int thrown = Item.NewItem(new EntitySource_Misc("AutoTerraMod:VoodooThrow"), ritualTarget, ItemID.GuideVoodooDoll);
				Main.item[thrown].noGrabDelay = DollGrabDelay;
				Inventory.ConsumeOne(Avatar, doll);
				return true;
			default:
				return Abandon("unknown ritual");
		}
	}
}

public sealed class RallyTask : BotTask
{
	const float ArrivalRadius = 16f * 8f;
	const float LeaderDriftPixels = 16f * 8f;

	Vector2? goal;
	public const float SelfDefenseRadius = 16f * 10f;
	const float RallyPriority = 93f;
	const float ArenaBedsideReach = 16f * 3f;

	public RallyTask(Bot bot) : base(bot) { }

	const float RecallTripDistance = 16f * 80f;
	const float CampGatherRadius = 16f * 60f;
	const uint OrphanGraceTicks = 300;

	const float CampRecallDistance = 16f * 25f;
	const float CloseEnough = 16f * 20f;
	const uint ParkTicks = 300;
	uint parkedUntil;

	bool RecallShortensTrip(Vector2 target)
	{
		if (!CarriesRecall(Avatar))
			return false;
		var tile = target.ToTileCoordinates();
		var here = Bot.CenterTile;
		var camp = Bot.Faction.Settlement;
		bool reachesCamp = RecallLandsAtBase() || Bot.OutpostBed != null;
		if (camp != null && camp.Contains(tile.X, tile.Y) && !camp.Contains(here.X, here.Y) && Vector2.Distance(Bot.Center, target) > CampRecallDistance && reachesCamp)
			return true;
		return Vector2.Distance(Bot.Center, target) - Vector2.Distance(CampLanding, target) > RecallTripDistance;
	}

	public static bool Joined(Bot member, Vector2 point, float radius) =>
		member.IsAlive && member.Npc is NPC body && Vector2.Distance(body.Center, point) < radius
		&& World.TileQuery.ClearLine(body.Center, point);

	public override string NameKey => "Rally";

	public override void Start()
	{
		base.Start();
		goal = null;
	}

	public override float Evaluate()
	{
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until || Bot.Brain.Plan is SummonBossStep)
			return 0f;
		bool leading = BotWorldSystem.FindBot(Bot.Faction.RallyLeaderId) is { IsAlive: true } leader && (leader.Brain.Plan is SummonBossStep or BuildOutpostStep || leader.Brain.Current is SummonTask);
		if (leading)
			Bot.Faction.RallyLeaderSeenAt = Main.GameUpdateCount;
		else if (Main.GameUpdateCount - Bot.Faction.RallyLeaderSeenAt > OrphanGraceTicks)
		{
			Bot.Faction.Rally = null;
			return 0f;
		}
		if (!Bot.Faction.RallyAssembled && Bot.Brain.PoolingForChampion && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < CampGatherRadius)
			return 0f;
		if (SkipsRefight(rally.point))
			return 0f;
		return RallyPriority;
	}

	const float RefightSkipDistance = 16f * 150f;

	bool SkipsRefight(Vector2 point) =>
		BossCatalog.ChallengeOf(Bot.Faction.RallyBossType) is { } challenge && BossCredit.HasDefeated(Bot.Faction, challenge)
		&& Bot.Brain.Plan is GatherStep { Source: NpcSource { IsRare: true } }
		&& Vector2.Distance(Bot.Center, point) > RefightSkipDistance;

	public override void Tick()
	{
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until)
		{
			IsFinished = true;
			return;
		}
		var leader = BotWorldSystem.FindBot(Bot.Faction.RallyLeaderId);
		var camp = Bot.Faction.BaseFeet - new Vector2(0f, 16f * 2f);
		var target = Bot.Faction.RallyAssembled ? rally.point
			: leader is { IsAlive: true } && leader != Bot && Vector2.Distance(leader.Center, Bot.Faction.BaseCenter) < CampGatherRadius ? leader.Center
			: camp;
		if (goal is Vector2 kept && Vector2.Distance(kept, target) < LeaderDriftPixels)
			target = kept;
		goal = target;
		LeaveOutpostIfBedside();
		if (Building.Outpost.ArenaBedFor(Bot.Faction) is Point arena && Bot.OutpostBed != arena)
		{
			var bedside = new Vector2(arena.X * 16f + 8f, (arena.Y + 1) * 16f - 21f);
			if (Vector2.Distance(Bot.Center, bedside) <= ArenaBedsideReach)
			{
				Bot.OutpostBed = arena;
				BotDiagnostics.Log(Bot, $"set the respawn point at the arena outpost bed {arena}");
			}
			else if (TravelOrTunnel(bedside, ArenaBedsideReach * 0.5f))
				return;
		}
		bool close = Vector2.Distance(Bot.Center, target) < CloseEnough;
		if (close && (Bot.Motor.IsStalled || Bot.Motor.IsStuck))
			parkedUntil = Main.GameUpdateCount + ParkTicks;
		if (close && Main.GameUpdateCount < parkedUntil)
		{
			Bot.Motor.Stop();
			Bot.Watchdog.RecordProgress();
			return;
		}
		bool inSight = Joined(Bot, target, ArrivalRadius * 2f);
		if (!inSight && RecallShortensTrip(target) && TryRecallToCamp())
			return;
		TravelOrTunnel(target, inSight ? ArrivalRadius : 16f);
		if (Bot.Motor.HasArrived)
			Bot.Watchdog.RecordProgress();
	}
}

public sealed class ScoutTask : BotTask
{
	const int SurfaceLeg = 250;
	const int WorldEdgeMargin = 60;
	const int UnderworldDepth = 10;
	const int SpanMargin = 20;
	const int MinShrineDepth = 15;
	const int MaxShrineDepth = 110;
	const int ShrineDepthBelowRock = 150;
	const int UnderworldMargin = 50;
	const int OpenSearchRadius = 15;

	static Point OpenTileNear(Point origin)
	{
		for (int radius = 0; radius <= OpenSearchRadius; radius++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				foreach (int dy in new[] { -radius, radius })
				{
					if (!TileQuery.IsSolid(origin.X + dx, origin.Y + dy) && !TileQuery.IsSolid(origin.X + dx, origin.Y + dy - 1))
						return new Point(origin.X + dx, origin.Y + dy);
				}
			}
		}
		return origin;
	}

	ScoutStep step;
	Vector2 destination;

	public ScoutTask(Bot bot) : base(bot) { }

	public override string NameKey => "Scout";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not ScoutStep scout || Inventory.IsNearlyFull(Avatar))
			return 0f;
		step = scout;
		Detail = Terraria.Localization.Language.GetTextValue($"Mods.AutoTerraMod.Landmarks.{scout.Landmark}");
		return 50f + Traits.Exploring * 30f;
	}

	ScoutStep destinationFor;
	uint destinationChosenAt;
	const uint KeepDestinationTicks = 10 * 3600;

	public override void Start()
	{
		base.Start();
		if (step == destinationFor && Main.GameUpdateCount - destinationChosenAt < KeepDestinationTicks && Vector2.Distance(Bot.Center, destination) > 48f)
			return;
		destination = ChooseDestination();
		destinationFor = step;
		destinationChosenAt = Main.GameUpdateCount;
		BotDiagnostics.Log(Bot, $"scouting for {step.Landmark} toward {destination.ToTileCoordinates()}");
	}

	Vector2 ChooseDestination()
	{
		var here = Bot.CenterTile;
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		if (step.Landmark == Landmark.Underworld)
			return new Vector2(here.X * 16f, (Main.UnderworldLayer + UnderworldDepth) * 16f);
		if (step.Landmark == Landmark.Temple && knowledge.LandmarkOf(Landmark.Temple) is Point temple)
			return (TempleEntrance(temple, here) ?? TempleSpot(knowledge, temple)).ToWorldCoordinates(8f, 0f);
		bool jungleCaverns = step.SeekTile == TileID.PlanteraBulb || step.Landmark == Landmark.Temple;
		if (jungleCaverns && knowledge.SpanOf(Landmark.Jungle) is var (jungleMinX, jungleMaxX) && CavernJungleSpot(knowledge, here, jungleMinX, jungleMaxX) is Point cavern)
			return new Vector2(cavern.X * 16f + 8f, cavern.Y * 16f);
		if (step.Underground && knowledge.SpanOf(step.Landmark) is var (minX, maxX))
		{
			int column = Main.rand.Next(minX - SpanMargin, maxX + SpanMargin + 1);
			int surface = TileQuery.SurfaceY(column);
			int deepest = step.SeekTile == TileID.PlanteraBulb ? Main.UnderworldLayer - UnderworldMargin : Math.Min(Math.Max(surface + MaxShrineDepth, (int)Main.rockLayer + ShrineDepthBelowRock), Main.UnderworldLayer - UnderworldMargin);
			int shallowest = step.SeekTile == TileID.PlanteraBulb ? Math.Max(surface + MinShrineDepth, (int)Main.rockLayer) : surface + MinShrineDepth;
			var spot = OpenTileNear(new Point(column, Main.rand.Next(shallowest, deepest)));
			return new Vector2(spot.X * 16f + 8f, spot.Y * 16f);
		}
		if (step.Landmark == Landmark.Dungeon && knowledge.LandmarkOf(Landmark.Jungle) is Point jungle)
			knowledge.ScoutDirection = jungle.X > Main.maxTilesX / 2 ? -1 : 1;
		int x = here.X + knowledge.ScoutDirection * SurfaceLeg;
		if (x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
		{
			knowledge.ScoutDirection = -knowledge.ScoutDirection;
			x = Utils.Clamp(x, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		}
		return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
	}

	const int CavernSpotTries = 80;
	const int JungleProbe = 4;
	const int BulbFloorMargin = 4;

	static Point? CavernJungleSpot(Factions.FactionKnowledge knowledge, Point from, int minX, int maxX)
	{
		Point? fallback = null;
		Point? nearest = null;
		long nearestDistance = long.MaxValue;
		int cell = Factions.FactionKnowledge.ExploreCell;
		for (int attempt = 0; attempt < CavernSpotTries; attempt++)
		{
			int x = Main.rand.Next(minX - SpanMargin, maxX + SpanMargin + 1);
			int y = Main.rand.Next((int)Main.rockLayer, Main.UnderworldLayer - BulbFloorMargin);
			if (!NearJungleGrass(x, y))
				continue;
			var spot = OpenTileNear(new Point(x, y));
			if (Navigation.PathFinder.InsideMaze(spot))
				continue;
			fallback ??= spot;
			if (knowledge.IsExplored(spot.X / cell, spot.Y / cell))
				continue;
			long distance = (long)(spot.X - from.X) * (spot.X - from.X) + (long)(spot.Y - from.Y) * (spot.Y - from.Y);
			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				nearest = spot;
			}
		}
		return nearest ?? fallback;
	}

	const int TempleReach = 80;
	const int TempleSpotTries = 200;

	static Point TempleSpot(Factions.FactionKnowledge knowledge, Point temple)
	{
		int cell = Factions.FactionKnowledge.ExploreCell;
		Point? inside = null;
		for (int attempt = 0; attempt < TempleSpotTries; attempt++)
		{
			int x = temple.X + Main.rand.Next(-TempleReach, TempleReach + 1);
			int y = temple.Y + Main.rand.Next(-TempleReach / 2, TempleReach / 2 + 1);
			if (!WorldGen.InWorld(x, y, 10) || Main.tile[x, y].WallType != WallID.LihzahrdBrickUnsafe || TileQuery.IsSolid(x, y) || TileQuery.IsSolid(x, y - 1))
				continue;
			if (!knowledge.IsExplored(x / cell, y / cell))
				return new Point(x, y);
			inside ??= new Point(x, y);
		}
		return inside ?? OpenTileNear(temple);
	}

	const int DoorSearchWidth = 200;
	const int DoorSearchHeight = 120;
	const int EntranceArrival = 6;
	const int DoorStepInside = 2;

	static Point? TempleEntrance(Point temple, Point here)
	{
		if (LockedTempleDoor(temple) is not Point door)
			return null;
		int row = door.Y + 1;
		Point? mouth = null;
		int shortest = int.MaxValue;
		foreach (int direction in new[] { -1, 1 })
		{
			for (int offset = 1; offset < DoorSearchWidth; offset++)
			{
				int x = door.X + direction * offset;
				if (!WorldGen.InWorld(x, row, 10))
					break;
				if (Main.tile[x, row].WallType == WallID.LihzahrdBrickUnsafe)
					continue;
				if (offset < shortest)
				{
					shortest = offset;
					mouth = new Point(x, row);
				}
				break;
			}
		}
		if (mouth is not Point outside)
			return null;
		if (Math.Abs(here.X - outside.X) > EntranceArrival || Math.Abs(here.Y - outside.Y) > EntranceArrival)
			return outside;
		return new Point(door.X - Math.Sign(outside.X - door.X) * DoorStepInside, row);
	}

	static Point? LockedTempleDoor(Point temple)
	{
		for (int x = temple.X - DoorSearchWidth; x <= temple.X + DoorSearchWidth; x++)
		{
			for (int y = temple.Y - DoorSearchHeight; y <= temple.Y + DoorSearchHeight; y++)
			{
				if (WorldGen.InWorld(x, y, 10) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.ClosedDoor && WorldGen.IsLockedDoor(x, y))
					return new Point(x, y);
			}
		}
		return null;
	}

	static bool NearJungleGrass(int x, int y)
	{
		for (int dx = -JungleProbe; dx <= JungleProbe; dx++)
		{
			for (int dy = -JungleProbe; dy <= JungleProbe; dy++)
			{
				if (WorldGen.InWorld(x + dx, y + dy, 2) && Main.tile[x + dx, y + dy].HasTile && Main.tile[x + dx, y + dy].TileType == TileID.JungleGrass)
					return true;
			}
		}
		return false;
	}

	public override void Tick()
	{
		if (Discovered())
		{
			BotDiagnostics.Log(Bot, $"discovered {step.Landmark}{(step.Underground ? " shrine" : "")}");
			Bot.Watchdog.RecordProgress();
			IsFinished = true;
			return;
		}
		bool moving = TravelOrTunnel(destination, 48f);
		if (moving && !Bot.Motor.HasArrived)
			return;
		if (!moving)
		{
			BotDiagnostics.Log(Bot, $"cannot reach scouting spot {destination.ToTileCoordinates()}, giving it up; motor: {Bot.Motor.Describe()}");
			BotWorldSystem.KnowledgeOf(Bot.Faction).MarkExplored(destination.ToTileCoordinates());
		}
		destinationFor = null;
		if (SearchingShrines)
			BotWorldSystem.KnowledgeOf(Bot.Faction).FruitlessShrineSearches++;
		IsFinished = true;
	}

	bool SearchingShrines => step.Underground && step.Landmark != Landmark.Underworld;

	bool Discovered()
	{
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		if (SearchingShrines)
		{
			bool found = step.SeekTile >= 0 ? BossRituals.KnowsTile(Bot, step.SeekTile) : BossRituals.KnowsShrine(Bot, step.Landmark);
			if (found)
				knowledge.FruitlessShrineSearches = 0;
			return found;
		}
		return knowledge.LandmarkOf(step.Landmark) != null;
	}
}

public sealed class SmashShrineTask : BotTask
{
	SmashShrineStep step;

	public SmashShrineTask(Bot bot) : base(bot) { }

	public override string NameKey => "Smash";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SmashShrineStep smash)
			return 0f;
		step = smash;
		return 60f + Traits.Exploring * 20f;
	}

	public override void Tick()
	{
		var hammer = Bot.Hands.BestHammer;
		if (hammer == null || !TileQuery.HasTile(step.Shrine))
		{
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReach(hammer, step.Shrine))
		{
			if (!TravelOrTunnel(step.Shrine.ToWorldCoordinates(), 24f))
			{
				BotDiagnostics.Log(Bot, $"stuck walking to shrine {step.Shrine}; motor: {Bot.Motor.Describe()}{TerrainDump.Around(Bot.CenterTile, 30, 14)}");
				IsFinished = true;
			}
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(step.Shrine.ToWorldCoordinates());
		var result = Bot.Hands.Smash(step.Shrine);
		if (result == ToolResult.Done)
			BotDiagnostics.Log(Bot, $"smashed shrine at {step.Shrine}, orbs smashed so far {WorldGen.shadowOrbCount}");
		if (result is not ToolResult.Working)
			IsFinished = true;
	}
}

public sealed class TravelTask : BotTask
{
	const float ArrivalRadius = 16f * 6f;

	TravelStep step;

	public TravelTask(Bot bot) : base(bot) { }

	public override string NameKey => "Travel";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not TravelStep travel)
			return 0f;
		step = travel;
		return 55f;
	}

	public override void Tick()
	{
		if (!TravelOrTunnel(step.Place, ArrivalRadius) || Bot.Motor.HasArrived)
			IsFinished = true;
	}
}

public sealed class BuildBridgeTask : BotTask
{

	const float StandTolerance = 16f * 2f;
	const uint FrontierPatienceTicks = 600;

	const int MaxDip = 2;

	bool ClearChestOn(Point tile)
	{
		for (int x = tile.X - 1; x <= tile.X + 1; x++)
		{
			for (int y = tile.Y - 2; y < tile.Y; y++)
			{
				if (!WorldGen.InWorld(x, y, 2) || !Main.tile[x, y].HasTile || !TileID.Sets.BasicChest[Main.tile[x, y].TileType])
					continue;
				if (Chest.IsLocked(x, y))
					return false;
				int index = Chest.FindChestByGuessing(x, y);
				if (index >= 0)
				{
					var chest = Main.chest[index];
					for (int slot = 0; slot < chest.item.Length; slot++)
					{
						if (chest.item[slot] is not { IsAir: false } item)
							continue;
						var leftover = Inventory.Add(Avatar, item.Clone());
						if (!leftover.IsAir)
							Item.NewItem(new EntitySource_TileBreak(x, y), Bot.Center, leftover);
						chest.item[slot] = new Item();
					}
				}
				BotDiagnostics.Log(Bot, $"emptied and removing the {TileQuery.TypeAt(new Point(x, y))} chest resting on corridor tile {tile}");
				Bot.Hands.Dig(new Point(x, y));
				return true;
			}
		}
		return false;
	}

	bool NeedsBlast(Point tile)
	{
		if (!TileQuery.IsSolid(tile) || !Explosives.CanBlast(tile.X, tile.Y) || !WorldGen.CanKillTile(tile.X, tile.Y))
			return false;
		if (!Bot.Hands.Explosives.IsFused && Bot.Hands.Explosives.Light(tile, spareProtected: false))
			BotDiagnostics.Log(Bot, $"bombing corridor tile {TileQuery.TypeAt(tile)} at {tile}");
		return true;
	}

	public static bool BlocksWithoutBombs(Bot bot, World.ICorridor bridge, int x)
	{
		int pick = bot.Hands.BestPickaxe?.pick ?? 0;
		for (int y = bridge.FeetRow(x) - bridge.Headroom + 1; y <= bridge.FeetRow(x) + 1; y++)
		{
			if (TileQuery.IsSolid(x, y) && !bridge.IsTolerated(x, y) && Navigation.PickRequirement.MinimumPick(Main.tile[x, y].TileType, x, y) > pick && Explosives.CanBlast(x, y))
				return true;
		}
		return false;
	}

	static bool Obstructed(World.ICorridor bridge, int x)
	{
		if (TileQuery.HasTile(new Point(x, bridge.FeetRow(x) + 1)) && !bridge.IsDeck(x))
			return true;
		for (int y = bridge.FeetRow(x) - bridge.Headroom + 1; y <= bridge.FeetRow(x); y++)
		{
			if (TileQuery.IsSolid(x, y) && !bridge.IsTolerated(x, y))
				return true;
		}
		return false;
	}
	int stuckFrontier = int.MinValue;
	uint stuckSince;

	BuildBridgeStep step;

	public BuildBridgeTask(Bot bot) : base(bot) { }

	public override void Start()
	{
		base.Start();
		stuckFrontier = int.MinValue;
	}

	public override string NameKey => step?.Bridge is World.Highway ? "BuildHighway" : "BuildBridge";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not BuildBridgeStep build)
			return 0f;
		step = build;
		return 65f + Traits.Building * 20f;
	}

	public override void Tick()
	{
		var bridge = step.Bridge;
		int direction = step.Direction;
		int frontier = bridge.Frontier(direction);
		if (frontier == bridge.End(direction) && bridge.IsBuilt(frontier) || bridge.RequiresPlatform(frontier) && !bridge.IsDeck(frontier) && !Bot.Hands.HasPlatform)
		{
			BotDiagnostics.Log(Bot, $"corridor work at {frontier} ends: built={bridge.IsBuilt(frontier)} needsPlatform={bridge.RequiresPlatform(frontier)} hasPlatform={Bot.Hands.HasPlatform}");
			IsFinished = true;
			return;
		}
		if (frontier != stuckFrontier)
		{
			stuckFrontier = frontier;
			stuckSince = Main.GameUpdateCount;
		}
		else if (Main.GameUpdateCount - stuckSince > FrontierPatienceTicks && Vector2.Distance(Bot.Center, new Vector2(frontier * 16f, bridge.Y * 16f)) < 16f * 8f && Obstructed(bridge, frontier) && !BlocksWithoutBombs(Bot, bridge, frontier))
		{
			BotDiagnostics.Log(Bot, $"corridor frontier {frontier} would not budge ({TileQuery.TypeAt(new Point(frontier, bridge.Y + 1))}), going past it{TerrainDump.Around(new Point(frontier, bridge.Y), 6, 4)}");
			bridge.Skip(frontier);
			stuckSince = Main.GameUpdateCount;
			return;
		}
		var stand = new Vector2(PathFinder.CenterXOf(frontier - 2 * direction), (bridge.FeetRow(frontier - 2 * direction) + 1) * 16f - 21f);
		if (Vector2.Distance(Bot.Center, stand) > StandTolerance)
		{
			if (!TravelOrTunnel(stand, 12f))
			{
				if (bridge is Hellbridge && Bot.CenterTile.Y > bridge.FeetRow(Bot.CenterTile.X) + 1 && CarriesRecall(Avatar) && TryRecallHome())
				{
					BotDiagnostics.Log(Bot, $"below the Underworld bridge at {Bot.CenterTile}, recalling home to come back down the hellevator");
					return;
				}
				BotDiagnostics.Log(Bot, $"cannot reach bridge frontier {frontier}; motor: {Bot.Motor.Describe()}{TerrainDump.Around(new Point(frontier, bridge.Y), 16, 8)}");
				IsFinished = true;
			}
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(new Vector2(frontier * 16f, bridge.Y * 16f));
		foreach (int column in new[] { frontier, frontier + direction })
		{
			int feet = bridge.FeetRow(column);
			for (int y = feet - bridge.Headroom + 1; y <= feet; y++)
			{
				if (TileQuery.IsSolid(column, y) && !bridge.IsTolerated(column, y))
				{
					var dug = Bot.Hands.Dig(new Point(column, y), clearSaplings: true);
					if (dug == ToolResult.OutOfReach)
					{
						Bot.Motor.MoveTo(new Point(column, y).ToWorldCoordinates(), 24f);
						if (Bot.Motor.IsStuck)
						{
							BotDiagnostics.Log(Bot, $"cannot get within reach of corridor tile {column},{y}");
							IsFinished = true;
						}
						return;
					}
					if (dug is ToolResult.Unbreakable or ToolResult.NoTool)
					{
						if (PlugLavaAround(new Point(column, y)) || ClearChestOn(new Point(column, y)))
							return;
						if (dug == ToolResult.Unbreakable && !NeedsBlast(new Point(column, y)))
						{
							int rows = y - (feet - ICorridor.BodyRows + 1) + 1;
							if (rows <= 0)
							{
								BotDiagnostics.Log(Bot, $"corridor leaves an unbreakable {TileQuery.TypeAt(new Point(column, y))} above head height at {column},{y}");
								bridge.Tolerate(column, y);
								return;
							}
							if (bridge is Hellbridge && rows <= MaxDip)
							{
								BotDiagnostics.Log(Bot, $"corridor dips {rows} rows under an unbreakable {TileQuery.TypeAt(new Point(column, y))} at {column},{y}");
								bridge.Dip(column, rows);
								return;
							}
							BotDiagnostics.Log(Bot, $"corridor goes past an unbreakable {TileQuery.TypeAt(new Point(column, y))} at {column},{y}");
							bridge.Skip(column);
							return;
						}
						if (Bot.Hands.Explosives.IsFused)
							return;
						IsFinished = true;
					}
					return;
				}
			}
		}
		var deck = new Point(frontier, bridge.FeetRow(frontier) + 1);
		var deckTile = Main.tile[deck.X, deck.Y];
		if (deckTile.HasTile && (deckTile.IsHalfBlock || deckTile.Slope != SlopeType.Solid) && (TileQuery.IsSolid(deck.X, deck.Y) || TileQuery.IsPlatform(deck.X, deck.Y)))
		{
			deckTile.IsHalfBlock = false;
			deckTile.Slope = SlopeType.Solid;
			WorldGen.SquareTileFrame(deck.X, deck.Y);
			Bot.Watchdog.RecordProgress();
			return;
		}
		if (bridge.IsDeck(frontier))
			return;
		if (TileQuery.HasTile(deck))
		{
			if (Main.tileCut[Main.tile[deck.X, deck.Y].TileType])
			{
				Bot.Hands.Cut(deck);
				return;
			}
			var cleared = Bot.Hands.Dig(deck, clearSaplings: true);
			if (cleared == ToolResult.OutOfReach)
			{
				Bot.Motor.MoveTo(deck.ToWorldCoordinates(), 24f);
				if (Bot.Motor.IsStuck)
					IsFinished = true;
			}
			else if (cleared is ToolResult.Unbreakable or ToolResult.NoTool && !PlugLavaAround(deck))
			{
				BotDiagnostics.Log(Bot, $"corridor deck {deck} is unbreakable {TileQuery.TypeAt(deck)}");
				if (cleared == ToolResult.Unbreakable && !NeedsBlast(deck))
					bridge.Skip(frontier);
				else if (!Bot.Hands.Explosives.IsFused)
					IsFinished = true;
			}
			return;
		}
		if (Bot.Hands.PlaceBridge(deck, solidDeck: !bridge.WantsPlatform(frontier)) || (Main.tile[deck.X, deck.Y].LiquidAmount > 0 && Bot.Hands.PlaceScaffold(deck)))
			Bot.Watchdog.RecordProgress();
		else if (!Bot.Hands.CanReach(null, deck))
		{
			Bot.Motor.MoveTo(deck.ToWorldCoordinates(), 24f);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
		}
		else if (Bot.Hands.Ready && Main.GameUpdateCount % 300 == 0)
			BotDiagnostics.Log(Bot, $"cannot place bridge deck at {frontier},{bridge.Y + 1}; material={Bot.Hands.HasBridgeMaterial} tile={Main.tile[frontier, bridge.Y + 1].HasTile} liquid={Main.tile[frontier, bridge.Y + 1].LiquidAmount}");
	}
}

public sealed class DigShaftTask : BotTask
{
	const int PlatformLookBack = 8;
	const int ShaftEntryDepth = 6;

	const float CellApproach = 16f * 2.5f;

	void Approach(Hellevator shaft, Point cell, Vector2 workSpot)
	{
		var tile = Bot.CenterTile;
		if (tile.X >= shaft.X && tile.X <= shaft.Right && Bot.Center.Y < workSpot.Y)
		{
			Descend(shaft, workSpot);
			return;
		}
		if (!viaMouth && TravelOrTunnel(cell.ToWorldCoordinates(), CellApproach))
			return;
		Descend(shaft, workSpot);
	}

	void Descend(Hellevator shaft, Vector2 workSpot)
	{
		float centerX = (shaft.X + 1) * 16f;
		var tile = Bot.CenterTile;
		bool inShaft = tile.X >= shaft.X && tile.X <= shaft.Right && tile.Y >= shaft.TopY - ShaftEntryDepth && Bot.Center.Y < workSpot.Y;
		if (inShaft)
		{
			viaMouth = false;
			CentreIn(shaft);
			return;
		}
		if (!viaMouth && TravelOrTunnel(workSpot, 12f))
			return;
		viaMouth = true;
		var mouth = new Vector2(centerX, shaft.TopY * 16f - 21f);
		if (!TravelOrTunnel(mouth, 12f))
			IsFinished = true;
	}

	bool viaMouth;

	public override void Start()
	{
		base.Start();
		viaMouth = false;
	}

	DigShaftStep step;

	public DigShaftTask(Bot bot) : base(bot) { }

	public override string NameKey => "DigShaft";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not DigShaftStep dig)
			return 0f;
		step = dig;
		return 70f + Traits.Mining * 15f;
	}

	public override void Tick()
	{
		var shaft = step.Shaft;
		int row = shaft.FirstUnfinishedRow();
		if (shaft.IsFinished)
		{
			IsFinished = true;
			return;
		}
		if (Hellevator.LavaBelow(shaft.X, row))
		{
			if (shaft.LastBlastAt > 0 && Main.GameUpdateCount - shaft.LastBlastAt < Hellevator.DrainTicks)
			{
				if (!Bot.Hands.Explosives.IsFused)
					Bot.Motor.Stop();
				Bot.Watchdog.RecordProgress();
				return;
			}
			if (shaft.LavaBlasts < Hellevator.MaxLavaBlasts && shaft.LavaFloor(row) is Point floor && Bot.Hands.Explosives.Light(floor))
			{
				shaft.LavaBlasts++;
				shaft.LastBlastAt = Main.GameUpdateCount;
				BotDiagnostics.Log(Bot, $"blasting the floor of the lava at {floor} under the hellevator ({shaft.LavaBlasts}/{Hellevator.MaxLavaBlasts})");
			}
			else
				BotDiagnostics.Log(Bot, $"hellevator stops above lava at row {row}");
			IsFinished = true;
			return;
		}
		float centerX = (shaft.X + 1) * 16f;
		var workSpot = new Vector2(centerX, row * 16f - 21f);
		for (int above = row - 1; above >= row - PlatformLookBack && above > shaft.TopY; above--)
		{
			if (shaft.NeedsPlatform(above) && !shaft.HasPlatform(above) && Bot.Hands.PlaceBridge(new Point(shaft.X, above)))
			{
				Bot.Watchdog.RecordProgress();
				return;
			}
		}
		for (int column = shaft.X; column <= shaft.Right; column++)
		{
			var cell = new Point(column, row);
			bool cleanup = shaft.NeedsCleanup(column, row);
			if (!cleanup && !TileQuery.IsSolid(cell.X, cell.Y))
				continue;
			var dug = Bot.Hands.Dig(cell, clearSaplings: true);
			if (column == shaft.Right && dug is ToolResult.Unbreakable or ToolResult.NoTool)
			{
				shaft.LeaveNarrow(row);
				return;
			}
			if (dug == ToolResult.OutOfReach)
			{
				Approach(shaft, cell, workSpot);
				return;
			}
			Bot.Motor.Stop();
			if (dug == ToolResult.Unbreakable && PlugLavaAround(cell))
				return;
			if (dug is ToolResult.Unbreakable or ToolResult.NoTool || !BossRituals.CanDigRow(Bot, shaft, row))
			{
				BotDiagnostics.Log(Bot, $"cannot dig the hellevator at {cell}: tile {TileQuery.TypeAt(cell)} (pick {Bot.Hands.BestPickaxe?.pick ?? 0}, protected {Building.HouseInspector.IsProtected(cell.X, cell.Y)})");
				IsFinished = true;
			}
			return;
		}
		if (Bot.Hands.Ready && Main.GameUpdateCount % 300 == 0)
			BotDiagnostics.Log(Bot, $"shaft row {row} idle: needsPlatform={shaft.NeedsPlatform(row)} hasPlatform={shaft.HasPlatform(row)} material={Bot.Hands.HasBridgeMaterial} place0={Bot.Hands.DescribeBridgePlacement(new Point(shaft.X, row))} place1={Bot.Hands.DescribeBridgePlacement(new Point(shaft.X + 1, row))} botFeet={PathFinder.FeetOf(Bot.Npc)} cells={TileQuery.TypeAt(new Point(shaft.X, row))}/{TileQuery.TypeAt(new Point(shaft.X + 1, row))}{TerrainDump.Around(new Point(shaft.X, row), 6, 4)}");
	}
}
