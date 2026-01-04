using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public sealed class BotGrapple
{
	const float DefaultRange = 300f;
	const float PullSpeed = 11f;
	const float HangDistance = 20f;
	const int MaxHangTicks = 600;

	readonly Bot bot;
	int hangTicks;

	public BotGrapple(Bot bot) => this.bot = bot;

	public Vector2? Anchor { get; private set; }
	public bool IsAttached => Anchor != null;
	public bool IsHanging { get; private set; }

	public bool HasHook => FindHook() != null;

	public bool TryLatch(NPC body, Vector2 target)
	{
		var hook = FindHook();
		if (hook == null)
			return false;
		var anchor = FindAnchor(body, target, RangeOf(hook.shoot));
		if (anchor == null)
			return false;
		Anchor = anchor;
		hangTicks = 0;
		body.noGravity = true;
		SoundEngine.PlaySound(SoundID.Item1, body.Center);
		BotDiagnostics.Log(bot, $"grappled to {anchor.Value.ToTileCoordinates()}");
		return true;
	}

	public void Tick(NPC body, Vector2? destination)
	{
		if (Anchor is not Vector2 anchor)
			return;
		var toAnchor = anchor - body.Center;
		float distance = toAnchor.Length();
		IsHanging = distance <= HangDistance;
		body.velocity = IsHanging ? Vector2.Zero : toAnchor / distance * PullSpeed;
		if (IsHanging && ++hangTicks > MaxHangTicks)
			Release(body, jump: false);
		else if (destination is Vector2 target && ShouldLetGo(anchor, target))
			Release(body, jump: target.Y < anchor.Y);
	}

	public void Release(NPC body, bool jump)
	{
		Anchor = null;
		IsHanging = false;
		body.noGravity = false;
		if (jump)
			body.velocity.Y = -5f;
	}

	static bool ShouldLetGo(Vector2 anchor, Vector2 target) =>
		target.Y > anchor.Y + 48f || target.Y < anchor.Y - 24f || System.Math.Abs(target.X - anchor.X) > 16f * 6f;

	Item FindHook() => Inventory.Items(bot.Avatar)
		.Where(item => item.shoot > ProjectileID.None && item.shoot < Main.projHook.Length && Main.projHook[item.shoot])
		.MaxBy(item => RangeOf(item.shoot));

	static Vector2? FindAnchor(NPC body, Vector2 target, float range)
	{
		var origin = body.Center;
		int reach = (int)(range / 16f);
		var originTile = origin.ToTileCoordinates();
		Vector2? best = null;
		float bestScore = float.MaxValue;
		for (int x = originTile.X - reach; x <= originTile.X + reach; x++)
		{
			for (int y = originTile.Y - reach; y < originTile.Y - 2; y++)
			{
				if (!TileQuery.IsSolid(x, y) && !TileQuery.IsPlatform(x, y))
					continue;
				var point = new Vector2(x * 16 + 8, y * 16 + 16);
				if (Vector2.Distance(origin, point) > range || TileQuery.IsSolid(x, y + 1))
					continue;
				if (!Collision.CanHitLine(origin, 1, 1, point, 1, 1))
					continue;
				float score = Vector2.Distance(point, target);
				if (score < bestScore)
				{
					bestScore = score;
					best = point;
				}
			}
		}
		return best;
	}

	public static float RangeOf(int hookProjectile)
	{
		if (hookProjectile >= ProjectileID.Count)
		{
			var sample = ContentSamples.ProjectilesByType[hookProjectile];
			return sample.ModProjectile?.GrappleRange() ?? DefaultRange;
		}
		if (hookProjectile is >= 230 and <= 235)
			return DefaultRange + (hookProjectile - 230) * 30f;
		return hookProjectile switch
		{
			32 => 400f,
			73 or 74 => 440f,
			165 => 375f,
			256 => 350f,
			315 => 500f,
			322 or 332 => 550f,
			331 or 372 => 400f,
			>= 646 and <= 649 => 550f,
			652 => 600f,
			935 or 446 => 500f,
			>= 486 and <= 489 => 480f,
			_ => DefaultRange
		};
	}
}
