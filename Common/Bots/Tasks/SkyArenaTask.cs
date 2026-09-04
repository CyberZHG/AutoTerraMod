using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Terraria;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class SkyArenaTask : BotTask
{
	const float BuildPriority = 66f;

	public SkyArenaTask(Bot bot) : base(bot) { }

	public override string NameKey => "BuildBridge";

	public override float Evaluate() => Bot.Brain.Plan is BuildSkyArenaStep ? BuildPriority : 0f;

	public override void Tick()
	{
		if (SkyArena.For(Bot.Faction) is not { } arena || !arena.RopeReady || arena.NextGap() is not Microsoft.Xna.Framework.Point gap || !Bot.Hands.HasPlatform)
		{
			IsFinished = true;
			return;
		}
		if (!Bot.Hands.CanReachPlatform(gap))
		{
			int side = System.Math.Sign(gap.X - arena.CenterX);
			var stand = new Microsoft.Xna.Framework.Point(gap.X - side * 2, arena.DeckRow - 1);
			if (!TravelOrTunnel(stand.ToWorldCoordinates(8f, 0f), 24f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		if (Bot.Hands.PlacePlatform(gap))
			Bot.Watchdog.RecordProgress();
	}
}
