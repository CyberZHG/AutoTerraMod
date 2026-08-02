using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Content.NPCs;

public sealed class BotNPC : ModNPC
{
	public int BotId = -1;

	public override string Texture => $"Terraria/Images/NPC_{NPCID.Guide}";

	public Bot Bot => BotWorldSystem.FindBot(BotId);

	public override void SetStaticDefaults()
	{
		NPCID.Sets.NPCBestiaryDrawOffset[Type] = new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true };
	}

	public override void SetDefaults()
	{
		NPC.width = 20;
		NPC.height = 42;
		NPC.aiStyle = -1;
		NPC.lifeMax = 100;
		NPC.damage = 0;
		NPC.defense = 0;
		NPC.knockBackResist = 0f;
		NPC.friendly = true;
		NPC.dontTakeDamageFromHostiles = true;
		NPC.npcSlots = 0f;
		NPC.dontCountMe = true;
		NPC.chaseable = false;
		NPC.HitSound = SoundID.PlayerHit;
	}

	public override void AI()
	{
		var bot = Bot;
		if (bot == null)
		{
			NPC.active = false;
			return;
		}
		Common.Navigation.PathFinder.Prioritized = bot == Common.Spectating.SpectatorSystem.Followed;
		using (Perf.Measure(PerfSection.BotAi))
		using (Common.World.ShrineGuard.BotActing())
			bot.Brain.Tick(NPC);
		Common.Navigation.PathFinder.Prioritized = false;
	}

	public override void PostAI()
	{
		if (Bot is { IsAlive: true } bot && bot.Brain.Override == null)
			bot.Motor.ApplyTerrainHazards(NPC);
	}

	public override bool CheckActive() => false;

	public override bool NeedSaving() => false;

	public override bool? CanFallThroughPlatforms() => Bot?.Motor.WantsToDropThroughPlatforms ?? false;

	public override bool? CanBeHitByItem(Player player, Item item) => false;

	public override bool? CanBeHitByProjectile(Projectile projectile) => false;

	public override bool CanBeHitByNPC(NPC attacker) => false;

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => false;
}
