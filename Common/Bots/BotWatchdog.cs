using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;

namespace AutoTerraMod.Common.Bots;

public sealed class BotWatchdog
{
	const int CheckIntervalTicks = 60;
	const int DecisionStallTicks = 1200;
	const int TrappedStallTicks = 2400;
	const int RetryTicks = 1200;
	const int TravelProgressTiles = 12;
	const int TrappedRadiusTiles = 6;

	readonly Bot bot;
	uint lastProgress;
	uint lastEscalation;
	Point travelAnchor;
	Point trapAnchor;
	uint trapAnchorSince;
	int checkTimer;

	public BotWatchdog(Bot bot)
	{
		this.bot = bot;
		checkTimer = bot.Id % CheckIntervalTicks;
	}

	public bool EscapeRequested { get; private set; }
	public int EscapeDirection { get; private set; } = 1;
	public int StalledSeconds => (int)((Main.GameUpdateCount - lastProgress) / 60);

	public void RecordProgress() => lastProgress = Main.GameUpdateCount;

	public void Reset()
	{
		RecordProgress();
		EscapeRequested = false;
		travelAnchor = trapAnchor = bot.CenterTile;
		trapAnchorSince = Main.GameUpdateCount;
	}

	public void EscapeFinished()
	{
		EscapeRequested = false;
		RecordProgress();
	}

	public void Tick()
	{
		if (++checkTimer < CheckIntervalTicks)
			return;
		checkTimer = 0;
		var here = bot.CenterTile;
		if (WorldScan.DistanceSq(here, travelAnchor) > TravelProgressTiles * TravelProgressTiles)
		{
			travelAnchor = here;
			RecordProgress();
		}
		if (WorldScan.DistanceSq(here, trapAnchor) > TrappedRadiusTiles * TrappedRadiusTiles)
		{
			trapAnchor = here;
			trapAnchorSince = Main.GameUpdateCount;
		}
		uint now = Main.GameUpdateCount;
		if (now - lastProgress < DecisionStallTicks || now - lastEscalation < RetryTicks)
			return;
		lastEscalation = now;
		bool trapped = now - trapAnchorSince >= TrappedStallTicks;
		bot.Brain.SuppressCurrentGoal();
		if (trapped)
		{
			EscapeRequested = true;
			EscapeDirection = -EscapeDirection;
		}
		BotDiagnostics.Log(bot, trapped
			? $"watchdog: trapped near {here} for {StalledSeconds}s, escaping; motor: {bot.Motor.Describe()}{TerrainDump.Around(here)}"
			: $"watchdog: no progress for {StalledSeconds}s, suppressing current goal");
		bot.Brain.ForceReconsider();
	}
}
