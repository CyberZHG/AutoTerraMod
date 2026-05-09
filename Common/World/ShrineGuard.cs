using System;
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
		if (type == TileID.PlanteraBulb && botActing > 0 && bulbAllowed == 0)
			fail = true;
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
