using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class HuntTask : BotTask
{
	const float SearchRadius = 16f * 100f;
	const int WanderTiles = 60;

	NPC prey;
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
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var npc = Main.npc[i];
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

	public override void Start()
	{
		base.Start();
		wanderDirection = Main.rand.NextBool() ? 1 : -1;
	}

	public override void Tick()
	{
		if (prey != null && prey.active && Engagement.IsHostileTarget(prey) && !Bot.IsIgnoring(prey))
		{
			Engagement.Fight(Bot, prey);
			return;
		}
		int x = Utils.Clamp(Bot.CenterTile.X + wanderDirection * WanderTiles, 50, Main.maxTilesX - 50);
		Bot.Motor.MoveTo(new Vector2(x * 16f, TileQuery.SurfaceY(x) * 16f - 24f), 32f);
		if (Bot.Motor.HasArrived || Bot.Motor.IsStuck)
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
		if (Main.dayTime || Traits.Courage >= 0.5f)
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
		int direction = Main.rand.NextBool() ? 1 : -1;
		int trip = (int)(Main.rand.Next(MinimumTrip, MaximumTrip) * (0.5f + Traits.Exploring));
		int x = Utils.Clamp(Bot.CenterTile.X + direction * trip, 50, Main.maxTilesX - 50);
		bool delveUnderground = Traits.Exploring > 0.7f && Main.rand.NextFloat() < 0.4f;
		int y = delveUnderground ? Bot.CenterTile.Y + Main.rand.Next(40, 120) : TileQuery.SurfaceY(x) - 2;
		destination = new Vector2(x * 16f, y * 16f);
		Detail = delveUnderground ? "↓" : direction > 0 ? "→" : "←";
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
