using System.Collections.Generic;
using AutoTerraMod.Common.Systems;
using Terraria;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public sealed class EntityIndex : ModSystem
{
	static readonly List<NPC> activeNpcs = new(Main.maxNPCs);
	static readonly List<Projectile> avatarProjectiles = new();
	static readonly List<Projectile> damagingProjectiles = new();

	public static IReadOnlyList<NPC> ActiveNpcs => activeNpcs;
	public static IReadOnlyList<Projectile> AvatarProjectiles => avatarProjectiles;
	public static IReadOnlyList<Projectile> DamagingProjectiles => damagingProjectiles;

	public override void PreUpdateNPCs()
	{
		activeNpcs.Clear();
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			if (Main.npc[i].active)
				activeNpcs.Add(Main.npc[i]);
		}
		avatarProjectiles.Clear();
		damagingProjectiles.Clear();
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			var projectile = Main.projectile[i];
			if (!projectile.active)
				continue;
			if (projectile.owner >= BotWorldSystem.FirstAvatarSlot && projectile.owner < Main.maxPlayers - 1)
				avatarProjectiles.Add(projectile);
			if (projectile.damage > 0 && (projectile.hostile || projectile.friendly))
				damagingProjectiles.Add(projectile);
		}
	}
}
