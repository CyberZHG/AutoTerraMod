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
}
