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
		var stabbing = bots.Where(IsStabbing).ToList();
		if (stabbing.Count == 0)
			return;
		var spriteBatch = Main.spriteBatch;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
		foreach (var bot in stabbing)
			DrawStab(spriteBatch, bot);
		spriteBatch.End();
	}

	static bool IsStabbing(Bot bot)
	{
		var avatar = bot.Avatar;
		var item = avatar.lastVisualizedSelectedItem;
		return avatar.itemAnimation > 0 && item != null && !item.IsAir && item.useStyle == ItemUseStyleID.Rapier;
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
