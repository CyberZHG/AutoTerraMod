using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AutoTerraMod.Common.Config;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public enum PerfSection
{
	BotAi,
	Pathfinding,
	Planner,
	HouseQueries,
	WorldScans,
	VanillaHooks,
	Drawing
}

public readonly struct PerfScope : IDisposable
{
	readonly PerfSection section;
	readonly long startedAt;
	readonly long allocatedAt;

	public PerfScope(PerfSection section)
	{
		this.section = section;
		startedAt = Stopwatch.GetTimestamp();
		allocatedAt = GC.GetAllocatedBytesForCurrentThread();
	}

	public void Dispose()
	{
		Perf.Add(section, Stopwatch.GetTimestamp() - startedAt);
		Perf.AddAllocated(section, GC.GetAllocatedBytesForCurrentThread() - allocatedAt);
	}
}

public sealed class Perf : ModSystem
{
	const int ReportIntervalTicks = 600;

	static readonly long[] elapsed = new long[Enum.GetValues<PerfSection>().Length];
	static readonly int[] calls = new int[elapsed.Length];
	static readonly long[] allocated = new long[elapsed.Length];
	static readonly long[] thisTick = new long[elapsed.Length];
	static readonly long[] worstTick = new long[elapsed.Length];
	static int pathNodes;
	static int ticksSinceReport;
	static long tickStartedAt;
	static long worstUpdate;
	static int tickGcStart;
	static int worstTickGcs;
	static int slowTicks;
	const double SlowTickMilliseconds = 8.0;

	public static PerfScope Measure(PerfSection section) => new(section);

	public static void Add(PerfSection section, long ticks)
	{
		elapsed[(int)section] += ticks;
		thisTick[(int)section] += ticks;
		calls[(int)section]++;
	}

	public override void OnWorldLoad()
	{
		Probes.Enabled = Environment.GetEnvironmentVariable("AUTOTERRA_PROBES") == "1";
		AllocationSampler.Start();
	}

	public override void PreUpdateEntities()
	{
		tickStartedAt = Stopwatch.GetTimestamp();
		tickGcStart = GC.CollectionCount(1);
	}

	static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

	static void CloseTick()
	{
		long update = tickStartedAt == 0 ? 0 : Stopwatch.GetTimestamp() - tickStartedAt;
		if (Milliseconds(update) > SlowTickMilliseconds)
			slowTicks++;
		if (update > worstUpdate)
		{
			worstUpdate = update;
			worstTickGcs = GC.CollectionCount(1) - tickGcStart;
			Array.Copy(thisTick, worstTick, thisTick.Length);
		}
		Array.Clear(thisTick);
	}

	public static void CountPathNodes(int nodes) => pathNodes += nodes;

	public static void AddAllocated(PerfSection section, long bytes) => allocated[(int)section] += bytes;

	public override void PostUpdateEverything()
	{
		CloseTick();
		if (!BotWorldSystem.IsRunning || ++ticksSinceReport < ReportIntervalTicks)
			return;
		float seconds = ticksSinceReport / 60f;
		ticksSinceReport = 0;
		if (Bots.BotDiagnostics.Enabled)
		{
			Mod.Logger.Info(BuildReport(seconds));
			if (Probes.Enabled)
				Mod.Logger.Info(Probes.Report(seconds, 25));
			if (AllocationSampler.Report(seconds, 40) is string types)
				Mod.Logger.Info(types);
		}
		Array.Clear(elapsed);
		Array.Clear(calls);
		Array.Clear(allocated);
		pathNodes = 0;
		worstUpdate = 0;
		slowTicks = 0;
	}

	static string BuildReport(float seconds)
	{
		var report = new StringBuilder($"perf over {seconds:0}s ({BotWorldSystem.Bots.Count(bot => bot.IsAlive)} bots alive):");
		foreach (var section in Enum.GetValues<PerfSection>())
		{
			double milliseconds = elapsed[(int)section] * 1000.0 / Stopwatch.Frequency;
			report.Append($" {section}={milliseconds / seconds:0.00}ms/s ({calls[(int)section] / seconds:0}/s, {allocated[(int)section] / seconds / 1024:0}KB/s)");
		}
		report.Append($" pathNodes={pathNodes / seconds:0}/s; worst tick {Milliseconds(worstUpdate):0.0}ms [");
		report.Append(string.Join(" ", Enum.GetValues<PerfSection>().Where(section => worstTick[(int)section] > 0).Select(section => $"{section}={Milliseconds(worstTick[(int)section]):0.0}")));
		report.Append($"] gc1+={worstTickGcs}, ticks over {SlowTickMilliseconds:0}ms: {slowTicks}");
		return report.ToString();
	}
}
