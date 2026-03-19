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
	const int AssembleTicks = 3 * 3600;
	const int TravelRetries = 3;
	const int RallyCooldownTicks = 2 * 3600;
	const float GatherRadius = 16f * 20f;
	const float BelowCampTolerance = 16f * 3f;
	const float ArrivalRadius = 16f * 10f;
	const float StuckAcceptRadius = 16f * 25f;
	const float OldManSearchRadius = 16f * 150f;
	const float TalkRadius = 16f * 4f;
	const float ThrowRadius = 16f * 8f;
	const int LavaSearchRadius = 60;
	const int DollGrabDelay = 300;
	const int FirstAccessorySlot = 3;
	const int AccessorySlots = 7;

	SummonBossStep step;
	int travelFailures;
	string headcount;

	public SummonTask(Bot bot) : base(bot) { }

	public override string NameKey => "Summon";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SummonBossStep summon)
			return 0f;
		step = summon;
		Detail = Bot.Faction.RallyAssembled ? summon.Challenge.Name : headcount ?? summon.Challenge.Name;
		return Engagement.NearestHostile(Bot, RallyTask.SelfDefenseRadius, true) != null ? RallyTask.YieldToSelfDefense : 85f;
	}

	public override void Start()
	{
		base.Start();
		travelFailures = 0;
		lacewing = null;
		headcount = null;
		var faction = Bot.Faction;
		if (faction.Rally is { } rally && Main.GameUpdateCount < rally.until && faction.RallyLeaderId == Bot.Id)
			return;
		faction.Rally = (step.Place, Main.GameUpdateCount + AssembleTicks);
		faction.RallyLeaderId = Bot.Id;
		faction.RallyAssembled = false;
	}

	public override void Tick()
	{
		if (!step.Challenge.CanSummonNow || BossCatalog.IsBossAlive(step.Challenge))
		{
			IsFinished = true;
			return;
		}
		if (!Assembled())
			return;
		if (!ReachedPlace())
			return;
		if (!ApproachRitual(out var ritualTarget))
			return;
		if (!TeamGathered())
			return;
		if (!Perform(ritualTarget))
			return;
		Bot.Faction.Rally = null;
		Bot.Watchdog.RecordProgress();
		BotDiagnostics.Log(Bot, $"summoned {step.Challenge.Name} via {step.Trigger.GetType().Name}");
		IsFinished = true;
	}

	bool Assembled()
	{
		var faction = Bot.Faction;
		if (faction.RallyAssembled)
			return true;
		Bot.Watchdog.RecordProgress();
		bool awayFromCamp = BelowOwnCamp() && Bot.Center.Y > faction.BaseCenter.Y + BelowCampTolerance
			|| Vector2.Distance(Bot.Center, step.Place) > ArrivalRadius && Vector2.Distance(Bot.Center, faction.BaseCenter) > GatherRadius;
		if (!awayFromCamp || !TravelOrTunnel(faction.BaseFeet - new Vector2(0f, 16f * 2f), 16f))
			Bot.Motor.Stop();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId).ToList();
		int present = team.Count(member => RallyTask.Joined(member, Bot.Center, GatherRadius));
		headcount = $"{step.Challenge.Name} {present}/{team.Count}";
		bool waiting = faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		if (present < team.Count && waiting)
			return false;
		if (present * 2 < team.Count)
			return GiveUpRally(present, team.Count);
		faction.RallyAssembled = true;
		faction.Rally = (step.Place, Main.GameUpdateCount + RallyTicks);
		BotDiagnostics.Log(Bot, $"set out for {step.Challenge.Name} with {present}/{team.Count} teammates");
		return true;
	}

	bool GiveUpRally(int present, int teamSize)
	{
		Bot.Faction.Rally = null;
		Bot.Faction.RallyCooldownUntil = Main.GameUpdateCount + RallyCooldownTicks;
		return Abandon($"only {present}/{teamSize} teammates made it to the rally");
	}

	bool ReachedPlace()
	{
		float distance = Vector2.Distance(Bot.Center, step.Place);
		if (distance <= ArrivalRadius || (Bot.Motor.IsStuck && distance <= StuckAcceptRadius))
			return true;
		Bot.Faction.Rally = (step.Place, Main.GameUpdateCount + RallyTicks);
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
				var lava = WaterFinder.NearestLavaSurface(Bot.CenterTile, LavaSearchRadius);
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

	bool TeamGathered()
	{
		Bot.Motor.Stop();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId).ToList();
		int present = team.Count(member => RallyTask.Joined(member, step.Place, GatherRadius));
		bool stillWaiting = Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		if (present == team.Count)
			return true;
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
	public const float YieldToSelfDefense = 40f;

	public RallyTask(Bot bot) : base(bot) { }

	public static bool Joined(Bot member, Vector2 point, float radius) =>
		member.IsAlive && member.Npc is NPC body && Vector2.Distance(body.Center, point) < radius
		&& Collision.CanHitLine(body.position, body.width, body.height, point - new Vector2(8f), 16, 16);

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
		return Engagement.NearestHostile(Bot, SelfDefenseRadius, true) != null ? YieldToSelfDefense : 88f;
	}

	public override void Tick()
	{
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until)
		{
			IsFinished = true;
			return;
		}
		var leader = BotWorldSystem.FindBot(Bot.Faction.RallyLeaderId);
		var target = leader is { IsAlive: true } && leader != Bot ? leader.Center : rally.point;
		if (goal is Vector2 kept && Vector2.Distance(kept, target) < LeaderDriftPixels)
			target = kept;
		goal = target;
		bool inSight = Joined(Bot, target, ArrivalRadius * 2f);
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

	ScoutStep step;
	Vector2 destination;

	public ScoutTask(Bot bot) : base(bot) { }

	public override string NameKey => "Scout";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not ScoutStep scout)
			return 0f;
		step = scout;
		Detail = Terraria.Localization.Language.GetTextValue($"Mods.AutoTerraMod.Landmarks.{scout.Landmark}");
		return 50f + Traits.Exploring * 30f;
	}

	public override void Start()
	{
		base.Start();
		destination = ChooseDestination();
		BotDiagnostics.Log(Bot, $"scouting for {step.Landmark} toward {destination.ToTileCoordinates()}");
	}

	Vector2 ChooseDestination()
	{
		var here = Bot.CenterTile;
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		if (step.Landmark == Landmark.Underworld)
			return new Vector2(here.X * 16f, (Main.UnderworldLayer + UnderworldDepth) * 16f);
		if (step.Underground && knowledge.SpanOf(step.Landmark) is var (minX, maxX))
		{
			int column = Main.rand.Next(minX - SpanMargin, maxX + SpanMargin + 1);
			return new Vector2(column * 16f, (TileQuery.SurfaceY(column) + Main.rand.Next(MinShrineDepth, MaxShrineDepth)) * 16f);
		}
		int x = here.X + knowledge.ScoutDirection * SurfaceLeg;
		if (x < WorldEdgeMargin || x > Main.maxTilesX - WorldEdgeMargin)
		{
			knowledge.ScoutDirection = -knowledge.ScoutDirection;
			x = Utils.Clamp(x, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		}
		return new Vector2(x * 16f, (TileQuery.SurfaceY(x) - 2) * 16f);
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
			bool found = BossRituals.KnowsShrine(Bot, step.Landmark);
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
	const float StandTolerance = 16f * 3f;

	BuildBridgeStep step;

	public BuildBridgeTask(Bot bot) : base(bot) { }

	public override string NameKey => "BuildBridge";

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
		if (frontier == (direction > 0 ? bridge.RightEnd : bridge.LeftEnd) && bridge.IsBuilt(frontier))
		{
			IsFinished = true;
			return;
		}
		var stand = new Vector2(PathFinder.CenterXOf(frontier - 2 * direction), (bridge.Y + 1) * 16f - 21f);
		if (Vector2.Distance(Bot.Center, stand) > StandTolerance)
		{
			if (!TravelOrTunnel(stand, StandTolerance * 0.5f))
			{
				BotDiagnostics.Log(Bot, $"cannot reach bridge frontier {frontier}; motor: {Bot.Motor.Describe()}{TerrainDump.Around(new Point(frontier, bridge.Y), 16, 8)}");
				IsFinished = true;
			}
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(new Vector2(frontier * 16f, bridge.Y * 16f));
		foreach (int column in new[] { frontier, frontier + direction })
		{
			for (int y = bridge.Y - 2; y <= bridge.Y; y++)
			{
				if (TileQuery.IsSolid(column, y))
				{
					Bot.Hands.Dig(new Point(column, y));
					return;
				}
			}
		}
		if (bridge.IsDeck(frontier))
			return;
		var deck = new Point(frontier, bridge.Y + 1);
		if (TileQuery.HasTile(deck))
		{
			if (Main.tileCut[Main.tile[deck.X, deck.Y].TileType])
				Bot.Hands.Cut(deck);
			else
				Bot.Hands.Dig(deck);
			return;
		}
		if (Bot.Hands.PlaceBridge(deck) || (Main.tile[deck.X, deck.Y].LiquidAmount > 0 && Bot.Hands.PlaceScaffold(deck)))
			Bot.Watchdog.RecordProgress();
		else if (Bot.Hands.Ready && Main.GameUpdateCount % 300 == 0)
			BotDiagnostics.Log(Bot, $"cannot place bridge deck at {frontier},{bridge.Y + 1}; material={Bot.Hands.HasBridgeMaterial} tile={Main.tile[frontier, bridge.Y + 1].HasTile} liquid={Main.tile[frontier, bridge.Y + 1].LiquidAmount}");
	}
}

public sealed class DigShaftTask : BotTask
{
	const float WorkReach = 16f * 4f;

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
		if (row >= Main.UnderworldLayer)
		{
			IsFinished = true;
			return;
		}
		if (Hellevator.LavaBelow(shaft.X, row))
		{
			BotDiagnostics.Log(Bot, $"hellevator stops above lava at row {row}");
			IsFinished = true;
			return;
		}
		var workSpot = new Vector2(PathFinder.CenterXOf(shaft.X), row * 16f - 21f);
		bool withinReach = Math.Abs(Bot.Center.X - workSpot.X) < 24f && Math.Abs(workSpot.Y - Bot.Center.Y) < WorkReach;
		if (!withinReach)
		{
			if (!TravelOrTunnel(workSpot, 12f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		foreach (int column in new[] { shaft.X, shaft.X + 1 })
		{
			var cell = new Point(column, row);
			if (TileQuery.IsSolid(cell.X, cell.Y) || (TileQuery.HasTile(cell) && !TileQuery.IsPlatform(cell.X, cell.Y)))
			{
				Bot.Hands.Dig(cell);
				return;
			}
		}
		if (shaft.NeedsPlatform(row) && !shaft.HasPlatform(row))
		{
			foreach (int column in new[] { shaft.X, shaft.X + 1 })
			{
				if (Bot.Hands.PlaceBridge(new Point(column, row)))
				{
					Bot.Watchdog.RecordProgress();
					return;
				}
			}
			var anchor = new Point(shaft.X - 1, row);
			if (!TileQuery.HasTile(anchor) && Bot.Hands.PlaceScaffold(anchor))
				return;
		}
		if (Bot.Hands.Ready && Main.GameUpdateCount % 300 == 0)
			BotDiagnostics.Log(Bot, $"shaft row {row} idle: needsPlatform={shaft.NeedsPlatform(row)} hasPlatform={shaft.HasPlatform(row)} material={Bot.Hands.HasBridgeMaterial} place0={Bot.Hands.DescribeBridgePlacement(new Point(shaft.X, row))} place1={Bot.Hands.DescribeBridgePlacement(new Point(shaft.X + 1, row))} botFeet={PathFinder.FeetOf(Bot.Npc)} cells={TileQuery.TypeAt(new Point(shaft.X, row))}/{TileQuery.TypeAt(new Point(shaft.X + 1, row))}{TerrainDump.Around(new Point(shaft.X, row), 6, 4)}");
	}
}
