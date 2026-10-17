using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class NailheadVolley : GlobalNPC
{
	const int NailProjectile = 498;
	const float VolleyCooldown = 30f;

	public override bool InstancePerEntity => true;

	bool volleyDue;

	public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => entity.type == NPCID.Nailhead;

	public override bool PreAI(NPC npc)
	{
		volleyDue = Main.netMode != NetmodeID.MultiplayerClient && npc.justHit && npc.localAI[3] <= 1f;
		if (volleyDue)
			npc.localAI[3] = 2f;
		return true;
	}

	public override void PostAI(NPC npc)
	{
		if (!volleyDue)
			return;
		volleyDue = false;
		npc.localAI[3] = 0f;
		if (Main.rand.Next(3) != 0)
			return;
		npc.localAI[3] = VolleyCooldown;
		int count = Main.rand.Next(3, 6);
		var targets = new List<Player>();
		for (int slot = 0; slot < Main.maxPlayers && targets.Count < count; slot++)
		{
			var player = Main.player[slot];
			if (player.active && !player.dead && Collision.CanHitLine(npc.position, npc.width, npc.height, player.position, player.width, player.height))
				targets.Add(player);
		}
		targets = targets.OrderBy(_ => Main.rand.Next()).ToList();
		for (int shot = 0; shot < count; shot++)
		{
			float speed = Main.rand.Next(8, 13);
			var velocity = new Vector2(Main.rand.Next(-100, 101), Main.rand.Next(-100, 101)).SafeNormalize(Vector2.UnitY);
			if (targets.Count > 0)
			{
				var player = targets[^1];
				targets.RemoveAt(targets.Count - 1);
				velocity = (player.Center - npc.Center).SafeNormalize(Vector2.UnitY);
			}
			Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center.X, npc.position.Y + npc.width / 4, velocity.X * speed, velocity.Y * speed, NailProjectile, (int)(npc.damage * 0.15), 1f, Main.myPlayer);
		}
	}
}
