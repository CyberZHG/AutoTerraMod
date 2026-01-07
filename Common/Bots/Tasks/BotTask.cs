using Microsoft.Xna.Framework;
using Terraria;

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
