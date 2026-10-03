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

	Vector2? relocated;

	Vector2 GatherPoint => relocated is Vector2 here ? here : step.Trigger is VoodooDollTrigger
		? step.Place + new Vector2(step.Place.X / 16f > Main.maxTilesX / 2 ? -WallGatherInset : WallGatherInset, 0f)
		: step.Place;
	string headcount;

	public SummonTask(Bot bot) : base(bot) { }

	public override string NameKey => "Summon";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SummonBossStep summon || RallyTask.Hosted(Bot.Faction, summon.Challenge.BossType))
			return 0f;
		step = summon;
		Detail = Bot.Faction.RallyAssembled ? summon.Challenge.Name : headcount ?? summon.Challenge.Name;
		return SummonPriority;
	}

	public override void Start()
	{
		base.Start();
		travelFailures = 0;
		closestApproach = float.MaxValue;
		closestApproachAt = Main.GameUpdateCount;
		lacewing = null;
		extensions = 0;
		headcount = null;
		relocated = null;
		var faction = Bot.Faction;
		if (faction.Rally is { } rally && Main.GameUpdateCount < rally.until && faction.RallyLeaderId == Bot.Id)
			return;
		faction.Rally = (GatherPoint, Main.GameUpdateCount + AssembleTicks);
		faction.RallyBossType = step.Challenge.BossType;
		faction.RallyLeaderId = Bot.Id;
		faction.RallyLeaderSeenAt = Main.GameUpdateCount;
		faction.RallyAssembled = false;
	}

	bool YieldsToNightBoss() =>
		!step.Challenge.NightOnly && step.Trigger is not PillarsTrigger && BossCredit.IsOptional(step.Challenge)
		&& BossCredit.NextChallengeFor(Bot.Faction) is { NightOnly: true } next && next != step.Challenge;

	public override void Tick()
	{
		if (!step.Challenge.WindowStillOpen || BossCatalog.IsBossAlive(step.Challenge) || YieldsToNightBoss())
		{
			if (Bot.Faction.RallyLeaderId == Bot.Id)
				Bot.Faction.Rally = null;
			IsFinished = true;
			return;
		}
		if (!Assembled())
			return;
		InviteGuests();
		if (step.Trigger is PillarsTrigger)
		{
			if (BesiegePillar())
				IsFinished = true;
			return;
		}
		if (!AtArenaBed())
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
		if (step.Trigger is not PillarsTrigger)
		{
			BossCredit.RecordSummoner(step.Challenge, Bot);
			BotDiagnostics.Log(Bot, $"summoned {step.Challenge.Name} via {step.Trigger.GetType().Name}");
			Main.NewText(Terraria.Localization.Language.GetTextValue("Mods.AutoTerraMod.UI.BossSummoned", Bot.Name, Bot.Faction.Name, step.Challenge.Name), Bot.Faction.Color);
		}
		IsFinished = true;
	}

	void InviteGuests()
	{
		var host = Bot.Faction;
		if (step.Trigger is PillarsTrigger || BotWorldSystem.Relation == Config.FactionRelation.War || host.Rally is not { } rally || !BossCredit.HasDefeated(host, step.Challenge) && ConfidentAlone(host))
			return;
		foreach (var guest in BotWorldSystem.Factions)
		{
			if (guest == host || BossCredit.HasDefeated(guest, step.Challenge))
				continue;
			bool free = guest.Rally is not { } own || Main.GameUpdateCount >= own.until || guest.RallyLeaderId == Bot.Id;
			if (!free)
				continue;
			if (guest.RallyLeaderId != Bot.Id)
				BotDiagnostics.Log(Bot, $"invited {guest.Name} to fight {step.Challenge.Name} at {rally.point.ToTileCoordinates()}");
			guest.Rally = rally;
			guest.RallyBossType = step.Challenge.BossType;
			guest.RallyLeaderId = Bot.Id;
			guest.RallyLeaderSeenAt = Main.GameUpdateCount;
			guest.RallyAssembled = true;
		}
	}

	const float SoloComfort = 1.5f;

	const uint ConfidenceCacheTicks = 600;
	uint confidenceAt;
	bool confident;

	bool ConfidentAlone(Factions.Faction host)
	{
		if (confidenceAt != 0 && Main.GameUpdateCount - confidenceAt < ConfidenceCacheTicks)
			return confident;
		var report = BossReadiness.Evaluate(host, step.Challenge, Bot);
		confident = report.KillSeconds > 0f && report.SurviveSeconds >= report.KillSeconds * report.RequiredRatio * SoloComfort;
		confidenceAt = Main.GameUpdateCount;
		return confident;
	}

	const uint ArenaOutpostWaitTicks = 5 * 3600;
	uint arenaWaitStarted;

	bool AwaitingArenaOutpost()
	{
		var faction = Bot.Faction;
		if (step.Trigger is LacewingTrigger || !Building.Outpost.IsDistantArena(faction, step.Place) || Building.Outpost.ArenaBedFor(faction) != null
			|| OutpostTask.ArenaRecentlyRejected(Bot))
			return false;
		if (!OutpostTask.ArenaUnderway(faction) && !BotWorldSystem.Bots.Any(member => member.FactionId == faction.Id && member.IsAlive && Building.Outpost.WantsArenaOutpost(member)))
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
		if (step.Trigger is PillarsTrigger)
		{
			faction.RallyAssembled = true;
			faction.Rally = (GatherPoint, Main.GameUpdateCount + RallyTicks);
			return true;
		}
		if (Vector2.Distance(step.Place, faction.BaseCenter) > DistantArenaReach)
		{
			faction.RallyAssembled = true;
			faction.Rally = (GatherPoint, Main.GameUpdateCount + DistantRallyTicks);
			BotDiagnostics.Log(Bot, $"calling the team to {step.Place.ToTileCoordinates()} for {step.Challenge.Name}");
			ChooseRallyRoute(faction, step.Place);
			return true;
		}
		LeaveOutpostIfBedside();
		bool awayFromCamp = BelowOwnCamp() && Bot.Center.Y > faction.BaseCenter.Y + BelowCampTolerance
			|| Vector2.Distance(Bot.Center, step.Place) > ArrivalRadius && Vector2.Distance(Bot.Center, faction.BaseCenter) > GatherRadius;
		if (awayFromCamp && Vector2.Distance(Bot.Center, faction.BaseCenter) > Vector2.Distance(CampLanding, faction.BaseCenter) + CampRecallReach && Main.GameUpdateCount - Bot.LastRecallAt > RecallCooldownTicks && CanRecall(Bot) && RecallReachesCamp() && TryRecallToCamp())
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
		ChooseRallyRoute(faction, step.Place);
		return true;
	}

	const float RouteWorthTiles = 150f;
	const int UndergroundRallyDepth = 30;
	const uint RallyRouteTicks = 5 * 3600;

	void ChooseRallyRoute(Factions.Faction faction, Vector2 place)
	{
		faction.RallyRoute = null;
		faction.RallyShaft = null;
		if (step.Trigger is PillarsTrigger || Vector2.Distance(place, faction.BaseCenter) < RouteWorthTiles * 16f)
			return;
		var goal = place.ToTileCoordinates();
		bool underground = goal.Y > Main.worldSurface + UndergroundRallyDepth;
		bool shaftReaches = underground && faction.Hellevator is { } own && own.FirstImpassableRow() > goal.Y + 1 && TunnelDiggable(faction, own.RopeX, goal);
		int choice = underground ? (shaftReaches ? 1 + Main.rand.Next(2) : 1) : Main.rand.Next(2);
		var route = new System.Collections.Generic.List<Highway>();
		Hellevator shaft = null;
		string strategy;
		if (choice == 1)
		{
			int low = System.Math.Min(faction.BaseTile.X, goal.X), high = System.Math.Max(faction.BaseTile.X, goal.X);
			route.AddRange(Highway.All.Where(highway => !highway.IsComplete && highway.Y < Main.worldSurface + UndergroundRallyDepth
				&& System.Math.Min(highway.StartX, highway.EndX) < high && System.Math.Max(highway.StartX, highway.EndX) > low));
			if (underground && DiggableColumn(faction, goal) is int shaftX)
				shaft = new Hellevator(shaftX, TileQuery.SurfaceY(shaftX + 1) - 1) { StopRow = goal.Y + 1 };
			strategy = $"building {route.Count} highways{(shaft != null ? " and a shaft down" : "")} on the way";
		}
		else if (choice == 2 && faction.Hellevator is { } hellevator)
		{
			int direction = System.Math.Sign(goal.X - hellevator.RopeX);
			if (direction != 0)
				route.Add(new Highway(faction.Id, direction, hellevator.RopeX + direction * 2, goal.X, goal.Y + 1, null));
			strategy = "going down the hellevator and building a tunnel across";
		}
		else
			strategy = "heading straight there";
		BotDiagnostics.Log(Bot, $"rally route to {goal}: {strategy}");
		if (route.Count == 0 && shaft == null)
			return;
		faction.RallyRoute = route;
		faction.RallyShaft = shaft;
		faction.RallyCheckedIn.Clear();
		faction.RallyRouteUntil = Main.GameUpdateCount + (uint)(RallyRouteTicks * Planning.ProgressionPlanner.WorldScale);
	}

	const int ShaftShiftTiles = 24;

	static int? DiggableColumn(Factions.Faction faction, Point goal)
	{
		int bestPick = BestPick(faction);
		for (int shift = 0; shift <= ShaftShiftTiles; shift++)
		{
			foreach (int x in new[] { goal.X - 1 + shift, goal.X - 1 - shift })
			{
				if (ColumnDiggable(x, TileQuery.SurfaceY(x + 1) - 1, goal.Y + 1, bestPick))
					return x;
			}
		}
		return null;
	}

	static bool ColumnDiggable(int x, int top, int bottom, int bestPick)
	{
		for (int y = top; y <= bottom; y++)
		{
			for (int column = x; column <= x + Hellevator.Width - 1; column++)
			{
				if (TileQuery.IsSolid(column, y) && (Navigation.PickRequirement.MinimumPick(Main.tile[column, y].TileType, column, y) > bestPick || Building.HouseInspector.IsProtected(column, y)))
					return false;
			}
		}
		return true;
	}

	static int BestPick(Factions.Faction faction) =>
		BotWorldSystem.Bots.Where(member => member.FactionId == faction.Id && member.IsAlive).Select(member => member.Hands.BestPickaxe?.pick ?? 0).DefaultIfEmpty(0).Max();

	static bool TunnelDiggable(Factions.Faction faction, int fromX, Point goal)
	{
		int bestPick = BestPick(faction);
		int low = Math.Min(fromX, goal.X), high = Math.Max(fromX, goal.X);
		for (int x = low; x <= high; x++)
		{
			for (int y = goal.Y - 2; y <= goal.Y + 1; y++)
			{
				if (TileQuery.IsSolid(x, y) && Navigation.PickRequirement.MinimumPick(Main.tile[x, y].TileType, x, y) > bestPick)
					return false;
			}
		}
		return true;
	}

	bool GiveUpRally(int present, int teamSize)
	{
		Bot.Faction.Rally = null;
		Bot.Faction.RallyCooldownUntil = Main.GameUpdateCount + RallyCooldownTicks;
		return Abandon($"only {present}/{teamSize} teammates made it to the rally");
	}

	const float UnderworldAcceptRadius = 16f * 60f;

	const uint ApproachPatienceTicks = 8 * 60;
	float closestApproach = float.MaxValue;
	uint closestApproachAt;

	bool ReachedPlace()
	{
		if (relocated != null)
			return true;
		float distance = Vector2.Distance(Bot.Center, step.Place);
		bool noWayCloser = Bot.Motor.IsStalled || Bot.Motor.Tunneling || Bot.Motor.HasNoRoute;
		bool inUnderworld = Bot.CenterTile.Y >= Main.UnderworldLayer && step.Place.Y >= Main.UnderworldLayer * 16f;
		if (distance < closestApproach - 16f)
		{
			closestApproach = distance;
			closestApproachAt = Main.GameUpdateCount;
		}
		bool noLongerApproaching = Main.GameUpdateCount - closestApproachAt > ApproachPatienceTicks;
		if ((noWayCloser || noLongerApproaching) && step.Trigger is SummonItemTrigger { CanUseHere: { } canUseHere } && canUseHere(Avatar))
		{
			relocated = GroundBelow();
			Bot.Faction.Rally = (relocated.Value, Main.GameUpdateCount + RallyTicks);
			BotDiagnostics.Log(Bot, $"cannot get closer to {step.Place.ToTileCoordinates()}, summoning {step.Challenge.Name} here at {relocated.Value.ToTileCoordinates()} instead");
			return true;
		}
		if (distance <= ArrivalRadius || (noWayCloser || noLongerApproaching) && distance <= StuckAcceptRadius || inUnderworld && distance <= UnderworldAcceptRadius)
			return true;
		if (!noLongerApproaching)
			Bot.Watchdog.RecordProgress();
		Bot.Faction.Rally = (GatherPoint, Main.GameUpdateCount + (uint)(Vector2.Distance(step.Place, Bot.Faction.BaseCenter) > DistantArenaReach ? DistantRallyTicks : RallyTicks));
		if (TravelOrTunnel(step.Place, ArrivalRadius * 0.5f))
			return false;
		if (++travelFailures >= TravelRetries)
			return Abandon($"cannot reach {step.Place.ToTileCoordinates()}");
		Bot.Motor.Stop();
		return false;
	}

	const int GroundSearchRows = 15;
	const int GroundSearchColumns = 6;

	Vector2 GroundBelow()
	{
		if (Bot.Npc is not NPC body)
			return Bot.Center;
		var feet = PathFinder.FeetOf(body);
		for (int y = feet.Y; y <= feet.Y + GroundSearchRows; y++)
		{
			for (int offset = 0; offset <= GroundSearchColumns * 2; offset++)
			{
				int x = feet.X + (offset % 2 == 0 ? offset / 2 : -(offset + 1) / 2);
				if (PathFinder.IsSupportedNow(x, y) && BodyFits(x, y))
					return new Vector2((x + 1) * 16f, (y + 1) * 16f - body.height / 2f);
			}
		}
		return Bot.Center;
	}

	static bool BodyFits(int x, int feetY)
	{
		for (int column = x; column <= x + 1; column++)
			for (int row = feetY - 2; row <= feetY; row++)
				if (TileQuery.IsSolid(column, row))
					return false;
		return true;
	}

	bool ApproachRitual(out Vector2 ritualTarget)
	{
		ritualTarget = step.Place;
		switch (step.Trigger)
		{
			case PillarsTrigger:
				return BesiegePillar();
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
				float fromSpot = Vector2.Distance(Bot.Center, step.Place);
				return fromSpot < 16f * 4f || Avatar.ZoneHallow && fromSpot < LacewingWaitRadius || MoveCloser(step.Place, 16f);
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

	const float PillarReach = 16f * 60f;
	const float PillarGuardReach = 16f * 40f;

	bool BesiegePillar()
	{
		if (PillarSiege.SiegedPillar(step.Place) is not NPC pillar)
			return true;
		PillarSiege.Tick(Bot, pillar, TravelOrTunnel);
		return false;
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
		var withGuests = gathered.Concat(BotWorldSystem.Bots.Where(guest => guest.FactionId != Bot.FactionId && guest.Faction.RallyLeaderId == Bot.Id
			&& RallyTask.Joined(guest, GatherPoint, GatherRadius))).ToList();
		if (present == team.Count || BossReadiness.Evaluate(Bot.Faction, step.Challenge, Bot, withGuests).IsReady)
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
	const float LacewingWaitRadius = 16f * 30f;

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
		if (castAt == 0 || Main.GameUpdateCount - castAt > CastWaitTicks * 4)
		{
			castAt = Main.GameUpdateCount;
			Bot.Hands.CastLine(pole, water);
			return false;
		}
		if (Main.GameUpdateCount - castAt < CastWaitTicks)
			return false;
		castAt = 0;
		Bot.Hands.CastLine(pole, water);
		using (AvatarPresence.Enter())
		{
			int bobber = Projectile.NewProjectile(new EntitySource_ItemUse(Avatar, pole), water, Vector2.Zero, pole.shoot, 0, 0f, Avatar.whoAmI);
			NPC.SpawnOnPlayer(Avatar.whoAmI, step.Challenge.BossType);
			Main.projectile[bobber].Kill();
		}
		Inventory.ConsumeOne(Avatar, bait);
		return true;
	}

	const uint CastWaitTicks = 90;
	uint castAt;

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
		{
			Avatar.killClothier = true;
			Bot.Hands.Strike(Main.npc[clothierIndex], weapon);
		}
		if (Main.npc[clothierIndex].active && Main.npc[clothierIndex].type == NPCID.Clothier)
			return false;
		return NPC.AnyNPCs(NPCID.SkeletronHead) || Abandon("the Clothier died without summoning Skeletron");
	}

	bool WearDoll()
	{
		if (Enumerable.Range(FirstAccessorySlot, AccessorySlots).Any(index => Avatar.armor[index].type == ItemID.ClothierVoodooDoll && Avatar.IsItemSlotUnlockedAndUsable(index)))
			return true;
		var doll = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.ClothierVoodooDoll);
		if (doll == null)
			return false;
		int slot = Enumerable.Range(FirstAccessorySlot, AccessorySlots).FirstOrDefault(index => Avatar.armor[index].IsAir && Avatar.IsItemSlotUnlockedAndUsable(index), -1);
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
				Bosses.DollThrow.Throw(ItemID.GuideVoodooDoll, Bot.Center, ritualTarget, DollGrabDelay);
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
	const float SiegeJoinPixels = 16f * 70f;
	const float RallyPriority = 93f;

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
		if (!CanRecall(Bot))
			return false;
		var tile = target.ToTileCoordinates();
		var here = Bot.CenterTile;
		var camp = Bot.Faction.Settlement;
		bool reachesCamp = RecallReachesCamp();
		if (camp != null && camp.Contains(tile.X, tile.Y) && !camp.Contains(here.X, here.Y) && Vector2.Distance(Bot.Center, target) > CampRecallDistance && reachesCamp)
			return true;
		return Vector2.Distance(Bot.Center, target) - Vector2.Distance(CampLanding, target) > RecallTripDistance;
	}

	public static bool Hosted(Factions.Faction faction, int bossType) =>
		faction.Rally is { } rally && Main.GameUpdateCount < rally.until && faction.RallyBossType == bossType
		&& BotWorldSystem.FindBot(faction.RallyLeaderId) is { } leader && leader.FactionId != faction.Id;

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
		if (Bot.Faction.RallyBuilding)
			return 0f;
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until || Bot.Brain.Plan is SummonBossStep && !Hosted(Bot.Faction, Bot.Faction.RallyBossType))
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
		if (Bot.Faction.RallyAssembled && Bot.Faction.RallyBossType == Terraria.ID.NPCID.MoonLordCore && PillarSiege.SiegedPillar(rally.point) is NPC sieged
			&& Vector2.Distance(Bot.Center, rally.point) < SiegeJoinPixels)
		{
			PillarSiege.Tick(Bot, sieged, TravelOrTunnel);
			return;
		}
		LeaveOutpostIfBedside();
		if (!AtArenaBed())
			return;
		bool sees = Bot.Npc is NPC body && Collision.CanHitLine(body.position, body.width, body.height, target - new Vector2(8f), 16, 16);
		bool close = sees && Vector2.Distance(Bot.Center, target) < CloseEnough;
		if (close && (Bot.Motor.IsStalled || Bot.Motor.IsStuck))
			parkedUntil = Main.GameUpdateCount + ParkTicks;
		if (close && Main.GameUpdateCount < parkedUntil)
		{
			Bot.Motor.Stop();
			Bot.Watchdog.RecordProgress();
			return;
		}
		bool inSight = sees && Joined(Bot, target, ArrivalRadius * 2f);
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

	int UnderworldShaftColumn(Point here)
	{
		if (Bot.Faction.Hellevator is { IsComplete: true } own)
			return own.RopeX;
		var open = BotWorldSystem.Factions.Select(faction => faction.Hellevator).Where(shaft => shaft is { IsComplete: true })
			.MinBy(shaft => Math.Abs(shaft.RopeX - here.X));
		return open?.RopeX ?? Bot.Faction.Hellevator?.RopeX ?? here.X;
	}

	ScoutStep destinationFor;
	uint destinationChosenAt;
	const uint KeepDestinationTicks = 10 * 3600;

	public override void Start()
	{
		base.Start();
		if (step == destinationFor && Main.GameUpdateCount - destinationChosenAt < KeepDestinationTicks * Planning.ProgressionPlanner.WorldScale && Vector2.Distance(Bot.Center, destination) > 48f)
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
			return new Vector2(UnderworldShaftColumn(here) * 16f, (Main.UnderworldLayer + UnderworldDepth) * 16f);
		if (step.Landmark == Landmark.Temple && knowledge.LandmarkOf(Landmark.Temple) is Point temple)
			return (TempleEntrance(temple, here) ?? TempleSpot(knowledge, temple)).ToWorldCoordinates(8f, 0f);
		bool jungleCaverns = step.SeekTile == TileID.PlanteraBulb || step.Landmark == Landmark.Temple;
		if (jungleCaverns && knowledge.SpanOf(Landmark.Jungle) is var (jungleMinX, jungleMaxX) && CavernJungleSpot(knowledge, here, jungleMinX, jungleMaxX) is Point cavern)
			return new Vector2(cavern.X * 16f + 8f, cavern.Y * 16f);
		if (step.Underground && step.Landmark == Landmark.Jungle && step.SeekTile < 0 && UnvisitedHive(knowledge, here) is Point hiveTile)
			return OpenTileNear(hiveTile).ToWorldCoordinates(8f, 0f);
		if (step.Underground && knowledge.SpanOf(step.Landmark) is var (minX, maxX))
		{
			int column = Main.rand.Next(minX - SpanMargin, maxX + SpanMargin + 1);
			int surface = TileQuery.SurfaceY(column);
			bool bulb = step.SeekTile == TileID.PlanteraBulb;
			bool hive = step.Landmark == Landmark.Jungle && !bulb;
			int deepest = bulb || hive ? Main.UnderworldLayer - UnderworldMargin : Math.Min(Math.Max(surface + MaxShrineDepth, (int)Main.rockLayer + ShrineDepthBelowRock), Main.UnderworldLayer - UnderworldMargin);
			int shallowest = bulb ? Math.Max(surface + MinShrineDepth, (int)Main.rockLayer) : hive ? Math.Max(surface + MinShrineDepth, (int)Main.worldSurface) : surface + MinShrineDepth;
			var spot = OpenTileNear(new Point(column, Main.rand.Next(shallowest, deepest)));
			return new Vector2(spot.X * 16f + 8f, spot.Y * 16f);
		}
		int direction = Heading(knowledge, step.Landmark, Bot.Faction.BaseTile.X, here.X);
		int x = here.X + direction * SurfaceLeg;
		if (AtShore(here.X, direction) || x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
		{
			direction = -direction;
			x = UnsurveyedColumn(knowledge, Bot.Faction.BaseTile.X, direction) ?? here.X + direction * SurfaceLeg;
			BotDiagnostics.Log(Bot, $"reached the shore at {here.X}, scouting the other way from {x}");
		}
		else if (UnsurveyedColumn(knowledge, Bot.Faction.BaseTile.X, direction) is int frontier && (frontier - here.X) * direction > SurfaceLeg)
			x = frontier;
		knowledge.ScoutDirection = direction;
		x = Utils.Clamp(x, WorldGen.beachDistance, Main.maxTilesX - WorldGen.beachDistance);
		return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
	}

	static Point? UnvisitedHive(Factions.FactionKnowledge knowledge, Point here)
	{
		int cell = Factions.FactionKnowledge.ExploreCell;
		return knowledge.KnownTilesOf(TileID.Hive).Where(tile => !knowledge.IsExplored(tile.X / cell, tile.Y / cell))
			.Select(tile => (Point?)tile).MinBy(tile => Vector2.DistanceSquared(tile.Value.ToVector2(), here.ToVector2()));
	}

	public static int Heading(Factions.FactionKnowledge knowledge, Landmark landmark, int campX, int fromX)
	{
		if (InferredSide(knowledge, landmark) is int side)
			return side;
		int middle = Main.maxTilesX / 2;
		if (landmark == Landmark.Ocean)
			return campX < middle ? -1 : 1;
		var east = UnsurveyedColumn(knowledge, campX, 1);
		var west = UnsurveyedColumn(knowledge, campX, -1);
		if (east is int right && west is int left)
			return System.Math.Abs(right - fromX) <= System.Math.Abs(fromX - left) ? 1 : -1;
		if (east != null || west != null)
			return east != null ? 1 : -1;
		return knowledge.ScoutDirection == 0 ? 1 : knowledge.ScoutDirection;
	}

	static int? InferredSide(Factions.FactionKnowledge knowledge, Landmark landmark)
	{
		var sides = new System.Collections.Generic.Dictionary<Landmark, int>
		{
			[Landmark.Dungeon] = 1,
			[Landmark.Snow] = Main.drunkWorld ? -1 : 1,
			[Landmark.Jungle] = -1
		};
		if (!sides.TryGetValue(landmark, out int wanted))
			return null;
		foreach (var (known, relative) in sides)
		{
			if (known == landmark || knowledge.LandmarkOf(known) is not Point spot)
				continue;
			int knownSide = spot.X > Main.maxTilesX / 2 ? 1 : -1;
			return knownSide * relative * wanted;
		}
		return null;
	}

	static bool AtShore(int x, int direction) =>
		direction > 0 ? x >= Main.maxTilesX - WorldGen.beachDistance - ShoreSlack : x <= WorldGen.beachDistance + ShoreSlack;

	const int ShoreSlack = 10;

	public static int? UnsurveyedColumn(Factions.FactionKnowledge knowledge, int fromX, int direction)
	{
		int cell = Factions.FactionKnowledge.ExploreCell;
		for (int x = fromX; x > WorldGen.beachDistance && x < Main.maxTilesX - WorldGen.beachDistance; x += direction * cell / 2)
		{
			if (!knowledge.IsExplored(x / cell, TileQuery.SurfaceY(x) / cell))
				return x;
		}
		return null;
	}

	const int CavernSpotTries = 80;
	const int JungleProbe = 4;
	const int BulbFloorMargin = 4;

	public static Point? CavernJungleSpot(Factions.FactionKnowledge knowledge, Point from, int minX, int maxX)
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
		if (World.TempleDoor.Mouth(door) is not Point outside || World.TempleDoor.OutsideSide(door) is not int side)
			return null;
		if (World.TempleDoor.InTunnel(door, here) || Math.Abs(here.X - outside.X) <= EntranceArrival && Math.Abs(here.Y - outside.Y) <= EntranceArrival)
			return World.TempleDoor.InsideStep(door, side, DoorStepInside);
		return outside;
	}

	public static Point? LockedTempleDoor(Point temple)
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
			BotWorldSystem.KnowledgeOf(Bot.Faction).ShrineSearchFailed(step.Landmark);
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
				knowledge.ShrineFound(step.Landmark);
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
				if (Chest.IsLocked(x, y) && !UnlockChest(x, y))
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

	bool UnlockChest(int x, int y)
	{
		var tile = Main.tile[x, y];
		int left = x - tile.TileFrameX % 36 / 18, top = y - tile.TileFrameY % 36 / 18;
		int frame = Main.tile[left, top].TileFrameX;
		int key = Main.tile[left, top].TileType != TileID.Containers ? ItemID.GoldenKey
			: frame >= 144 && frame <= 178 ? ItemID.ShadowKey
			: frame >= 828 && frame <= 1006 ? ItemID.JungleKey + frame / 36 - 23
			: ItemID.GoldenKey;
		var held = Inventory.Items(Avatar).FirstOrDefault(item => item.type == key);
		if (held == null || !Chest.Unlock(left, top))
			return false;
		if (key != ItemID.ShadowKey)
			Inventory.ConsumeOne(Avatar, held);
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendTileSquare(-1, left, top, 2, 2);
		BotDiagnostics.Log(Bot, $"unlocked the chest at {left},{top} with {Lang.GetItemNameValue(key)}");
		return true;
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
			if (Blocks(bridge, x, y))
				return true;
		}
		return false;
	}

	const int ClutterSweepColumns = 40;

	static void SweepClutter(World.Highway highway, int fromX)
	{
		for (int x = fromX; x != fromX + highway.Direction * ClutterSweepColumns && highway.Spans(x); x += highway.Direction)
		{
			for (int y = highway.Y - highway.Headroom + 1; y <= highway.Y; y++)
			{
				if (!World.Highway.IsClutter(x, y))
					continue;
				WorldGen.KillTile(x, y, noItem: true);
				if (Main.netMode == NetmodeID.Server)
					NetMessage.SendTileSquare(-1, x, y);
			}
		}
	}

	static readonly System.Collections.Generic.Dictionary<Point, int> clutterSweeps = new();
	const int MaxClutterSweeps = 3;

	static bool Blocks(World.ICorridor bridge, int x, int y) =>
		(TileQuery.IsSolid(x, y) || bridge is World.Highway && World.Highway.IsClutter(x, y)) && !bridge.IsTolerated(x, y);
	int stuckFrontier = int.MinValue;
	uint stuckSince;

	BuildBridgeStep step;

	public BuildBridgeTask(Bot bot) : base(bot) { }

	bool HoldUpSand(ICorridor bridge, int column, int feet)
	{
		var ceiling = new Point(column, feet - bridge.Headroom);
		if (!TileQuery.HasTile(ceiling) || !TileID.Sets.Falling[TileQuery.TypeAt(ceiling)])
			return false;
		bool digging = false;
		for (int y = feet - bridge.Headroom + 1; y <= feet && !digging; y++)
			digging = TileQuery.IsSolid(column, y);
		if (!digging)
			return false;
		var result = Bot.Hands.ReplaceWithBlock(ceiling);
		if (result == ToolResult.OutOfReach)
		{
			Bot.Motor.MoveTo(ceiling.ToWorldCoordinates(), 24f);
			return !Bot.Motor.IsStuck;
		}
		if (result == ToolResult.Working && !TileID.Sets.Falling[TileQuery.TypeAt(ceiling)])
			BotDiagnostics.Log(Bot, $"holding up the sand over the corridor at {ceiling} with {TileQuery.TypeAt(ceiling)}");
		return result == ToolResult.Working;
	}

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
		int frontier = bridge is World.Highway road && direction == road.Direction ? road.WorkFrontier() : bridge.Frontier(direction);
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
				if (bridge is Hellbridge && Bot.CenterTile.Y > bridge.FeetRow(Bot.CenterTile.X) + 1 && CanRecall(Bot) && TryRecallHome())
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
			if (column != frontier && bridge.Skipped(column))
				continue;
			int feet = bridge.FeetRow(column);
			if (HoldUpSand(bridge, column, feet))
				return;
			for (int y = feet - bridge.Headroom + 1; y <= feet; y++)
			{
				if (Blocks(bridge, column, y))
				{
					if (bridge is World.Highway highway && World.Highway.IsClutter(column, y))
					{
						var cell = new Point(column, y);
						clutterSweeps[cell] = (clutterSweeps.TryGetValue(cell, out int swept) ? swept : 0) + 1;
						if (clutterSweeps[cell] > MaxClutterSweeps)
						{
							BotDiagnostics.Log(Bot, $"{TileQuery.TypeAt(cell)} at {cell} keeps coming back on the highway, leaving it");
							bridge.Tolerate(column, y);
							return;
						}
						Bot.Hands.Dig(cell, clearSaplings: true);
						SweepClutter(highway, column);
						Bot.Watchdog.RecordProgress();
						return;
					}
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
		else if (bridge is Hellbridge && TileQuery.IsDangerousLiquid(deck.X, deck.Y) && Bot.Hands.CanReach(null, deck) && Bot.Hands.Ready)
		{
			if (bridge.Raise(frontier, direction))
				BotDiagnostics.Log(Bot, $"lava fills the bridge deck at {deck}, raising column {frontier} to row {bridge.FeetRow(frontier)}");
			else
			{
				BotDiagnostics.Log(Bot, $"lava fills the bridge deck at {deck} even when raised, skipping column {frontier}");
				bridge.Skip(frontier);
			}
			Bot.Watchdog.RecordProgress();
		}
		else if (!Bot.Hands.CanReach(null, deck))
		{
			Bot.Motor.MoveTo(deck.ToWorldCoordinates(), 24f);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
		}
		else if (Bot.Hands.Ready && Main.GameUpdateCount % 300 == 0)
			BotDiagnostics.Log(Bot, $"cannot place bridge deck at {deck}; material={Bot.Hands.HasBridgeMaterial} tile={Main.tile[deck.X, deck.Y].HasTile} liquid={Main.tile[deck.X, deck.Y].LiquidAmount} anchored={BotHands.Anchored(deck)}");
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
		if (tile.Y > shaft.TopY + ShaftMouthReach)
		{
			IsFinished = true;
			return;
		}
		viaMouth = true;
		var mouth = new Vector2(centerX, shaft.TopY * 16f - 21f);
		if (!TravelOrTunnel(mouth, 12f))
			IsFinished = true;
	}

	bool viaMouth;

	const int BombDropRows = 20;
	const int ThrowSpotSlack = 2;

	const int LavaThrowMinRows = 6;

	const int SealScanAbove = 3;
	const int SealScanBelow = 6;
	const int MaxFloorDrop = 4;
	const int MaxSealFailures = 30;
	const int MaxSealApproachTicks = 180;
	const int SealApproachRadius = 8;
	const uint SealSkipTicks = 900;
	int sealFailures;
	int approachTicks;
	Point approaching;
	readonly System.Collections.Generic.Dictionary<Point, uint> sealSkipped = new();

	static bool HoldsLava(int x, int y) => WorldGen.InWorld(x, y, 2) && Sealable(x, y) && Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Lava;

	static bool LeaksLava(Hellevator shaft, int x, int y) => (x == shaft.X - 1 || x == shaft.Right + 1) && Sealable(x, y) && TileQuery.TouchesLava(x, y);

	static bool Sealable(int x, int y) => !TileQuery.IsSolid(x, y) && !(Main.tile[x, y].HasTile && Main.tileRope[Main.tile[x, y].TileType]);

	Point? LavaToSeal(Hellevator shaft, int row)
	{
		int feet = PathFinder.FeetOf(Bot.Npc).Y;
		int from = System.Math.Max(shaft.TopY + 1, System.Math.Min(feet, row) - SealScanAbove);
		for (int y = from; y <= row + SealScanBelow; y++)
		{
			for (int x = shaft.X - 1; x <= shaft.Right + 1; x++)
			{
				var cell = new Point(x, y);
				if ((HoldsLava(x, y) || LeaksLava(shaft, x, y)) && BotHands.Anchored(cell) && !(sealSkipped.TryGetValue(cell, out uint until) && Main.GameUpdateCount < until))
					return cell;
			}
		}
		return null;
	}

	bool SealLava(Hellevator shaft, int row)
	{
		if (Bot.Hands.Explosives.IsFused)
			return false;
		var block = Inventory.Items(Avatar).Where(Building.Outpost.IsBlock).OrderBy(item => item.type == ItemID.StoneBlock ? 0 : 1).FirstOrDefault();
		if (block == null || sealFailures >= MaxSealFailures || LavaToSeal(shaft, row) is not Point cell)
			return false;
		Bot.Watchdog.RecordProgress();
		if (Bot.Hands.CanReach(block, cell))
		{
			Bot.Motor.Stop();
			if (!Bot.Hands.Ready)
				return true;
			if (TileQuery.HasTile(cell))
			{
				if (Bot.Hands.Dig(cell) is ToolResult.Unbreakable or ToolResult.NoTool)
					sealFailures++;
				return true;
			}
			if (Bot.Hands.Place(block, cell, intoShaft: true))
			{
				Main.tile[cell.X, cell.Y].LiquidAmount = 0;
				Liquid.AddWater(cell.X, cell.Y);
				sealFailures = 0;
				BotDiagnostics.Log(Bot, $"sealed lava at {cell} in the hellevator with {block.Name}");
			}
			else if (++sealFailures >= MaxSealFailures)
				BotDiagnostics.Log(Bot, $"cannot seal lava at {cell}: {Bot.Hands.PlaceFailure(block, cell)}");
			return true;
		}
		if (DigFloorTowardLava(shaft, cell))
			return true;
		if (cell != approaching)
		{
			approaching = cell;
			approachTicks = 0;
		}
		if (WorldScan.DistanceSq(Bot.CenterTile, cell) <= SealApproachRadius * SealApproachRadius && ++approachTicks > MaxSealApproachTicks)
		{
			sealSkipped[cell] = Main.GameUpdateCount + SealSkipTicks;
			BotDiagnostics.Log(Bot, $"cannot get within reach of lava at {cell}, digging on for now; feet={PathFinder.FeetOf(Bot.Npc)}{TerrainDump.Around(cell, 6, 6)}");
			return false;
		}
		sealingLava = true;
		Descend(shaft, new Vector2((shaft.X + 1) * 16f, (cell.Y - 2) * 16f - 21f));
		sealingLava = false;
		return true;
	}

	bool DigFloorTowardLava(Hellevator shaft, Point lava)
	{
		var feet = PathFinder.FeetOf(Bot.Npc);
		if (!PathFinder.IsSupportedNow(feet.X, feet.Y) || Bot.CenterTile.X < shaft.X || Bot.CenterTile.X > shaft.Right)
			return false;
		int floor = feet.Y + 1;
		if (floor >= lava.Y || LavaInRows(shaft, floor, floor))
			return false;
		int landing = floor + 1;
		while (landing < lava.Y && !RowHasSolid(shaft, landing))
			landing++;
		if (!RowHasSolid(shaft, landing) || LavaInRows(shaft, floor + 1, landing - 1) || landing - floor > MaxFloorDrop)
			return false;
		for (int x = shaft.X; x <= shaft.Right; x++)
		{
			if (!TileQuery.IsSolid(x, floor))
				continue;
			Bot.Motor.Stop();
			var dug = Bot.Hands.Dig(new Point(x, floor));
			if (dug is ToolResult.Unbreakable or ToolResult.NoTool or ToolResult.OutOfReach)
				return false;
			Bot.Watchdog.RecordProgress();
			return true;
		}
		return false;
	}

	static bool RowHasSolid(Hellevator shaft, int y)
	{
		for (int x = shaft.X; x <= shaft.Right; x++)
			if (TileQuery.IsSolid(x, y))
				return true;
		return false;
	}

	static bool LavaInRows(Hellevator shaft, int from, int to)
	{
		for (int y = from; y <= to; y++)
			for (int x = shaft.X - 1; x <= shaft.Right + 1; x++)
				if (Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Lava && !TileQuery.IsSolid(x, y))
					return true;
		return false;
	}

	void BlastLavaWall(Hellevator shaft, int row)
	{
		if (shaft.LastBlastAt > 0 && Main.GameUpdateCount - shaft.LastBlastAt < Hellevator.DrainTicks || Bot.Hands.Explosives.IsFused)
		{
			Bot.Watchdog.RecordProgress();
			return;
		}
		Point? wall = null;
		for (int x = shaft.X; x <= shaft.Right && wall == null; x++)
			if (TileQuery.IsSolid(x, row) && TileQuery.TouchesLava(x, row))
				wall = new Point(x, row);
		if (wall is not Point cell)
			return;
		bool highEnough = Bot.CenterTile.Y <= row - LavaThrowMinRows;
		if (!highEnough || !Bot.Hands.Explosives.InThrowRange(cell))
		{
			if (highEnough)
				Descend(shaft, new Vector2((shaft.X + 1) * 16f, AboveLava(shaft, row - LavaThrowMinRows) * 16f - 21f));
			else
				Bot.Motor.MoveTo(new Vector2((shaft.X + 1) * 16f, (row - LavaThrowMinRows - 4) * 16f), 16f);
			return;
		}
		if (Bot.Hands.Explosives.Light(cell))
		{
			shaft.LavaBlasts++;
			shaft.LastBlastAt = Main.GameUpdateCount;
			BotDiagnostics.Log(Bot, $"blasting rock holding back lava at {cell} from row {Bot.CenterTile.Y} ({shaft.LavaBlasts}/{Hellevator.MaxLavaBlasts})");
		}
		else
			shaft.LavaBlasts = Hellevator.MaxLavaBlasts;
		IsFinished = true;
	}

	const float ThrowAtBreath = 0.5f;
	bool diving;

	const int SurfaceApproachRows = 8;

	bool AtShaftWater(Hellevator shaft, int row) =>
		Bot.CenterTile.X >= shaft.X && Bot.CenterTile.X <= shaft.Right && Bot.CenterTile.Y >= row - shaft.WaterAbove(row) - SurfaceApproachRows;

	bool RefillAir(Hellevator shaft, int row)
	{
		int water = shaft.WaterAbove(row);
		if (water <= Hellevator.SafeDiveRows || BossRituals.BreathesUnderwater(Bot))
			return false;
		var surface = new Vector2((shaft.X + 1) * 16f, (row - water - 3) * 16f);
		if (!AtShaftWater(shaft, row))
		{
			diving = false;
			Descend(shaft, surface);
			return true;
		}
		if (diving)
		{
			if (Bot.Vitals.BreathFraction >= ThrowAtBreath)
				return false;
			diving = false;
		}
		if (Bot.Vitals.BreathFraction >= 1f && Bot.Npc?.wet != true)
		{
			diving = true;
			return false;
		}
		if (Bot.Center.Y > surface.Y + 16f)
			Bot.Motor.MoveTo(surface, 12f);
		else
			Bot.Motor.Stop();
		Bot.Watchdog.RecordProgress();
		return true;
	}

	void BlastWater(Hellevator shaft, int row)
	{
		if (shaft.LastBlastAt > 0 && Main.GameUpdateCount - shaft.LastBlastAt < Hellevator.DrainTicks)
		{
			if (!Bot.Hands.Explosives.IsFused)
				Bot.Motor.Stop();
			Bot.Watchdog.RecordProgress();
			return;
		}
		if (!Bot.Hands.Explosives.Carries)
		{
			IsFinished = true;
			return;
		}
		bool breathLow = diving && AtShaftWater(shaft, row) && Bot.Vitals.BreathFraction < ThrowAtBreath;
		if (!breathLow && RefillAir(shaft, row))
			return;
		int waterTop = row - shaft.WaterAbove(row);
		int throwRow = row - BombDropRows;
		var spot = new Vector2((shaft.X + 1) * 16f, throwRow * 16f - 21f);
		bool there = System.Math.Abs(Bot.CenterTile.Y - throwRow) <= ThrowSpotSlack && Bot.CenterTile.X >= shaft.X && Bot.CenterTile.X <= shaft.Right;
		if (!there && !breathLow)
		{
			Descend(shaft, spot);
			return;
		}
		if (shaft.LavaFloor(row) is Point waterFloor && !Bot.Hands.Explosives.InThrowRange(waterFloor) && !breathLow)
		{
			Descend(shaft, spot);
			return;
		}
		if (shaft.LavaFloor(row) is Point floor && Bot.Hands.Explosives.Light(floor, volley: Hellevator.WaterVolley))
		{
			shaft.WaterBlasts++;
			shaft.LastBlastAt = Main.GameUpdateCount;
			BotDiagnostics.Log(Bot, $"blasting the floor at {floor} under {row - waterTop} rows of water from row {Bot.CenterTile.Y} (breath {Bot.Vitals.BreathFraction:0.00}, {shaft.WaterBlasts}/{Hellevator.MaxWaterBlasts})");
		}
		diving = false;
		IsFinished = true;
	}

	public override void Start()
	{
		base.Start();
		viaMouth = false;
		diving = false;
		sealFailures = 0;
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
		int row = shaft.WorkRow();
		if (shaft.IsFinished || BossRituals.ShaftTooFlooded(Bot, shaft))
		{
			IsFinished = true;
			return;
		}
		if (BossRituals.NeedsWaterBlast(Bot, shaft))
		{
			BlastWater(shaft, row);
			return;
		}
		if (RefillAir(shaft, row))
			return;
		if (SealLava(shaft, row))
			return;
		if (BossRituals.RowTouchesLava(shaft, row) && Bot.Hands.Explosives.Carries && shaft.LavaBlasts < Hellevator.MaxLavaBlasts)
		{
			BlastLavaWall(shaft, row);
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
			if (shaft.LavaBlasts < Hellevator.MaxLavaBlasts && shaft.LavaFloor(row) is Point lavaFloor && !Bot.Hands.Explosives.InThrowRange(lavaFloor))
			{
				Descend(shaft, new Vector2((shaft.X + 1) * 16f, AboveLava(shaft, row - 2) * 16f - 21f));
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
