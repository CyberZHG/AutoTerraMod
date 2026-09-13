using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class SurfaceDigProbe : GlobalTile
{
	const float Near = 16f * 12f;
	const uint ReportTicks = 3600;

	static readonly Dictionary<string, int> counts = new();
	static uint reportedAt;

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (!Probes.Enabled || fail || effectOnly || j >= Main.worldSurface || !Main.tileSolid[type] || Main.tileSolidTop[type] || !BotWorldSystem.IsRunning)
			return;
		var at = new Microsoft.Xna.Framework.Vector2(i * 16 + 8, j * 16 + 8);
		var bot = BotWorldSystem.Bots.Where(bot => bot.IsAlive).MinBy(bot => Microsoft.Xna.Framework.Vector2.DistanceSquared(bot.Center, at));
		string key = bot == null || Microsoft.Xna.Framework.Vector2.Distance(bot.Center, at) > Near ? "nobot" : $"{bot.Brain.Current?.NameKey ?? "none"}/{bot.Brain.GoalName}";
		if (Terraria.ID.TileID.Sets.Hallow[type])
			key += "/hallow";
		counts[key] = counts.GetValueOrDefault(key) + 1;
		if (Main.GameUpdateCount - reportedAt < ReportTicks)
			return;
		reportedAt = Main.GameUpdateCount;
		ModContent.GetInstance<global::AutoTerraMod.AutoTerraMod>().Logger.Info("surface digs: " + string.Join(", ", counts.OrderByDescending(entry => entry.Value).Take(25).Select(entry => $"{entry.Key}={entry.Value}")));
	}

}
