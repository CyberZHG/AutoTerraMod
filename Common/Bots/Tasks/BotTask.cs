using System.Linq;
using AutoTerraMod.Common.Systems;
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
		if (recall.consumable)
			Inventory.ConsumeOne(Avatar, recall);
		SoundEngine.PlaySound(SoundID.Item6, body.Center);
		var home = BotWorldSystem.RespawnFeet(Bot);
		body.position = home - new Vector2(body.width / 2f, body.height);
		body.velocity = Vector2.Zero;
		Bot.Vitals.Teleported(body);
		Bot.Motor.Stop();
		BotDiagnostics.Log(Bot, $"used {recall.Name} to return home");
		Spectating.SpectatorSystem.AnnounceJump(Bot, "RecalledHome", Bot.Name, recall.Name);
		return true;
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

	protected bool TravelOrTunnel(Vector2 target, float radius)
	{
		if (campExit != null && Bot.Motor.Tunneling && !Bot.Motor.HasArrived && BelowOwnCamp())
			return true;
		campExit = null;
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
		Bot.Motor.TunnelTo(target, radius);
		return true;
	}

	const int BelowCampDepth = 60;
	const int CampExitMargin = 6;

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
		return new Vector2(exitX * 16f + 8f, (World.TileQuery.SurfaceY(exitX) - 2) * 16f);
	}
}
