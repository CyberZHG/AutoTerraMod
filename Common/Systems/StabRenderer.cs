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

	public readonly record struct Target(Player Avatar, Vector2 Center, Vector2 Aim, Vector2 YoyoTarget);

	public static void Draw(IEnumerable<Bot> bots) => DrawTargets(bots.Select(bot => new Target(bot.Avatar, bot.Center, bot.Hands.AimDirection, bot.Hands.YoyoTarget)));

	public static void DrawTargets(IEnumerable<Target> targets)
	{
		var stabbing = targets.Where(target => IsStabbing(target.Avatar) || IsThrowing(target.Avatar) || IsHoldingTool(target.Avatar)).ToList();
		if (stabbing.Count == 0)
			return;
		var spriteBatch = Main.spriteBatch;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
		foreach (var target in stabbing)
		{
			if (IsHoldingTool(target.Avatar))
				DrawHeldTool(spriteBatch, target);
			else if (IsThrowing(target.Avatar))
				DrawYoyo(spriteBatch, target);
			else
				DrawStab(spriteBatch, target);
		}
		spriteBatch.End();
	}

	static bool IsStabbing(Player avatar)
	{
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.useStyle == ItemUseStyleID.Rapier;
	}

	static bool IsHeldTool(Item item) => item.channel && (item.pick > 0 || item.axe > 0 || item.hammer > 0);

	static bool IsHoldingTool(Player avatar)
	{
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.shoot > ProjectileID.None && IsHeldTool(item);
	}

	static bool IsThrowing(Player avatar)
	{
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.shoot > ProjectileID.None && item.useStyle != ItemUseStyleID.Rapier && !IsHeldTool(item)
			&& (BotHands.IsYoyo(item) || BotHands.IsWhip(item) || item.noUseGraphic && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Melee));
	}

	const float ToolHand = 6f;
	const float ToolGrip = 10f;
	const float ToolJitter = 1f;

	static void DrawHeldTool(SpriteBatch spriteBatch, Target target)
	{
		var item = target.Avatar.lastVisualizedSelectedItem;
		Main.instance.LoadProjectile(item.shoot);
		var texture = TextureAssets.Projectile[item.shoot].Value;
		var aim = target.Aim;
		var jitter = new Vector2(Main.rand.NextFloat(-ToolJitter, ToolJitter), Main.rand.NextFloat(-ToolJitter, ToolJitter));
		var hand = target.Center + new Vector2(0f, 2f) + aim * ToolHand + jitter;
		var light = Lighting.GetColor((hand + aim * texture.Width / 2f).ToTileCoordinates());
		var effects = aim.X < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;
		spriteBatch.Draw(texture, hand - Main.screenPosition, null, light, aim.ToRotation(), new Vector2(ToolGrip, texture.Height / 2f), 1f, effects, 0f);
	}

	const float YoyoSpinTurns = 6f;
	const float YoyoMaxThrow = 16f * 14f;
	const float YoyoFallbackThrow = 16f * 6f;
	static readonly Color YoyoString = new(220, 220, 220);

	static void DrawYoyo(SpriteBatch spriteBatch, Target target)
	{
		var avatar = target.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		Main.instance.LoadProjectile(item.shoot);
		var texture = TextureAssets.Projectile[item.shoot].Value;
		float progress = 1f - avatar.itemAnimation / (float)Math.Max(1, avatar.itemAnimationMax);
		var hand = target.Center + new Vector2(avatar.direction * 6f, -2f);
		var end = Vector2.Distance(hand, target.YoyoTarget) <= YoyoMaxThrow ? target.YoyoTarget : hand + target.Aim * YoyoFallbackThrow;
		var position = Vector2.Lerp(hand, end, (float)Math.Sin(progress * MathHelper.Pi));
		var line = position - hand;
		var light = Lighting.GetColor(position.ToTileCoordinates());
		if (ItemID.Sets.Spears[item.type])
		{
			var tip = hand + target.Aim * (SpearRest + (float)Math.Sin(progress * MathHelper.Pi) * SpearThrust);
			spriteBatch.Draw(texture, tip - Main.screenPosition, null, Lighting.GetColor(tip.ToTileCoordinates()), target.Aim.ToRotation() + MathHelper.PiOver4, texture.Size() / 2f, 1f, SpriteEffects.None, 0f);
			return;
		}
		if (IsJavelin(item))
		{
			if (progress > JavelinVisible)
				return;
			var flight = Vector2.Lerp(hand, end, progress / JavelinVisible);
			spriteBatch.Draw(texture, flight - Main.screenPosition, null, Lighting.GetColor(flight.ToTileCoordinates()), (end - hand).ToRotation() + MathHelper.PiOver2, texture.Size() / 2f, 1f, SpriteEffects.None, 0f);
			return;
		}
		if (BotHands.IsYoyo(item) || BotHands.IsWhip(item))
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, hand - Main.screenPosition, new Rectangle(0, 0, 1, 1), YoyoString.MultiplyRGB(light), line.ToRotation(), Vector2.Zero, new Vector2(line.Length(), 1f), SpriteEffects.None, 0f);
		spriteBatch.Draw(texture, position - Main.screenPosition, null, light, progress * MathHelper.TwoPi * YoyoSpinTurns, texture.Size() / 2f, 1f, SpriteEffects.None, 0f);
	}

	const float SpearRest = 8f;
	const float SpearThrust = 40f;
	const float JavelinVisible = 0.6f;

	static bool IsJavelin(Item item) => item.type is ItemID.DayBreak or ItemID.Javelin or ItemID.BoneJavelin;

	static void DrawStab(SpriteBatch spriteBatch, Target target)
	{
		var avatar = target.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		Main.instance.LoadItem(item.type);
		var texture = TextureAssets.Item[item.type].Value;
		float progress = 1f - avatar.itemAnimation / (float)Math.Max(1, avatar.itemAnimationMax);
		var aim = target.Aim;
		var position = target.Center + aim * (RestingReach + (float)Math.Sin(progress * MathHelper.Pi) * ThrustReach);
		var light = Lighting.GetColor(position.ToTileCoordinates());
		spriteBatch.Draw(texture, position - Main.screenPosition, null, item.GetAlpha(light), aim.ToRotation() + SpriteDiagonalOffset, texture.Size() / 2f, item.scale, SpriteEffects.None, 0f);
	}
}
