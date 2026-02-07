using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace AutoTerraMod.Common.Systems;

public sealed class AllocationSampler : EventListener
{
	const EventKeywords GcKeyword = (EventKeywords)0x1;

	static AllocationSampler instance;
	readonly Dictionary<string, long> bytesByType = new();
	readonly object gate = new();

	public static void Start()
	{
		if (instance == null && Environment.GetEnvironmentVariable("AUTOTERRA_ALLOCTYPES") == "1")
			instance = new AllocationSampler();
	}

	protected override void OnEventSourceCreated(EventSource source)
	{
		if (source.Name == "Microsoft-Windows-DotNETRuntime")
			EnableEvents(source, EventLevel.Verbose, GcKeyword);
	}

	protected override void OnEventWritten(EventWrittenEventArgs data)
	{
		if (data.EventName == null || !data.EventName.StartsWith("GCAllocationTick") || data.Payload == null)
			return;
		int nameIndex = data.PayloadNames.IndexOf("TypeName");
		int amountIndex = data.PayloadNames.IndexOf("AllocationAmount64");
		if (nameIndex < 0)
			return;
		string type = data.Payload[nameIndex] as string ?? "?";
		long amount = amountIndex >= 0 ? Convert.ToInt64(data.Payload[amountIndex]) : 100_000;
		lock (gate)
			bytesByType[type] = bytesByType.GetValueOrDefault(type) + amount;
	}

	public static string Report(float seconds, int top)
	{
		if (instance == null)
			return null;
		lock (instance.gate)
		{
			string report = "alloc types: " + string.Join(" | ", instance.bytesByType.OrderByDescending(pair => pair.Value).Take(top)
				.Select(pair => $"{pair.Key}={pair.Value / seconds / 1024:0}KB/s"));
			instance.bytesByType.Clear();
			return report;
		}
	}
}
