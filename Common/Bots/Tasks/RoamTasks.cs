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
	int wanderDirection = 1;

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
		wanderDirection = Main.rand.NextBool() ? 1 : -1;
		huntingGround = null;
		var knowledge = Systems.BotWorldSystem.KnowledgeOf(Bot.Faction);
		skyHunt = Bot.Brain.Plan is GatherStep { Source: NpcSource { Sky: true } };
		if (skyHunt && Bot.CenterTile.Y > SkyHeight + SkyTolerance)
		{
			var ladder = SkyLadder.Beside(Bot.Faction, SkyOffset);
			huntingGround = new Vector2((ladder.X + 1) * 16f, (ladder.TopRung - 1) * 16f);
		}
		else if (Bot.Brain.Plan is GatherStep { Source: NpcSource { Ground: Landmark ground } source } && (source.Underground || !Grounds.IsInside(knowledge, ground, Bot.CenterTile)))
			huntingGround = source.Underground ? Grounds.PickUndergroundSpot(knowledge, ground) : Grounds.PickSpot(knowledge, ground, Bot.CenterTile);
	}

	public override void Tick()
	{
		if (prey != null && prey.active && Engagement.IsHostileTarget(prey) && !Bot.IsIgnoring(prey))
		{
			Engagement.Fight(Bot, prey);
			return;
		}
		if (skyHunt && SkyLadder.Beside(Bot.Faction, SkyOffset) is var ladder && !ladder.IsComplete)
		{
			ClimbAndExtend(ladder);
			return;
		}
		if (huntingGround is Vector2 ground)
		{
			if (!TravelOrTunnel(ground, 48f) || Bot.Motor.HasArrived)
				huntingGround = null;
			return;
		}
		int x = Utils.Clamp(Bot.CenterTile.X + wanderDirection * WanderTiles, 50, Main.maxTilesX - 50);
		bool underground = Bot.CenterTile.Y > Main.worldSurface + UndergroundMargin;
		float y = underground || skyHunt ? Bot.Center.Y : TileQuery.SurfaceY(x) * 16f - 24f;
		Bot.Motor.MoveTo(new Vector2(x * 16f, y), 32f);
		if (Bot.Motor.HasArrived || Bot.Motor.IsStalled)
		{
			wanderDirection = -wanderDirection;
			IsFinished = true;
		}
	}
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

	public override void Tick() => Bot.Motor.MoveTo(Bot.Faction.BaseCenter, 32f);
}

public sealed class ExploreTask : BotTask
{
	const int MinimumTrip = 40;
	const int MaximumTrip = 160;

	Vector2 destination;

	public ExploreTask(Bot bot) : base(bot) { }

	public override string NameKey => "Explore";

	public override float Evaluate() => Bot.Brain.Plan is IdleStep ? 20f + Traits.Exploring * 45f : 5f + Traits.Exploring * 25f;

	public override void Start()
	{
		base.Start();
		bool delveUnderground = Traits.Exploring > 0.7f && Main.rand.NextFloat() < 0.4f;
		int depth = delveUnderground ? Bot.CenterTile.Y + Main.rand.Next(40, 120) : -1;
		int x = NearestUnexploredColumn(depth) ?? RandomTripColumn();
		int y = delveUnderground ? depth : TileQuery.SurfaceY(x) - 2;
		destination = new Vector2(x * 16f, y * 16f);
		Detail = delveUnderground ? "↓" : x > Bot.CenterTile.X ? "→" : "←";
	}

	const int MaxFrontierCells = 15;

	int? NearestUnexploredColumn(int depth)
	{
		var knowledge = BotWorldSystem.KnowledgeOf(Bot.Faction);
		int cellX = Bot.CenterTile.X / Factions.FactionKnowledge.ExploreCell;
		int lastCell = (Main.maxTilesX - 50) / Factions.FactionKnowledge.ExploreCell;
		int firstSide = Main.rand.NextBool() ? 1 : -1;
		for (int offset = 1; offset <= MaxFrontierCells; offset++)
		{
			foreach (int side in new[] { firstSide, -firstSide })
			{
				int candidate = cellX + side * offset;
				if (candidate < 1 || candidate > lastCell)
					continue;
				int x = candidate * Factions.FactionKnowledge.ExploreCell + Factions.FactionKnowledge.ExploreCell / 2;
				int y = depth >= 0 ? depth : TileQuery.SurfaceY(x);
				if (!knowledge.IsExplored(candidate, y / Factions.FactionKnowledge.ExploreCell))
					return x;
			}
		}
		return null;
	}

	int RandomTripColumn()
	{
		int direction = Main.rand.NextBool() ? 1 : -1;
		int trip = (int)(Main.rand.Next(MinimumTrip, MaximumTrip) * (0.5f + Traits.Exploring));
		return Utils.Clamp(Bot.CenterTile.X + direction * trip, 50, Main.maxTilesX - 50);
	}

	public override void Tick()
	{
		Bot.Motor.MoveTo(destination, 32f);
		if (Bot.Motor.HasArrived || Bot.Motor.IsStuck)
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
