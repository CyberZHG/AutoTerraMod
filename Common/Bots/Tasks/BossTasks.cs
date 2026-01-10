using System.Linq;
using AutoTerraMod.Common.Bosses;
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

	public SummonTask(Bot bot) : base(bot) { }

	public override string NameKey => "Summon";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SummonBossStep summon)
			return 0f;
		step = summon;
		Detail = summon.Challenge.Name;
		return 85f;
	}

	public override void Start()
	{
		base.Start();
		travelFailures = 0;
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
		Bot.Motor.Stop();
		Bot.Watchdog.RecordProgress();
		var team = BotWorldSystem.Bots.Where(member => member.FactionId == Bot.FactionId).ToList();
		int present = team.Count(member => member.IsAlive && Vector2.Distance(member.Center, Bot.Center) < GatherRadius);
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
		int present = team.Count(member => member.IsAlive && Vector2.Distance(member.Center, step.Place) < GatherRadius);
		bool stillWaiting = Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until;
		if (present == team.Count)
			return true;
		if (stillWaiting)
		{
			Bot.Watchdog.RecordProgress();
			return false;
		}
		return present * 2 >= team.Count || GiveUpRally(present, team.Count);
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
		int slot = System.Array.FindIndex(Avatar.armor, FirstAccessorySlot, AccessorySlots, item => item.IsAir);
		if (doll == null || slot < 0)
			return false;
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
					NPC.SpawnOnPlayer(Avatar.whoAmI, step.Challenge.BossType);
				Inventory.ConsumeOne(Avatar, summonItem);
				return true;
			case SmashOrbsTrigger when step.Shrine is Point shrine:
				Bot.Motor.Face(ritualTarget);
				var result = Bot.Hands.Smash(shrine);
				if (result is ToolResult.Unbreakable or ToolResult.NoTool)
					return Abandon($"cannot smash shrine at {shrine}: {result}");
				return result == ToolResult.Done;
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

	public RallyTask(Bot bot) : base(bot) { }

	public override string NameKey => "Rally";

	public override float Evaluate() =>
		Bot.Faction.Rally is { } rally && Main.GameUpdateCount < rally.until && Bot.Brain.Plan is not SummonBossStep ? 88f : 0f;

	public override void Tick()
	{
		if (Bot.Faction.Rally is not { } rally || Main.GameUpdateCount >= rally.until)
		{
			IsFinished = true;
			return;
		}
		var leader = BotWorldSystem.FindBot(Bot.Faction.RallyLeaderId);
		var target = leader is { IsAlive: true } && leader != Bot ? leader.Center : rally.point;
		TravelOrTunnel(target, ArrivalRadius);
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
			bool found = BossRituals.KnowsShrine(Bot);
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
