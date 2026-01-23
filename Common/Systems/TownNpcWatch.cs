using AutoTerraMod.Common.Bots;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class TownNpcWatch : GlobalNPC
{
	public override void OnSpawn(NPC npc, IEntitySource source)
	{
		if (npc.townNPC && BotWorldSystem.IsRunning)
			BotDiagnostics.LogWorld($"town NPC {npc.GivenOrTypeName} ({npc.TypeName}) arrived at tile {npc.Center.ToTileCoordinates()}");
	}
}
