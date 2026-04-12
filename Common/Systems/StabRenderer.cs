using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace AutoTerraMod.Common.Systems;

public static class StabRenderer
{
	const float RestingReach = 10f;
	const float ThrustReach = 18f;
	const float SpriteDiagonalOffset = MathHelper.PiOver4;

	public static void Draw(IEnumerable<Bot> bots)
	{
		var stabbing = bots.Where(bot => IsStabbing(bot) || IsThrowing(bot)).ToList();
		if (stabbing.Count == 0)
			return;
		var spriteBatch = Main.spriteBatch;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
		foreach (var bot in stabbing)
		{
			if (IsThrowing(bot))
				DrawYoyo(spriteBatch, bot);
			else
				DrawStab(spriteBatch, bot);
		}
		spriteBatch.End();
	}

	static bool IsStabbing(Bot bot)
	{
		var avatar = bot.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.useStyle == ItemUseStyleID.Rapier;
	}

	static bool IsThrowing(Bot bot)
	{
		var avatar = bot.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.shoot > ProjectileID.None && item.useStyle != ItemUseStyleID.Rapier
			&& (BotHands.IsYoyo(item) || item.noUseGraphic && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Melee));
	}

	const float YoyoSpinTurns = 6f;
	const float YoyoMaxThrow = 16f * 14f;
	const float YoyoFallbackThrow = 16f * 6f;
	static readonly Color YoyoString = new(220, 220, 220);

	static void DrawYoyo(SpriteBatch spriteBatch, Bot bot)
	{
		var avatar = bot.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		Main.instance.LoadProjectile(item.shoot);
		var texture = TextureAssets.Projectile[item.shoot].Value;
		float progress = 1f - avatar.itemAnimation / (float)Math.Max(1, avatar.itemAnimationMax);
		var hand = bot.Center + new Vector2(avatar.direction * 6f, -2f);
		var target = Vector2.Distance(hand, bot.Hands.YoyoTarget) <= YoyoMaxThrow ? bot.Hands.YoyoTarget : hand + bot.Hands.AimDirection * YoyoFallbackThrow;
		var position = Vector2.Lerp(hand, target, (float)Math.Sin(progress * MathHelper.Pi));
		var line = position - hand;
		var light = Lighting.GetColor(position.ToTileCoordinates());
		if (BotHands.IsYoyo(item))
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, hand - Main.screenPosition, new Rectangle(0, 0, 1, 1), YoyoString.MultiplyRGB(light), line.ToRotation(), Vector2.Zero, new Vector2(line.Length(), 1f), SpriteEffects.None, 0f);
		spriteBatch.Draw(texture, position - Main.screenPosition, null, light, progress * MathHelper.TwoPi * YoyoSpinTurns, texture.Size() / 2f, 1f, SpriteEffects.None, 0f);
	}

	static void DrawStab(SpriteBatch spriteBatch, Bot bot)
	{
		var avatar = bot.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		Main.instance.LoadItem(item.type);
		var texture = TextureAssets.Item[item.type].Value;
		float progress = 1f - avatar.itemAnimation / (float)Math.Max(1, avatar.itemAnimationMax);
		var aim = bot.Hands.AimDirection;
		var position = bot.Center + aim * (RestingReach + (float)Math.Sin(progress * MathHelper.Pi) * ThrustReach);
		var light = Lighting.GetColor(position.ToTileCoordinates());
		spriteBatch.Draw(texture, position - Main.screenPosition, null, item.GetAlpha(light), aim.ToRotation() + SpriteDiagonalOffset, texture.Size() / 2f, item.scale, SpriteEffects.None, 0f);
	}
}
