using System.Linq;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public abstract class BotTask
{
	protected BotTask(Bot bot) => Bot = bot;

	protected Bot Bot { get; }
	protected Player Avatar => Bot.Avatar;
	protected Personality Traits => Bot.Personality;
	protected float HealthFraction => Avatar.statLifeMax2 > 0 ? (float)Avatar.statLife / Avatar.statLifeMax2 : 1f;

	public abstract string NameKey { get; }
	public string Detail { get; protected set; } = "";
	public bool IsFinished { get; protected set; }
	public bool Accomplished { get; protected set; }
	public virtual bool IsUrgent => false;

	public abstract float Evaluate();

	string probeLabel, evaluateLabel;
	public string ProbeLabel => probeLabel ??= GetType().Name;
	public string EvaluateLabel => evaluateLabel ??= "eval:" + GetType().Name;

	public virtual void Start() => IsFinished = false;

	public void Begin()
	{
		Accomplished = false;
		Start();
	}

	public abstract void Tick();

	public virtual void Stop() => Bot.Motor.Stop();

	const float MaxTunnelDistance = 16f * 200f;
	const float PylonServesTiles = 350f;
	const float TunnelLegPixels = 16f * 40f;
	(Vector2 leg, Vector2 target)? legTunnel;

	static readonly int[] RecallItems = { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone, ItemID.RecallPotion };

	public static bool IsRecall(Item item) => RecallItems.Contains(item.type);

	public static bool CarriesRecall(Player avatar) => Inventory.Items(avatar).Any(item => RecallItems.Contains(item.type));

	const uint RelogGapTicks = 2 * 3600;

	public static bool CanRecall(Bot bot) => CarriesRecall(bot.Avatar) || bot.LastRecallAt == 0 || Main.GameUpdateCount - bot.LastRecallAt >= RelogGapTicks;

	public static Item KeptRecall(Player avatar) =>
		Inventory.Items(avatar).Where(IsRecall).MaxBy(item => item.type == ItemID.RecallPotion ? -1 : System.Array.IndexOf(RecallItems, item.type));

	protected bool TryRecallToCamp()
	{
		bool viaOutpost = Bot.OutpostBed != null && !RecallLandsAtBase();
		uint previousRecall = Bot.LastRecallAt;
		if (!TryRecallHome())
			return false;
		if (viaOutpost)
		{
			LeaveOutpostIfBedside();
			if (Bot.OutpostBed == null)
			{
				uint firstRecall = Bot.LastRecallAt;
				Bot.LastRecallAt = previousRecall;
				if (!TryRecallHome())
					Bot.LastRecallAt = firstRecall;
			}
		}
		return true;
	}

	const float RecallGainTiles = 40f;

	protected bool RecallBringsCloser(Vector2 target) =>
		Vector2.Distance(CampLanding, target) + RecallGainTiles * 16f < Vector2.Distance(Bot.Center, target);

	protected Vector2 CampLanding => Bot.OutpostBed != null && !RecallLandsAtBase() && !KeepsArenaBed ? Bot.Faction.BaseFeet : BotWorldSystem.RespawnFeet(Bot);

	bool KeepsArenaBed => Bot.OutpostBed is Point bed && bed == Bot.Faction.ArenaBed && Bot.Faction.Rally != null;

	protected bool RecallReachesCamp() => RecallLandsAtBase() || Bot.OutpostBed != null && !KeepsArenaBed;

	const int PointlessRecallTiles = 8;
	const uint MinRecallGapTicks = 300;

	protected bool TryRecallHome()
	{
		var recall = KeptRecall(Avatar);
		var body = Bot.Npc;
		if (body == null || Vector2.Distance(body.Bottom, BotWorldSystem.RespawnFeet(Bot)) < PointlessRecallTiles * 16f
			|| Bot.LastRecallAt != 0 && Main.GameUpdateCount - Bot.LastRecallAt < MinRecallGapTicks
			|| recall == null && !CanRecall(Bot))
			return false;
		string name = recall?.Name ?? "a relog";
		if (recall?.consumable == true)
			Inventory.ConsumeOne(Avatar, recall);
		if (recall != null)
			SoundEngine.PlaySound(SoundID.Item6, body.Center);
		var home = BotWorldSystem.RespawnFeet(Bot);
		if (Bot.Grapple.IsAttached)
			Bot.Grapple.Release(body, jump: false);
		Bot.LastRecallAt = Main.GameUpdateCount;
		body.position = home - new Vector2(body.width / 2f, body.height);
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Motor.Stop();
		BotDiagnostics.Log(Bot, $"used {name} to return home");
		if (recall != null)
			Spectating.SpectatorSystem.AnnounceJump(Bot, "RecalledHome", Bot.Name, name);
		else
			Spectating.SpectatorSystem.AnnounceJump(Bot, "RelogHome", Bot.Name);
		return true;
	}

	const float AspirationCap = 25f;

	protected float Aspire(float score) => Bosses.BossCredit.AllBossesDone(Bot.Faction) ? score : System.Math.Min(score, AspirationCap);

	const float BedsideReach = 16f * 4f;

	protected void LeaveOutpostIfBedside()
	{
		if (Bot.OutpostBed is not Point bed || bed == Bot.Faction.ArenaBed && Bot.Faction.Rally != null || Vector2.Distance(Bot.Center, bed.ToWorldCoordinates()) > BedsideReach)
			return;
		Bot.OutpostBed = null;
		BotDiagnostics.Log(Bot, $"cancelled the respawn point at the outpost bed {bed} on the way home");
	}

	protected bool RecallLandsAtBase()
	{
		var landing = BotWorldSystem.RespawnFeet(Bot).ToTileCoordinates();
		return Bot.Faction.Settlement?.Contains(landing.X, landing.Y - 1) != false;
	}

	protected bool TryRandomTeleport()
	{
		var potion = Inventory.Items(Avatar).FirstOrDefault(item => item.type == ItemID.TeleportationPotion);
		if (potion == null || Bot.Npc is not NPC body)
			return false;
		var before = Avatar.position;
		using (AvatarPresence.Enter())
			Avatar.TeleportationPotion();
		if (Avatar.position == before)
			return false;
		Inventory.ConsumeOne(Avatar, potion);
		body.Bottom = Avatar.Bottom;
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Motor.Stop();
		BotDiagnostics.Log(Bot, $"drank a Teleportation Potion and landed at {Bot.CenterTile}");
		return true;
	}

	Vector2? campExit;

	const float LongTripTiles = 150f;
	const float RecallSavesTiles = 100f;

	static float Walk(Vector2 from, Vector2 to) => (System.Math.Abs(from.X - to.X) + System.Math.Abs(from.Y - to.Y)) / 16f;

	protected const uint RecallCooldownTicks = 1800;
	const uint SameTripTicks = 18000;
	const float SameTripReach = 16f * 32f;

	const uint RecallCheckTicks = 60;
	const uint RoadCheckTicks = 20;
	const float SameTargetPixels = 32f;
	(Vector2 target, uint at, bool beats)? recallChecked;
	(Vector2 target, uint at, Vector2? road)? roadChecked;

	bool CachedRecallBeats(Vector2 target)
	{
		if (recallChecked is var (checkedTarget, at, beats) && Main.GameUpdateCount - at < RecallCheckTicks && Vector2.DistanceSquared(checkedTarget, target) < SameTargetPixels * SameTargetPixels)
			return beats;
		bool result = RecallBeatsLongTrip(target);
		recallChecked = (target, Main.GameUpdateCount, result);
		return result;
	}

	const float ReachedWaypointPixels = 16f * 6f;

	Vector2? CachedHighwayRoute(Vector2 target, out bool recalled)
	{
		recalled = false;
		if (roadChecked is var (checkedTarget, at, road) && Main.GameUpdateCount - at < RoadCheckTicks && Vector2.DistanceSquared(checkedTarget, target) < SameTargetPixels * SameTargetPixels)
			return road;
		var result = HighwayRoute(target, out recalled);
		if (result is Vector2 waypoint && Vector2.Distance(waypoint, Bot.Center) < ReachedWaypointPixels)
			result = null;
		roadChecked = recalled ? null : (target, Main.GameUpdateCount, result);
		return result;
	}

	bool RecallBeatsLongTrip(Vector2 target)
	{
		if (!CanRecall(Bot) || Main.GameUpdateCount - Bot.LastRecallAt < RecallCooldownTicks)
			return false;
		if (Bot.LastRecallTarget is Vector2 earlier && Vector2.Distance(earlier, target) < SameTripReach && Main.GameUpdateCount - Bot.LastRecallAt < SameTripTicks)
			return false;
		float fromHere = Vector2.Distance(Bot.Center, target) / 16f;
		if (fromHere < LongTripTiles)
			return false;
		fromHere = Walk(Bot.Center, target);
		float fromHome = Walk(BotWorldSystem.RespawnFeet(Bot), target);
		return fromHere - fromHome > RecallSavesTiles;
	}

	protected bool PlugLavaAround(Point tile)
	{
		if (Bot.Hands.Explosives.IsFused)
		{
			Bot.Motor.Stop();
			return true;
		}
		if (TileQuery.TouchesLava(tile.X, tile.Y) && Bot.Hands.Explosives.Carries && Explosives.CanBlast(tile.X, tile.Y) && Bot.Hands.Explosives.Light(tile))
		{
			BotDiagnostics.Log(Bot, $"blasting {tile} next to lava");
			Bot.Watchdog.RecordProgress();
			return true;
		}
		foreach (var next in new[] { new Point(tile.X - 1, tile.Y), new Point(tile.X + 1, tile.Y), new Point(tile.X, tile.Y - 1), new Point(tile.X, tile.Y + 1) })
		{
			if (!TileQuery.IsDangerousLiquid(next.X, next.Y) || TileQuery.HasTile(next))
				continue;
			var plug = Inventory.Items(Avatar).Where(Building.Outpost.IsBlock).OrderBy(item => item.type == ItemID.StoneBlock ? 0 : 1).FirstOrDefault();
			if (plug == null)
				return false;
			if (!Bot.Hands.CanReach(plug, next))
			{
				Bot.Motor.MoveTo(next.ToWorldCoordinates(), 24f);
				return !Bot.Motor.IsStuck;
			}
			if (!Bot.Hands.Ready)
				return true;
			if (!Bot.Hands.Place(plug, next))
			{
				BotDiagnostics.Log(Bot, $"cannot plug lava at {next} with {plug.Name}");
				return false;
			}
			Main.tile[next.X, next.Y].LiquidAmount = 0;
			BotDiagnostics.Log(Bot, $"plugged lava at {next} to clear the corridor");
			Bot.Watchdog.RecordProgress();
			return true;
		}
		return false;
	}

	const float ShaftDropMargin = 16f * 6f;
	const int ShaftDropRoom = 3;
	const int ShaftUsefulDepth = 20;
	const float ShaftDropFinish = 16f;
	const float ShaftCentring = 5f;
	protected const int ShaftMouthReach = 20;

	const float NudgePerTick = 1.5f;
	const float ShaftSlide = 8f;
	const int ShaftMouthHeadroom = 6;
	const int ShaftCatchment = 80;
	const int ShaftCatchmentDepth = 60;

	const int LavaSafetyRows = 4;
	protected const int SealingSafetyRows = 1;
	protected bool sealingLava;

	protected static bool LavaBelow(World.Hellevator shaft, int feetRow, int rows = LavaSafetyRows)
	{
		for (int y = feetRow; y <= feetRow + rows; y++)
			for (int x = shaft.X - 1; x <= shaft.Right + 1; x++)
				if (WorldGen.InWorld(x, y, 2) && Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == Terraria.ID.LiquidID.Lava && !World.TileQuery.IsSolid(x, y))
					return true;
		return false;
	}

	protected static int AboveLava(World.Hellevator shaft, int row)
	{
		for (int y = shaft.TopY + 1; y <= row + LavaSafetyRows; y++)
			for (int x = shaft.X; x <= shaft.Right; x++)
				if (Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == Terraria.ID.LiquidID.Lava && !World.TileQuery.IsSolid(x, y))
					return System.Math.Min(row, y - LavaSafetyRows - 1);
		return row;
	}

	protected void CentreIn(World.Hellevator shaft)
	{
		Bot.Motor.Stop();
		if (Bot.Npc is not NPC body)
			return;
		if (Bot.Grapple.IsAttached)
			Bot.Grapple.Release(body, jump: false);
		float offset = (shaft.X + 1) * 16f - Bot.Center.X;
		if (System.Math.Abs(offset) >= ShaftCentring)
			body.position.X += System.Math.Clamp(offset, -NudgePerTick, NudgePerTick);
		else if (body.velocity.Y == 0f && !LavaBelow(shaft, (int)((body.position.Y + body.height) / 16f), sealingLava ? SealingSafetyRows : LavaSafetyRows))
		{
			Bot.Motor.DropThroughPlatform();
			int footRow = (int)((body.position.Y + body.height + ShaftSlide) / 16f);
			if (!TileQuery.IsSolid(shaft.X, footRow) && !TileQuery.IsSolid(shaft.X + 1, footRow))
			{
				body.position.Y += ShaftSlide;
				Bot.Vitals.Teleported(body);
			}
		}
		Bot.Watchdog.RecordProgress();
	}

	bool shaftDropping;

	bool ShaftShortcut(Vector2 target)
	{
		bool wasDropping = shaftDropping;
		shaftDropping = false;
		if (Navigation.PathFinder.InsideWalledMaze(target.ToTileCoordinates()))
			return false;
		if (TravelShaft is not { } shaft || target.Y < Bot.Center.Y + ShaftDropFinish || shaft.FirstImpassableRow() - shaft.TopY < ShaftUsefulDepth)
			return false;
		float centerX = (shaft.X + 1) * 16f;
		var tile = Bot.CenterTile;
		bool inShaft = tile.X >= shaft.X && tile.X <= shaft.Right && tile.Y >= shaft.TopY - ShaftMouthHeadroom && tile.Y < shaft.FirstImpassableRow() - ShaftDropRoom;
		if (inShaft && (target.Y >= Bot.Center.Y + ShaftDropMargin || wasDropping && Bot.Npc?.velocity.Y > 0f))
		{
			shaftDropping = true;
			CentreIn(shaft);
			return true;
		}
		var targetTile = target.ToTileCoordinates();
		bool intoShaft = targetTile.X >= shaft.X - 1 && targetTile.X <= shaft.Right + 1 && targetTile.Y > shaft.TopY
			|| System.Math.Abs(targetTile.X - shaft.X) < ShaftCatchment && targetTile.Y > shaft.TopY + ShaftCatchmentDepth;
		if (!(intoShaft || shaft.IsComplete && target.Y >= Main.UnderworldLayer * 16f) || tile.Y > shaft.TopY + ShaftMouthReach)
			return false;
		Bot.Motor.MoveTo(new Vector2(centerX, shaft.TopY * 16f - 21f), 8f);
		return !Bot.Motor.IsStuck;
	}

	protected bool AtArenaBed()
	{
		if (Building.Outpost.ArenaBedFor(Bot.Faction) is not Point arena || Bot.OutpostBed == arena || Building.Outpost.TakeArenaBed(Bot))
			return true;
		return !TravelOrTunnel(Building.Outpost.Bedside(arena), Building.Outpost.BedsideReach * 0.5f);
	}

	World.Hellevator borrowedShaft;

	World.Hellevator TravelShaft => borrowedShaft ?? Bot.Faction.Hellevator;

	void ChooseTravelShaft(Vector2 target)
	{
		borrowedShaft = null;
		int x = Bot.CenterTile.X;
		var nearest = BotWorldSystem.Factions.Select(faction => faction.Hellevator).Where(shaft => shaft is { IsComplete: true }).MinBy(shaft => System.Math.Abs(shaft.RopeX - x));
		if (target.Y < Main.UnderworldLayer * 16f)
		{
			bool inNearest = nearest != null && nearest.Contains(Bot.CenterTile.X, Bot.CenterTile.Y);
			bool deepBelowCamp = (InAnyCamp(Bot.CenterTile) || inNearest) && target.Y / 16f > Bot.CenterTile.Y + (inNearest ? 0 : DeepDetourRows);
			if (deepBelowCamp && nearest != null && (Bot.Faction.Hellevator is not { } own || System.Math.Abs(own.RopeX - x) > MaxSidewaysTunnel))
				borrowedShaft = nearest;
			return;
		}
		if (Bot.Faction.Hellevator is { IsComplete: true })
			return;
		borrowedShaft = nearest;
	}

	protected bool TravelOrTunnel(Vector2 target, float radius)
	{
		ChooseTravelShaft(target);
		if (campExit != null && Bot.Motor.Tunneling && !Bot.Motor.HasArrived && !Bot.Motor.IsStuck)
			return true;
		if (deepTunnel is Vector2 tunnel && Vector2.DistanceSquared(tunnel, target) < 32f * 32f && Bot.Motor.Tunneling && !Bot.Motor.HasArrived && !Bot.Motor.IsStuck)
			return true;
		deepTunnel = null;
		if (legTunnel is var (_, forTarget) && Vector2.DistanceSquared(forTarget, target) < 32f * 32f && Bot.Motor.Tunneling && !Bot.Motor.HasArrived && !Bot.Motor.IsStuck
			&& Main.GameUpdateCount - tunnelingSince <= TunnelPathRetryTicks)
			return true;
		legTunnel = null;
		if (TempleApproach(target) is Vector2 approach)
			target = approach;
		if (CachedRecallBeats(target) && TryRecallHome())
		{
			Bot.LastRecallTarget = target;
			return true;
		}
		var road = CachedHighwayRoute(target, out bool recalled);
		if (recalled)
		{
			Bot.LastRecallTarget = target;
			return true;
		}
		if (road is Vector2 onRoad)
			target = onRoad;
		else if (ShaftShortcut(target))
			return true;
		else if (BridgeLeg(target) is Vector2 leg)
			target = leg;
		else if (SurfaceFirst(target) is Vector2 overland)
			target = overland;
		campExit = null;
		if (Bot.Motor.Tunneling && Main.GameUpdateCount - tunnelingSince > TunnelPathRetryTicks)
			Bot.Motor.Stop();
		if (InOwnShaft() && LeavesShaft(target) && System.Math.Abs(target.ToTileCoordinates().Y - Bot.CenterTile.Y) > ShaftLevelRows && DeepDetour(target, radius))
			return true;
		if (InOwnShaft() && LeavesShaft(target) && target.ToTileCoordinates().Y < Bot.CenterTile.Y - ShaftClimbLeg / 2 && TravelShaft is { } climbed)
		{
			int row = System.Math.Max(climbed.TopY - 2, Bot.CenterTile.Y - ShaftClimbLeg);
			Bot.Motor.MoveTo(new Vector2((climbed.X + 1) * 16f, row * 16f), 16f);
			return !Bot.Motor.IsStuck;
		}
		if (target.Y >= Main.UnderworldLayer * 16f && Bot.CenterTile.Y < Main.UnderworldLayer && !InOwnShaft() && TravelShaft is { IsComplete: true } && DeepDetour(target, radius))
			return true;
		Bot.Motor.MoveTo(target, radius);
		if (!Bot.Motor.IsStuck)
			return true;
		if (Bot.Motor.Tunneling || Bot.Hands.BestPickaxe == null || Navigation.PathFinder.InsideWalledMaze(target.ToTileCoordinates()))
			return false;
		if ((InAnyCamp(Bot.CenterTile) || InAnyCamp(target.ToTileCoordinates())) && ++campRetries <= CampRetriesBeforeTunnel)
		{
			Bot.Motor.Stop();
			Bot.Motor.MoveTo(target, radius);
			return true;
		}
		campRetries = 0;
		bool beneath = BelowWithinReach(target);
		if (beneath && InAnyCamp(Bot.CenterTile) && DeepDetour(target, radius))
			return true;
		if (!beneath && (Vector2.Distance(Bot.Center, target) >= MaxTunnelDistance || InOwnShaft()) && DeepDetour(target, radius))
			return true;
		if (!beneath && Vector2.Distance(Bot.Center, target) >= MaxTunnelDistance)
		{
			if (Bot.CenterTile.Y > TileQuery.SurfaceY(Bot.CenterTile.X) + SurfaceFirstDepth && PylonTravel.Serves(BotWorldSystem.RespawnFeet(Bot), target, PylonServesTiles * 16f) && TryRecallHome())
			{
				BotDiagnostics.Log(Bot, $"far underground from {target.ToTileCoordinates()}, recalled home to travel by pylon");
				Bot.LastRecallTarget = target;
				return true;
			}
			var tunnelLeg = Bot.Center + Vector2.Normalize(target - Bot.Center) * TunnelLegPixels;
			if (World.Hellbridge.PassageAbove(Bot.CenterTile) is Point passage && target.Y < Bot.Center.Y)
				tunnelLeg = passage.ToWorldCoordinates();
			else if (!Navigation.PathFinder.InsideWalledMaze(target.ToTileCoordinates()) && CrossesWalledMaze(Bot.Center, tunnelLeg))
				tunnelLeg = new Vector2(Bot.Center.X, (TileQuery.SurfaceY(Bot.CenterTile.X) - 2) * 16f);
			BotDiagnostics.Log(Bot, $"stuck far from {target.ToTileCoordinates()}, tunneling a leg toward {tunnelLeg.ToTileCoordinates()}");
			legTunnel = (tunnelLeg, target);
			tunnelingSince = Main.GameUpdateCount;
			Bot.Motor.TunnelTo(tunnelLeg, 24f);
			return true;
		}
		if (CampExitAbove(target) is Vector2 exit)
		{
			BotDiagnostics.Log(Bot, $"stuck under the camp, tunneling out to {exit.ToTileCoordinates()}");
			campExit = exit;
			Bot.Motor.TunnelTo(exit, 32f);
			return true;
		}
		if (Navigation.PathFinder.InsideMaze(target.ToTileCoordinates()))
			return false;
		BotDiagnostics.Log(Bot, $"no walkable route to {target.ToTileCoordinates()}, tunneling");
		tunnelingSince = Main.GameUpdateCount;
		Bot.Motor.TunnelTo(target, radius);
		return true;
	}

	static bool CrossesWalledMaze(Vector2 from, Vector2 to)
	{
		int samples = (int)(Vector2.Distance(from, to) / (16f * 4f)) + 1;
		for (int index = 0; index <= samples; index++)
		{
			if (Navigation.PathFinder.InsideWalledMaze(Vector2.Lerp(from, to, index / (float)samples).ToTileCoordinates()))
				return true;
		}
		return false;
	}

	const int TempleArrival = 6;
	const int GuideHop = 30;
	const int DoorStepInside = 2;

	Vector2? TempleApproach(Vector2 target)
	{
		var goal = target.ToTileCoordinates();
		var here = Bot.CenterTile;
		if (!Navigation.PathFinder.InsideMaze(goal))
			return null;
		if (ViaTempleShaft(goal, here) is Vector2 shaftLeg)
			return shaftLeg;
		if (World.TempleDoor.Near(goal) is Point tunnelDoor && World.TempleDoor.OutsideSide(tunnelDoor) is int tunnelSide && World.TempleDoor.Mouth(tunnelDoor) is Point tunnelMouth
			&& (World.TempleDoor.InTunnel(tunnelDoor, here) || System.Math.Abs(here.X - tunnelMouth.X) + System.Math.Abs(here.Y - tunnelMouth.Y) <= TempleArrival))
		{
			var doorstep = World.TempleDoor.InsideStep(tunnelDoor, -tunnelSide, DoorStepInside);
			if (System.Math.Abs(here.X - doorstep.X) + System.Math.Abs(here.Y - doorstep.Y) <= TempleArrival)
				return World.TempleDoor.InsideStep(tunnelDoor, tunnelSide, DoorStepInside).ToWorldCoordinates(8f, 0f);
			return World.TempleDoor.GuideStep(doorstep, here, GuideHop) is Point hop && hop != here ? hop.ToWorldCoordinates(8f, 0f) : doorstep.ToWorldCoordinates(8f, 0f);
		}
		if (Navigation.PathFinder.InsideMaze(here))
		{
			if (System.Math.Abs(goal.X - here.X) + System.Math.Abs(goal.Y - here.Y) <= GuideHop)
				return null;
			return World.TempleDoor.GuideStep(goal, here, GuideHop) is Point step && step != here ? step.ToWorldCoordinates(8f, 0f) : null;
		}
		var door = World.TempleDoor.Near(goal);
		var mouth = door is Point found ? World.TempleDoor.Mouth(found) : null;
		if (Main.GameUpdateCount - templeLoggedAt > TempleLogTicks)
		{
			templeLoggedAt = Main.GameUpdateCount;
			BotDiagnostics.Log(Bot, $"heading into the temple for {goal}: door {door?.ToString() ?? "none"}, mouth {mouth?.ToString() ?? "none"}");
		}
		if (door is not Point doorTile || mouth is not Point mouthTile)
			return null;
		return TempleLeg(here, doorTile, mouthTile, World.TempleDoor.OutsideSide(doorTile) ?? System.Math.Sign(mouthTile.X - doorTile.X));
	}

	const int ShaftServesTiles = 120;
	const int ShaftBottomArrival = 4;

	static Vector2? ViaTempleShaft(Point goal, Point here)
	{
		if (World.TempleShaft.Current is not { IsComplete: true } shaft || System.Math.Abs(goal.X - shaft.RopeX) + System.Math.Abs(goal.Y - shaft.TopY) > ShaftServesTiles)
			return null;
		if (shaft.Contains(here))
			return here.Y >= shaft.TopY ? shaft.Top : null;
		if (Navigation.PathFinder.InsideMaze(here))
			return null;
		bool atBottom = System.Math.Abs(here.X - shaft.RopeX) <= ShaftBottomArrival && System.Math.Abs(here.Y - shaft.BottomY) <= ShaftBottomArrival;
		return atBottom ? shaft.Top : shaft.Bottom;
	}

	uint templeLoggedAt;
	const uint TempleLogTicks = 3600;

	static Vector2 TempleLeg(Point here, Point door, Point mouth, int side)
	{
		Point leg;
		if (System.Math.Abs(mouth.X - here.X) > TempleArrival && here.Y < mouth.Y - TempleArrival)
			leg = new Point(mouth.X, here.Y);
		else if (System.Math.Abs(mouth.X - here.X) + System.Math.Abs(mouth.Y - here.Y) > TempleArrival)
			leg = mouth;
		else
			leg = World.TempleDoor.InsideStep(door, side, DoorStepInside);
		return leg.ToWorldCoordinates(8f, 0f);
	}

	const int BridgeRowSlack = 4;
	const int BridgeLegTiles = 100;
	const int BridgeLegThreshold = 120;
	const int BridgeLegGrid = 50;

	Vector2? BridgeLeg(Vector2 target)
	{
		var here = Bot.CenterTile;
		var goal = target.ToTileCoordinates();
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (faction.Hellbridge is { } bridge && Along(bridge.Y, here, goal))
				return Leg(here, goal, x => bridge.FeetRow(x));
			foreach (var highway in World.Highway.For(faction))
			{
				if (Along(highway.Y, here, goal) && highway.Spans(here.X) && highway.Spans(goal.X))
					return Leg(here, goal, _ => highway.Y);
			}
		}
		return null;
	}

	const int SurfaceFirstSpan = 150;

	bool BelowWithinReach(Vector2 target)
	{
		var goal = target.ToTileCoordinates();
		var here = Bot.CenterTile;
		return goal.Y < Main.UnderworldLayer && goal.Y - here.Y > System.Math.Abs(goal.X - here.X) && System.Math.Abs(goal.X - here.X) <= SurfaceFirstSpan && !InOwnShaft();
	}
	const int SurfaceFirstDepth = 60;

	Vector2? SurfaceFirst(Vector2 target)
	{
		var goal = target.ToTileCoordinates();
		var here = Bot.CenterTile;
		if (goal.Y >= Main.UnderworldLayer || System.Math.Abs(goal.X - here.X) <= SurfaceFirstSpan || OnCorridorRow(goal))
			return null;
		int surface = TileQuery.SurfaceY(goal.X);
		if (goal.Y < surface + SurfaceFirstDepth || here.Y >= TileQuery.SurfaceY(here.X) + SurfaceFirstDepth)
			return null;
		return new Vector2(goal.X * 16f + 8f, (surface - 2) * 16f);
	}

	static bool OnCorridorRow(Point goal)
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			if (faction.Hellbridge is { } bridge && System.Math.Abs(goal.Y - bridge.Y) <= BridgeRowSlack)
				return true;
			if (World.Highway.For(faction).Any(highway => System.Math.Abs(goal.Y - highway.Y) <= BridgeRowSlack && highway.Spans(goal.X)))
				return true;
		}
		return false;
	}

	const int HighwayTripTiles = 200;
	const int FarHighwayTripTiles = 300;
	const int HighwayWorthTiles = 100;
	const int HighwayEntryReach = 100;
	const int HighwaySurfaceDepth = 60;
	const int HighwayExitSlack = 3;
	const int HighwayEntryArrival = 10;
	const int HighwayLegKeep = 30;

	Vector2? HighwayRoute(Vector2 target, out bool recalled)
	{
		recalled = false;
		var here = Bot.CenterTile;
		var goal = target.ToTileCoordinates();
		bool deepGoal = goal.Y > Main.worldSurface + HighwaySurfaceDepth;
		if (System.Math.Abs(goal.X - here.X) < HighwayTripTiles || deepGoal && here.Y > Main.worldSurface + HighwaySurfaceDepth)
			return null;
		if (goal.Y >= Main.UnderworldLayer && here.Y < Main.UnderworldLayer && TravelShaft is { IsComplete: true })
			return null;
		int baseX = Bot.Faction.BaseTile.X;
		bool bossSide = (here.X - baseX) * System.Math.Sign(goal.X - baseX) >= 0 || System.Math.Abs(goal.X - here.X) > FarHighwayTripTiles;
		int tripLow = System.Math.Min(here.X, goal.X), tripHigh = System.Math.Max(here.X, goal.X);
		NoteLeftRoad(here);
		Highway best = null;
		Point bestEntry = default;
		(int low, int high) bestSpan = default;
		float bestDistance = float.MaxValue;
		foreach (var highway in Highway.All)
		{
			if (highway.BuiltSpan is not var (low, high))
				continue;
			if (System.Math.Abs(here.Y - highway.Y) <= BridgeRowSlack && here.X >= low && here.X <= high && bossSide)
			{
				int exit = Utils.Clamp(goal.X, low, high);
				if (System.Math.Abs(exit - here.X) <= HighwayExitSlack)
					continue;
				roadRow = (highway, Main.GameUpdateCount);
				return Leg(here, new Point(exit, highway.Y), _ => highway.Y) ?? new Vector2(exit * 16f + 8f, (highway.Y + 1) * 16f - 21f);
			}
			int overlap = System.Math.Min(high, tripHigh) - System.Math.Max(low, tripLow);
			if (overlap < HighwayWorthTiles)
				continue;
			var entry = new Point(Utils.Clamp(here.X, low, high), highway.Y);
			float distance = Vector2.Distance(Bot.Center, entry.ToWorldCoordinates());
			if (distance < bestDistance)
			{
				best = highway;
				bestEntry = entry;
				bestSpan = (low, high);
				bestDistance = distance;
			}
		}
		if (best == null)
			return null;
		bool onLeg = roadRow is var (kept, at) && kept == best && Main.GameUpdateCount - at <= RoadMemoryTicks && bestDistance <= HighwayLegKeep * 16f;
		if (bossSide && (bestDistance <= HighwayEntryArrival * 16f || onLeg))
		{
			int exit = Utils.Clamp(goal.X, bestSpan.low, bestSpan.high);
			if (System.Math.Abs(exit - bestEntry.X) > HighwayExitSlack)
			{
				roadRow = (best, Main.GameUpdateCount);
				return Leg(bestEntry, new Point(exit, best.Y), _ => best.Y) ?? new Vector2(exit * 16f + 8f, (best.Y + 1) * 16f - 21f);
			}
		}
		var entryPoint = new Vector2(bestEntry.X * 16f + 8f, (best.Y + 1) * 16f - 21f);
		if (bestDistance > HighwayEntryReach * 16f && CanRecall(Bot) && Main.GameUpdateCount - Bot.LastRecallAt >= RecallCooldownTicks
			&& RecallBringsCloser(entryPoint) && TryRecallToCamp())
		{
			recalled = true;
			return null;
		}
		return bestDistance < Vector2.Distance(Bot.Center, target) ? entryPoint : null;
	}

	(Highway highway, uint at)? roadRow;
	uint leftRoadLoggedAt;
	const uint RoadMemoryTicks = 120;
	const uint LeftRoadLogTicks = 600;

	void NoteLeftRoad(Point here)
	{
		if (roadRow is not var (highway, at) || Main.GameUpdateCount - at > RoadMemoryTicks)
			return;
		if (System.Math.Abs(here.Y - highway.Y) <= BridgeRowSlack || !highway.Spans(here.X) || Main.GameUpdateCount - leftRoadLoggedAt < LeftRoadLogTicks)
			return;
		leftRoadLoggedAt = Main.GameUpdateCount;
		BotDiagnostics.Log(Bot, $"left the highway row {highway.Y} at {here} while {NameKey}{TerrainDump.Around(new Point(here.X, highway.Y), 8, 6)}");
	}

	static bool Along(int row, Point here, Point goal) => System.Math.Abs(here.Y - row) <= BridgeRowSlack && System.Math.Abs(goal.Y - row) <= BridgeRowSlack;

	Vector2? Leg(Point here, Point goal, System.Func<int, int> feetRow)
	{
		Bot.Motor.NoteAlongCorridor();
		int dx = goal.X - here.X;
		if (System.Math.Abs(dx) <= BridgeLegThreshold)
			return null;
		int x = here.X / BridgeLegGrid * BridgeLegGrid + System.Math.Sign(dx) * BridgeLegTiles;
		return new Vector2(x * 16f + 8f, (feetRow(x) + 1) * 16f - 21f);
	}

	Vector2? deepTunnel;
	const int DeepDetourRows = 60;
	const int ShaftLevelRows = 3;
	const int ShaftClimbLeg = 120;
	const int DeepDetourRise = 40;
	const int MaxSidewaysTunnel = 300;

	const int ShaftExitMargin = 3;

	bool LeavesShaft(Vector2 target) =>
		TravelShaft is { } shaft && target.ToTileCoordinates() is var goal && (goal.X < shaft.X - ShaftExitMargin || goal.X > shaft.Right + ShaftExitMargin);

	const int CampTunnelMargin = 4;
	const int CampRetriesBeforeTunnel = 3;
	int campRetries;

	static bool InAnyCamp(Point tile) => BotWorldSystem.Factions.Any(faction => faction.Settlement is { } camp
		&& tile.X >= camp.Left - CampTunnelMargin && tile.X <= camp.Right + CampTunnelMargin && tile.Y >= camp.Top - CampTunnelMargin && tile.Y <= camp.Floor + CampTunnelMargin);

	bool InOwnShaft() => TravelShaft is { } shaft && shaft.Contains(Bot.CenterTile.X, Bot.CenterTile.Y) && Bot.CenterTile.Y < shaft.FirstImpassableRow() - ShaftDropRoom;

	bool DeepDetour(Vector2 target, float radius)
	{
		if (TravelShaft is not { } shaft || Navigation.PathFinder.InsideWalledMaze(target.ToTileCoordinates()))
			return false;
		var goal = target.ToTileCoordinates();
		if (goal.Y < shaft.TopY + DeepDetourRows)
			return false;
		int levelRow = System.Math.Min(goal.Y, shaft.FirstImpassableRow() - 2);
		var tile = Bot.CenterTile;
		bool inShaft = tile.X >= shaft.X && tile.X <= shaft.Right && tile.Y >= shaft.TopY - ShaftMouthHeadroom && tile.Y < shaft.FirstImpassableRow() - ShaftDropRoom;
		if (!inShaft && goal.Y - tile.Y < DeepDetourRise)
			return false;
		if (!inShaft && goal.Y < Main.UnderworldLayer && (System.Math.Abs(goal.X - shaft.X) > System.Math.Abs(goal.X - tile.X) + DeepDetourRise || System.Math.Abs(goal.X - shaft.X) > MaxSidewaysTunnel))
			return false;
		var feet = Bot.Npc is NPC body ? Navigation.PathFinder.FeetOf(body) : tile;
		if (inShaft && System.Math.Abs(feet.Y - levelRow) <= 1)
		{
			int mouthX = goal.X < shaft.X ? shaft.X - 1 : shaft.Right + 1;
			if (OpenShaftMouth(mouthX, feet.Y))
				return true;
			BotDiagnostics.Log(Bot, $"tunneling sideways from the hellevator at row {feet.Y} toward {goal}");
			deepTunnel = target;
			Bot.Motor.TunnelTo(target, radius);
			return true;
		}
		var level = Navigation.PathFinder.BodyCenterAt(new Point(shaft.X, levelRow));
		if (ShaftShortcut(level))
			return true;
		if (inShaft)
		{
			Bot.Motor.MoveTo(level, 8f);
			return !Bot.Motor.IsStuck;
		}
		bool sameTrip = Bot.LastRecallTarget is Vector2 earlier && Vector2.Distance(earlier, target) < SameTripReach && Main.GameUpdateCount - Bot.LastRecallAt < SameTripTicks;
		if (tile.Y > shaft.TopY + ShaftMouthReach && !sameTrip && CanRecall(Bot) && RecallLandsAtBase() && TryRecallHome())
		{
			Bot.LastRecallTarget = target;
			return true;
		}
		Bot.Motor.MoveTo(new Vector2((shaft.X + 1) * 16f, shaft.TopY * 16f - 21f), 12f);
		return !Bot.Motor.IsStuck;
	}

	bool OpenShaftMouth(int x, int feetRow)
	{
		for (int y = feetRow - 2; y <= feetRow; y++)
		{
			if (!TileQuery.IsSolid(x, y) || Building.HouseInspector.IsProtected(x, y))
				continue;
			Bot.Motor.Stop();
			Bot.Hands.Dig(new Point(x, y));
			return true;
		}
		if (!TileQuery.IsSolid(x, feetRow + 1) && !TileQuery.IsPlatform(x, feetRow + 1))
		{
			Bot.Motor.Stop();
			Bot.Hands.PlaceBridge(new Point(x, feetRow + 1), solidDeck: true);
			return true;
		}
		return false;
	}

	const uint TunnelPathRetryTicks = 600;
	uint tunnelingSince;

	const int BelowCampDepth = 60;
	const int CampExitMargin = 3;

	protected bool BelowOwnCamp()
	{
		var here = Bot.CenterTile;
		return Bot.Faction.Settlement is { } camp && here.X >= camp.Left && here.X <= camp.Right && here.Y > camp.Floor && here.Y < camp.Floor + BelowCampDepth;
	}

	protected Vector2? CampExitAbove(Vector2 target)
	{
		if (!BelowOwnCamp() || target.Y / 16f > Bot.Faction.Settlement.Floor)
			return null;
		var camp = Bot.Faction.Settlement;
		var here = Bot.CenterTile;
		int exitX = here.X - camp.Left < camp.Right - here.X ? camp.Left - CampExitMargin : camp.Right + CampExitMargin;
		return new Vector2(exitX * 16f + 8f, (camp.Floor - 2) * 16f);
	}
}
