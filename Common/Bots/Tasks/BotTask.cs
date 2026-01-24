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
	public virtual bool IsUrgent => false;

	public abstract float Evaluate();

	public virtual void Start() => IsFinished = false;

	public abstract void Tick();

	public virtual void Stop() => Bot.Motor.Stop();

	const float MaxTunnelDistance = 16f * 200f;

	static readonly int[] RecallItems = { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone, ItemID.RecallPotion };

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
		return true;
	}

	protected bool TravelOrTunnel(Vector2 target, float radius)
	{
		Bot.Motor.MoveTo(target, radius);
		if (!Bot.Motor.IsStuck)
			return true;
		if (Bot.Motor.Tunneling || Bot.Hands.BestPickaxe == null)
			return false;
		if (Vector2.Distance(Bot.Center, target) >= MaxTunnelDistance)
		{
			Bot.Motor.Stop();
			Bot.Motor.MoveTo(target, radius);
			return true;
		}
		BotDiagnostics.Log(Bot, $"no walkable route to {target.ToTileCoordinates()}, tunneling");
		Bot.Motor.TunnelTo(target, radius);
		return true;
	}
}
