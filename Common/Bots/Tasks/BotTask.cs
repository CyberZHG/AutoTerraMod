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

	static readonly int[] RecallItems = { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone, ItemID.RecallPotion };

	public static bool IsRecall(Item item) => RecallItems.Contains(item.type);

	public static bool CarriesRecall(Player avatar) => Inventory.Items(avatar).Any(item => RecallItems.Contains(item.type));

	protected bool TryRecallHome()
	{
		var recall = Inventory.Items(Avatar).FirstOrDefault(item => RecallItems.Contains(item.type));
		var body = Bot.Npc;
		if (recall == null || body == null)
			return false;
		string name = recall.Name;
		if (recall.consumable)
			Inventory.ConsumeOne(Avatar, recall);
		SoundEngine.PlaySound(SoundID.Item6, body.Center);
		var home = BotWorldSystem.RespawnFeet(Bot);
		body.position = home - new Vector2(body.width / 2f, body.height);
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Motor.Stop();
		BotDiagnostics.Log(Bot, $"used {name} to return home");
		Spectating.SpectatorSystem.AnnounceJump(Bot, "RecalledHome", Bot.Name, name);
		return true;
	}

	const float AspirationCap = 25f;

	protected float Aspire(float score) => Bosses.BossCredit.NextChallengeFor(Bot.Faction) == null ? score : System.Math.Min(score, AspirationCap);

	const float BedsideReach = 16f * 4f;

	protected void LeaveOutpostIfBedside()
	{
		if (Bot.OutpostBed is not Point bed || Vector2.Distance(Bot.Center, bed.ToWorldCoordinates()) > BedsideReach)
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
	const int UndergroundTiles = 30;

	bool RecallBeatsLongTrip(Vector2 target)
	{
		if (Bot.CenterTile.Y < Main.worldSurface + UndergroundTiles || !CarriesRecall(Avatar))
			return false;
		float fromHere = Vector2.Distance(Bot.Center, target) / 16f;
		if (fromHere < LongTripTiles)
			return false;
		float fromHome = Vector2.Distance(BotWorldSystem.RespawnFeet(Bot), target) / 16f;
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
	const float ShaftDropFinish = 16f;
	const float ShaftCentring = 5f;
	const int ShaftMouthReach = 20;

	const float NudgePerTick = 1.5f;

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
		else if (body.velocity.Y == 0f)
			Bot.Motor.DropThroughPlatform();
		Bot.Watchdog.RecordProgress();
	}

	bool ShaftShortcut(Vector2 target)
	{
		if (Bot.Faction.Hellevator is not { } shaft || target.Y < Bot.Center.Y + ShaftDropFinish)
			return false;
		float centerX = (shaft.X + 1) * 16f;
		var tile = Bot.CenterTile;
		bool inShaft = tile.X >= shaft.X && tile.X <= shaft.X + 1 && tile.Y >= shaft.TopY - 3 && tile.Y < Main.UnderworldLayer;
		if (inShaft && (target.Y >= Bot.Center.Y + ShaftDropMargin || Bot.Npc?.velocity.Y > 0f))
		{
			CentreIn(shaft);
			return true;
		}
		var targetTile = target.ToTileCoordinates();
		bool intoShaft = targetTile.X >= shaft.X - 1 && targetTile.X <= shaft.X + 2 && targetTile.Y > shaft.TopY;
		if (!(intoShaft || shaft.IsComplete && target.Y >= Main.UnderworldLayer * 16f) || tile.Y > shaft.TopY + ShaftMouthReach)
			return false;
		Bot.Motor.MoveTo(new Vector2(centerX, shaft.TopY * 16f - 21f), 8f);
		return !Bot.Motor.IsStuck;
	}

	protected bool TravelOrTunnel(Vector2 target, float radius)
	{
		if (campExit != null && Bot.Motor.Tunneling && !Bot.Motor.HasArrived && !Bot.Motor.IsStuck)
			return true;
		if (RecallBeatsLongTrip(target) && TryRecallHome())
			return true;
		if (ShaftShortcut(target))
			return true;
		campExit = null;
		if (Bot.Motor.Tunneling && Main.GameUpdateCount - tunnelingSince > TunnelPathRetryTicks)
			Bot.Motor.Stop();
		Bot.Motor.MoveTo(target, radius);
		if (!Bot.Motor.IsStuck)
			return true;
		if (Bot.Motor.Tunneling || Bot.Hands.BestPickaxe == null || Navigation.PathFinder.InsideMaze(target.ToTileCoordinates()))
			return false;
		if (Vector2.Distance(Bot.Center, target) >= MaxTunnelDistance)
		{
			Bot.Motor.Stop();
			Bot.Motor.MoveTo(target, radius);
			return true;
		}
		if (CampExitAbove(target) is Vector2 exit)
		{
			BotDiagnostics.Log(Bot, $"stuck under the camp, tunneling out to {exit.ToTileCoordinates()}");
			campExit = exit;
			Bot.Motor.TunnelTo(exit, 32f);
			return true;
		}
		BotDiagnostics.Log(Bot, $"no walkable route to {target.ToTileCoordinates()}, tunneling");
		tunnelingSince = Main.GameUpdateCount;
		Bot.Motor.TunnelTo(target, radius);
		return true;
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
