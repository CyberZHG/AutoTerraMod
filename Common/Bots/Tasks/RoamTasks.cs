using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class HuntTask : BotTask
{
	const float SearchRadius = 16f * 100f;
	const int WanderTiles = 60;
	const int UndergroundMargin = 20;

	const int SkyOffset = -60;
	const int SkyTolerance = 10;

	static int SkyHeight => (int)(Main.worldSurface * 0.35) - 5;

	NPC prey;
	Vector2? huntingGround;
	bool skyHunt;
	int wanderDirection = Main.rand.NextBool() ? 1 : -1;
	int huntingFor;
	Vector2? wanderTarget;

	public HuntTask(Bot bot) : base(bot) { }

	public override string NameKey => "Hunt";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not GatherStep { Source: NpcSource source } gather)
			return 0f;
		Detail = Lang.GetItemNameValue(gather.ItemType);
		prey = NearestPrey(source);
		return prey != null ? 40f + Traits.Combat * 40f : 30f + Traits.Combat * 20f;
	}

	NPC NearestPrey(NpcSource source)
	{
		NPC nearest = null;
		float nearestDistance = SearchRadius;
		foreach (var npc in World.EntityIndex.ActiveNpcs)
		{
			if (!npc.active || !source.NpcTypes.Contains(npc.type) || Bot.IsIgnoring(npc))
				continue;
			float distance = Vector2.Distance(npc.Center, Bot.Center);
			if (distance < nearestDistance)
			{
				nearest = npc;
				nearestDistance = distance;
			}
		}
		return nearest;
	}

	const float RungReach = 16f * 2f;

	void ClimbAndExtend(SkyLadder ladder)
	{
		if (ladder.NextRung() is not int rung)
			return;
		int standY = rung + SkyLadder.RungSpacing - 1;
		if (rung + SkyLadder.RungSpacing > ladder.GroundY)
			standY = ladder.GroundY;
		var stand = new Vector2((ladder.X + 1) * 16f, standY * 16f);
		if (Vector2.Distance(Bot.Center, stand) > RungReach)
		{
			if (!TravelOrTunnel(stand, RungReach * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		Bot.Watchdog.RecordProgress();
		if (!Bot.Hands.HasBridgeMaterial)
		{
			IsFinished = true;
			return;
		}
		Bot.Hands.PlaceBridge(new Point(ladder.X, rung));
		Bot.Hands.PlaceBridge(new Point(ladder.X + 1, rung));
	}

	public override void Start()
	{
		base.Start();
		wanderTarget = null;
		int quarry = Bot.Brain.Plan is GatherStep gather ? gather.ItemType : 0;
		if (quarry != huntingFor)
		{
			huntingFor = quarry;
			huntingGround = null;
		}
		var knowledge = Systems.BotWorldSystem.KnowledgeOf(Bot.Faction);
		skyHunt = Bot.Brain.Plan is GatherStep { Source: NpcSource { Sky: true } };
		if (Bot.Brain.Plan is GatherStep { Source: NpcSource { Critter: true } })
			huntingGround = Avatar.ZoneGlowshroom ? null : World.MushroomPatch.Nearest(Bot.CenterTile);
		else if (skyHunt && Bot.CenterTile.Y > SkyHeight + SkyTolerance)
		{
			var ladder = SkyLadder.Beside(Bot.Faction, SkyOffset);
			huntingGround = new Vector2((ladder.X + 1) * 16f, (ladder.TopRung - 1) * 16f);
		}
		else if (huntingGround == null && Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark ground } source } && (source.Underground || !InBiome(ground)))
			huntingGround = source.Underground ? Grounds.PickUndergroundSpot(knowledge, ground) : Grounds.PickSpot(knowledge, ground, Bot.CenterTile);
	}

	public override void Tick()
	{
		if (prey != null && prey.active && Engagement.IsHostileTarget(prey) && !Bot.IsIgnoring(prey))
		{
			Engagement.Fight(Bot, prey);
			return;
		}
		if (Bot.Brain.Plan is GatherStep { Source: NpcSource { Critter: true } } && huntingGround == null)
		{
			StalkCritter();
			return;
		}
		if (skyHunt && SkyLadder.Beside(Bot.Faction, SkyOffset) is var ladder && !ladder.IsComplete)
		{
			ClimbAndExtend(ladder);
			return;
		}
		if (huntingGround is Vector2 ground)
		{
			if (Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark.Dungeon } } && Grounds.InsideDungeon(Bot.CenterTile))
			{
				Bot.Motor.MoveTo(ground, 48f);
				if (Bot.Motor.IsStuck || Bot.Motor.HasArrived)
					huntingGround = null;
				return;
			}
			if (!TravelOrTunnel(ground, 48f) || Bot.Motor.HasArrived)
				huntingGround = null;
			return;
		}
		if (Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark home, Underground: false } } && !InBiome(home)
			&& Grounds.PickSpot(Systems.BotWorldSystem.KnowledgeOf(Bot.Faction), home, Bot.CenterTile) is Vector2 spot)
		{
			huntingGround = spot;
			wanderTarget = null;
			return;
		}
		if (wanderTarget == null && Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark.Dungeon } }
			&& Grounds.PickSpot(Systems.BotWorldSystem.KnowledgeOf(Bot.Faction), Landmark.Dungeon, Bot.CenterTile) is Vector2 inside)
			wanderTarget = inside;
		if (wanderTarget == null)
		{
			int x = Bot.CenterTile.X + wanderDirection * WanderTiles;
			if (x < WanderEdgeMargin || x > Main.maxTilesX - WanderEdgeMargin || LeavesHuntingGround(x))
			{
				wanderDirection = -wanderDirection;
				x = Bot.CenterTile.X + wanderDirection * WanderTiles;
			}
			x = Utils.Clamp(x, WanderEdgeMargin, Main.maxTilesX - WanderEdgeMargin);
			bool underground = Bot.CenterTile.Y > Main.worldSurface + UndergroundMargin;
			float y = underground || skyHunt ? Bot.Center.Y : TileQuery.SurfaceY(x) * 16f - 24f;
			wanderTarget = new Vector2(x * 16f, y);
		}
		Bot.Motor.MoveTo(wanderTarget.Value, 32f);
		if (Bot.Motor.IsStalled)
			wanderDirection = -wanderDirection;
		if (Bot.Motor.HasArrived || Bot.Motor.IsStalled)
			IsFinished = true;
	}

	const int WanderEdgeMargin = 50;
	const float NetReach = 16f * 3f;

	void StalkCritter()
	{
		if (prey == null || !prey.active || prey.catchItem <= 0)
		{
			Bot.Motor.Stop();
			Bot.Watchdog.RecordProgress();
			if (!Avatar.ZoneGlowshroom)
				IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, prey.Center) > NetReach)
		{
			Bot.Motor.MoveTo(prey.Center, NetReach * 0.5f);
			if (Bot.Motor.IsStalled)
				IsFinished = true;
			return;
		}
		var caught = new Item();
		caught.SetDefaults(prey.catchItem);
		prey.active = false;
		Inventory.Add(Avatar, caught);
		BotDiagnostics.Log(Bot, $"caught a {Lang.GetItemNameValue(caught.type)} with the bug net");
		Bot.Watchdog.RecordProgress();
		IsFinished = true;
	}

	bool InBiome(Landmark ground) => ground switch
	{
		Landmark.Snow => Avatar.ZoneSnow,
		Landmark.Dungeon => Avatar.ZoneDungeon,
		Landmark.Underworld => Bot.CenterTile.Y > Main.UnderworldLayer + Grounds.UnderworldDepth * 2 / 3,
		Landmark.Temple => Navigation.PathFinder.InsideMaze(Bot.CenterTile),
		_ => Bosses.BossChallenge.InArena(Avatar, ground)
	};

	bool LeavesHuntingGround(int x) =>
		Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark ground } }
		&& Systems.BotWorldSystem.KnowledgeOf(Bot.Faction).SpanOf(ground) is var (minX, maxX)
		&& (x < minX || x > maxX);
}

public sealed class ReturnHomeTask : BotTask
{
	const float FarFromBase = 16f * 40f;

	public ReturnHomeTask(Bot bot) : base(bot) { }

	public override string NameKey => "ReturnHome";

	public override float Evaluate()
	{
		if (Main.dayTime || Traits.Courage >= 0.5f || Bot.CommittedToMining)
			return 0f;
		float score = 25f + (0.5f - Traits.Courage) * 60f;
		return Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) > FarFromBase ? score : score * 0.4f;
	}

	public override void Tick()
	{
		LeaveOutpostIfBedside();
		Bot.Motor.MoveTo(Bot.Faction.BaseCenter, 32f);
	}
}

public sealed class ExploreTask : BotTask
{
	const int SurfaceStepTiles = 40;
	const int WorldEdgeMargin = 60;

	Vector2 destination;
	int direction = Main.rand.NextBool() ? 1 : -1;
	int? diveX;
	int diveLegY;

	const float DiveStartReach = 16f * 80f;
	const int DiveColumnSlack = 3;
	const int DiveLegTiles = 30;
	const uint DiveStallTicks = 3600;
	int deepestY;
	uint deepestAt;
	int? crossDepth;
	int crossDirection;
	int crossLegX;
	int crossProgress;
	uint crossProgressAt;

	const int CrossTopMargin = 20;
	const int CrossBottomMargin = 60;

	public ExploreTask(Bot bot) : base(bot) { }

	public override string NameKey => "Explore";

	public override float Evaluate() => Inventory.IsNearlyFull(Avatar) ? 0f : Bot.Brain.Plan is IdleStep ? 30f + Traits.Exploring * 45f : 10f + Traits.Exploring * 30f;

	public override void Start()
	{
		base.Start();
		if (diveX == null && crossDepth == null && Bot.Hands.BestPickaxe != null && Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) < DiveStartReach)
		{
			int strategy = Main.rand.Next(3);
			if (strategy == 1)
				StartDive();
			else if (strategy == 2)
				StartCrossing();
		}
		if (crossDepth != null)
		{
			Detail = crossDirection > 0 ? "⇘" : "⇙";
			return;
		}
		if (diveX != null)
		{
			Detail = "⇣";
			return;
		}
		bool delveUnderground = Traits.Exploring > 0.4f && Main.rand.NextBool();
		int depth = delveUnderground ? Bot.CenterTile.Y + Main.rand.Next(40, 120) : -1;
		int here = Bot.CenterTile.X;
		if (here + direction * SurfaceStepTiles < WorldEdgeMargin || here + direction * SurfaceStepTiles > Main.maxTilesX - WorldEdgeMargin)
			direction = -direction;
		int x = NearestUnexploredAhead(depth) ?? Utils.Clamp(here + direction * SurfaceStepTiles, WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
		int y = delveUnderground ? depth : TileQuery.SurfaceY(x) - 2;
		destination = new Vector2(x * 16f, y * 16f);
		Detail = delveUnderground ? "↓" : direction > 0 ? "→" : "←";
	}

	const int MaxFrontierCells = 15;

	int? NearestUnexploredAhead(int depth)
	{
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		int cellX = Bot.CenterTile.X / Factions.FactionKnowledge.ExploreCell;
		int lastCell = (Main.maxTilesX - WorldEdgeMargin) / Factions.FactionKnowledge.ExploreCell;
		for (int offset = 1; offset <= MaxFrontierCells; offset++)
		{
			int candidate = cellX + direction * offset;
			if (candidate < 1 || candidate > lastCell)
				return null;
			int x = candidate * Factions.FactionKnowledge.ExploreCell + Factions.FactionKnowledge.ExploreCell / 2;
			int y = depth >= 0 ? depth : TileQuery.SurfaceY(x);
			if (!knowledge.IsExplored(candidate, y / Factions.FactionKnowledge.ExploreCell))
				return x;
		}
		return null;
	}

	void StartDive()
	{
		diveX = DiveColumn();
		diveLegY = 0;
		deepestY = 0;
		deepestAt = Main.GameUpdateCount;
		if (diveX != null)
			BotDiagnostics.Log(Bot, $"diving down at column {diveX}");
	}

	void StartCrossing()
	{
		crossDepth = Main.rand.Next((int)Main.worldSurface + CrossTopMargin, Main.UnderworldLayer - CrossBottomMargin);
		crossDirection = Main.rand.NextBool() ? 1 : -1;
		crossLegX = Bot.CenterTile.X;
		diveLegY = 0;
		crossProgress = int.MinValue;
		crossProgressAt = Main.GameUpdateCount;
		BotDiagnostics.Log(Bot, $"heading down to depth {crossDepth} then {(crossDirection > 0 ? "east" : "west")}");
	}

	void Cross(int depth)
	{
		var here = Bot.CenterTile;
		bool descending = here.Y < depth - DiveColumnSlack;
		int progress = descending ? here.Y : Main.maxTilesY + here.X * crossDirection;
		if (progress > crossProgress)
		{
			crossProgress = progress;
			crossProgressAt = Main.GameUpdateCount;
		}
		int nextX = here.X + crossDirection * DiveLegTiles;
		bool atEdge = !descending && (nextX < WorldEdgeMargin || nextX > Main.maxTilesX - WorldEdgeMargin || TileQuery.InOcean(nextX, depth));
		if (atEdge || Main.GameUpdateCount - crossProgressAt > DiveStallTicks)
		{
			BotDiagnostics.Log(Bot, atEdge ? $"crossed to the edge at {here}" : $"crossing stalled at {here}");
			crossDepth = null;
			if (atEdge && CarriesRecall(Avatar))
				TryRecallHome();
			IsFinished = true;
			return;
		}
		Vector2 target;
		if (descending)
		{
			if (here.Y >= diveLegY - 2)
				diveLegY = System.Math.Min(here.Y + DiveLegTiles, depth);
			target = new Vector2(here.X * 16f + 8f, diveLegY * 16f);
		}
		else
		{
			if ((crossLegX - here.X) * crossDirection < DiveColumnSlack)
				crossLegX = nextX;
			target = new Vector2(crossLegX * 16f + 8f, depth * 16f);
		}
		if (!TravelOrTunnel(target, 32f))
		{
			crossDepth = null;
			IsFinished = true;
		}
	}

	int? DiveColumn()
	{
		for (int attempt = 0; attempt < 10; attempt++)
		{
			int x = Main.rand.Next(WorldEdgeMargin, Main.maxTilesX - WorldEdgeMargin);
			if (!TileQuery.InOcean(x, (int)Main.worldSurface - 1))
				return x;
		}
		return null;
	}

	void Dive(int column)
	{
		var here = Bot.CenterTile;
		if (here.Y >= Main.UnderworldLayer)
		{
			BotDiagnostics.Log(Bot, $"dove to the underworld at {here}");
			diveX = null;
			if (CarriesRecall(Avatar))
				TryRecallHome();
			IsFinished = true;
			return;
		}
		bool approaching = System.Math.Abs(here.X - column) > DiveColumnSlack && here.Y <= TileQuery.SurfaceY(column) + DiveColumnSlack;
		if (here.Y > deepestY || approaching)
		{
			deepestY = here.Y;
			deepestAt = Main.GameUpdateCount;
		}
		else if (Main.GameUpdateCount - deepestAt > DiveStallTicks)
		{
			BotDiagnostics.Log(Bot, $"dive at column {column} stalled at {here}");
			diveX = null;
			IsFinished = true;
			return;
		}
		Vector2 target;
		if (approaching)
			target = new Vector2(column * 16f + 8f, (TileQuery.SurfaceY(column) - 2) * 16f);
		else
		{
			if (here.Y >= diveLegY - 2)
				diveLegY = System.Math.Min(here.Y + DiveLegTiles, Main.UnderworldLayer + 2);
			target = new Vector2(column * 16f + 8f, diveLegY * 16f);
		}
		if (!TravelOrTunnel(target, 32f))
		{
			diveX = null;
			IsFinished = true;
		}
	}

	public override void Tick()
	{
		if (crossDepth is int depth)
		{
			Cross(depth);
			return;
		}
		if (diveX is int column)
		{
			Dive(column);
			return;
		}
		Bot.Motor.MoveTo(destination, 32f);
		if (Bot.Motor.IsStuck)
			direction = -direction;
		if (Bot.Motor.HasArrived || Bot.Motor.IsStuck)
			IsFinished = true;
	}
}

public sealed class SurveyTask : BotTask
{
	SurveyStep step;

	public SurveyTask(Bot bot) : base(bot) { }

	public override string NameKey => "Survey";

	public override float Evaluate()
	{
		if (Bot.Brain.Plan is not SurveyStep survey || Inventory.IsNearlyFull(Avatar))
			return 0f;
		step = survey;
		return 50f + Traits.Exploring * 30f;
	}

	public override void Tick()
	{
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		int cell = Factions.FactionKnowledge.ExploreCell;
		if (knowledge.IsExplored(step.Target.X / cell, step.Target.Y / cell))
		{
			Bot.Watchdog.RecordProgress();
			IsFinished = true;
			return;
		}
		if (TravelOrTunnel(step.Target.ToWorldCoordinates(), 48f) && !Bot.Motor.HasArrived)
			return;
		knowledge.MarkExplored(step.Target);
		IsFinished = true;
	}
}

public sealed class IdleTask : BotTask
{
	public IdleTask(Bot bot) : base(bot) { }

	public override string NameKey => "Idle";

	public override float Evaluate() => 1f;

	public override void Tick() => Bot.Motor.MoveTo(Bot.Faction.BaseCenter, 48f);
}
