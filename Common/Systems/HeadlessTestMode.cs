using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class HeadlessTestMode : ModSystem
{
	public static bool Enabled => Main.dedServ && Environment.GetEnvironmentVariable("AUTOTERRA_HEADLESS") == "1";

	public override void Load()
	{
		if (Enabled)
			On_Netplay.UpdateConnectedClients += KeepWorldRunningWithoutClients;
	}

	static void KeepWorldRunningWithoutClients(On_Netplay.orig_UpdateConnectedClients orig)
	{
		orig();
		Netplay.HasClients = true;
	}

	public override void PostUpdateWorld()
	{
		if (Enabled && Main.netMode == NetmodeID.Server && Main.GameUpdateCount % 3600 == 0)
			Mod.Logger.Info($"headless test running: {Main.GameUpdateCount / 60}s of game time");
	}
}
