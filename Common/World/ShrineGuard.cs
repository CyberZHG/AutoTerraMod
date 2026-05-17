using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public sealed class ShrineGuard : GlobalTile
{
	static int botActing;
	static int bulbAllowed;

	public static IDisposable BotActing() => new Scope(() => botActing++, () => botActing--);

	public static IDisposable AllowBulb() => new Scope(() => bulbAllowed++, () => bulbAllowed--);

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (type == TileID.PlanteraBulb && bulbAllowed == 0 && (botActing > 0 || !PlayerNear(i, j)))
			fail = true;
	}

	public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => !GuardsBulb(i, j);

	public override bool CanExplode(int i, int j, int type) => !GuardsBulb(i, j);

	static bool GuardsBulb(int i, int j) =>
		bulbAllowed == 0 && WorldGen.InWorld(i, j - 1, 2) && Main.tile[i, j - 1].HasTile && Main.tile[i, j - 1].TileType == TileID.PlanteraBulb && (botActing > 0 || !PlayerNear(i, j));

	const float PlayerReach = 16f * 30f;

	static bool PlayerNear(int i, int j)
	{
		var bulb = new Vector2(i * 16f, j * 16f);
		return Main.player.Any(player => player.active && !player.dead && Vector2.Distance(player.Center, bulb) < PlayerReach && !Systems.BotWorldSystem.Bots.Any(bot => bot.Avatar == player));
	}

	sealed class Scope : IDisposable
	{
		readonly Action exit;

		public Scope(Action enter, Action exit)
		{
			enter();
			this.exit = exit;
		}

		public void Dispose() => exit();
	}
}
