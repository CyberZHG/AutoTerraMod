using System.Diagnostics;
using System.Linq;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public sealed class OutpostWatch : GlobalTile
{
	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (fail || effectOnly || !Systems.BotWorldSystem.IsRunning)
			return;
		var frame = Building.Outpost.Guarded().FirstOrDefault(guard => guard.frame is { } known && Building.Outpost.InFrame(known, i, j)).frame;
		if (frame == null)
			return;
		var caller = new StackTrace().GetFrames()
			.Select(step => step.GetMethod())
			.FirstOrDefault(method => method?.DeclaringType?.Namespace?.StartsWith("AutoTerraMod") == true && method.DeclaringType != typeof(OutpostWatch));
		Bots.BotDiagnostics.LogWorld($"outpost frame {frame} lost tile {type} at {i},{j} via {caller?.DeclaringType?.Name}.{caller?.Name ?? "vanilla"}");
	}
}
