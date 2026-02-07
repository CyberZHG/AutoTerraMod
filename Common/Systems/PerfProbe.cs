using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AutoTerraMod.Common.Systems;

public readonly struct PerfProbe : IDisposable
{
	readonly int label;
	readonly long startedAt;
	readonly long allocatedAt;
	readonly int collectionsAt;

	internal PerfProbe(int label)
	{
		this.label = label;
		collectionsAt = label < 0 ? 0 : GC.CollectionCount(0);
		startedAt = label < 0 ? 0 : Stopwatch.GetTimestamp();
		allocatedAt = label < 0 ? 0 : GC.GetAllocatedBytesForCurrentThread();
		if (label >= 0)
			Probes.Enter();
	}

	public void Dispose()
	{
		if (label >= 0)
			Probes.Exit(label, Stopwatch.GetTimestamp() - startedAt, GC.GetAllocatedBytesForCurrentThread() - allocatedAt, GC.CollectionCount(0) != collectionsAt);
	}
}

public static class Probes
{
	public static bool Enabled;

	static readonly Dictionary<string, int> indexOf = new();
	static readonly List<string> labels = new();
	static long[] exclusiveTicks = new long[64];
	static long[] exclusiveBytes = new long[64];
	static long[] longestTicks = new long[64];
	static readonly long[] childTicks = new long[32];
	static readonly long[] childBytes = new long[32];
	static int depth;

	public static PerfProbe Measure(string label)
	{
		if (!Enabled)
			return new PerfProbe(-1);
		if (!indexOf.TryGetValue(label, out int index))
		{
			index = labels.Count;
			indexOf[label] = index;
			labels.Add(label);
			if (index >= exclusiveTicks.Length)
			{
				Array.Resize(ref exclusiveTicks, index * 2);
				Array.Resize(ref exclusiveBytes, index * 2);
				Array.Resize(ref longestTicks, index * 2);
			}
		}
		return new PerfProbe(index);
	}

	internal static void Enter()
	{
		depth++;
		if (depth < childTicks.Length)
		{
			childTicks[depth] = 0;
			childBytes[depth] = 0;
		}
	}

	static long pausedTicks;
	static int pausedCount;

	internal static void Exit(int label, long ticks, long bytes, bool collected)
	{
		if (depth <= 0)
			return;
		if (depth < childTicks.Length)
		{
			exclusiveTicks[label] += ticks - childTicks[depth];
			if (collected && depth == 1)
			{
				pausedTicks = Math.Max(pausedTicks, ticks);
				pausedCount++;
			}
			else if (!collected)
				longestTicks[label] = Math.Max(longestTicks[label], ticks - childTicks[depth]);
			exclusiveBytes[label] += bytes - childBytes[depth];
		}
		depth--;
		if (depth >= 0 && depth < childTicks.Length)
		{
			childTicks[depth] += ticks;
			childBytes[depth] += bytes;
		}
	}

	public static string Report(float seconds, int top)
	{
		var rows = Enumerable.Range(0, labels.Count)
			.Where(index => exclusiveBytes[index] > 0 || exclusiveTicks[index] > 0)
			.OrderByDescending(index => exclusiveTicks[index])
			.Take(top)
			.Select(index => $"{labels[index]}={exclusiveBytes[index] / seconds / 1024:0}KB/s,{exclusiveTicks[index] * 1000.0 / Stopwatch.Frequency / seconds:0.00}ms/s");
		var longest = Enumerable.Range(0, labels.Count)
			.Where(index => longestTicks[index] * 1000.0 / Stopwatch.Frequency >= 2.0)
			.OrderByDescending(index => longestTicks[index])
			.Select(index => $"{labels[index]}={longestTicks[index] * 1000.0 / Stopwatch.Frequency:0.0}ms");
		string report = "alloc probes: " + string.Join(" ", rows) + " | longest: " + string.Join(" ", longest)
			+ $" | gc0 inside probes: {pausedCount}, longest {pausedTicks * 1000.0 / Stopwatch.Frequency:0.0}ms";
		pausedTicks = 0;
		pausedCount = 0;
		Array.Clear(longestTicks);
		Array.Clear(exclusiveTicks);
		Array.Clear(exclusiveBytes);
		return report;
	}
}
