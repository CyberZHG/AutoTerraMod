using System.Linq;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public sealed class BotGrapple
{
	const float DefaultRange = 300f;
	const float CappedAnchorPenalty = 16f * 12f;
	const float PullSpeed = 11f;
	const float HangDistance = 20f;
	const float HangSnap = 1f;
	const int MaxHangTicks = 600;

	readonly Bot bot;
	int hangTicks;

	public BotGrapple(Bot bot) => this.bot = bot;

	public Vector2? Anchor { get; private set; }
	public bool IsAttached => Anchor != null;
	public bool IsHanging { get; private set; }

	public bool HasHook => FindHook() != null;
	public int HookProjectile => FindHook()?.shoot ?? ProjectileID.None;

	public int HookReachTiles => !Suppressed && FindHook() is Item hook ? (int)(RangeOf(hook.shoot) / 16f) - 1 : 0;

	const int LatchesBeforeSuppress = 3;
	const uint LatchWindowTicks = 600;
	const uint SuppressTicks = 1800;

	uint suppressedUntil;
	uint windowStart;
	int windowLatches;
	float windowStartY;

	public bool Suppressed => Main.GameUpdateCount < suppressedUntil;

	public void ForgetSuppression()
	{
		suppressedUntil = 0;
		windowLatches = 0;
	}

	void NoteLatch(NPC body)
	{
		if (Main.GameUpdateCount - windowStart > LatchWindowTicks)
		{
			windowStart = Main.GameUpdateCount;
			windowLatches = 0;
			windowStartY = body.Center.Y;
		}
		if (++windowLatches >= LatchesBeforeSuppress && body.Center.Y > windowStartY - 16f)
		{
			suppressedUntil = Main.GameUpdateCount + SuppressTicks;
			BotDiagnostics.Log(bot, "hooking keeps getting nowhere, leaving the hook alone for a while");
		}
	}

	const int ClimbPullTicks = 60;

	bool climbing;
	bool climbReleased;

	public bool LatchForClimb(NPC body, Vector2 hold)
	{
		if (Suppressed || FindHook() == null)
			return false;
		NoteLatch(body);
		Anchor = hold;
		pullTo = BesideEdge(body, hold);
		climbing = true;
		hangTicks = 0;
		closestPull = float.MaxValue;
		snaggedTicks = 0;
		body.noGravity = true;
		SoundEngine.PlaySound(SoundID.Item1, body.Center);
		BotDiagnostics.Log(bot, $"hooked {hold.ToTileCoordinates()} to climb");
		return true;
	}

	Vector2 pullTo;
	const float EdgeClearance = 2f;

	static Vector2 BesideEdge(NPC body, Vector2 hold)
	{
		int hang = World.TileQuery.IsSolid((int)(hold.X - 1f) / 16, (int)hold.Y / 16) ? 1 : -1;
		return hold + new Vector2(hang * (body.width / 2f + EdgeClearance), 0f);
	}

	bool hangMode;

	public bool HangingInPlace => hangMode && IsHanging;

	public bool LatchToHang(NPC body, Vector2 hold, Vector2 bodyCenter)
	{
		if (Suppressed || FindHook() == null)
			return false;
		Anchor = hold;
		pullTo = bodyCenter;
		hangMode = true;
		climbing = false;
		IsHanging = false;
		hangTicks = 0;
		closestPull = float.MaxValue;
		snaggedTicks = 0;
		body.noGravity = true;
		SoundEngine.PlaySound(SoundID.Item1, body.Center);
		BotDiagnostics.Log(bot, $"hooked {hold.ToTileCoordinates()} to hang");
		return true;
	}

	public bool ConsumeClimbRelease()
	{
		bool released = climbReleased;
		climbReleased = false;
		return released;
	}

	public bool TryLatch(NPC body, Vector2 target)
	{
		var hook = FindHook();
		if (Suppressed || hook == null)
			return false;
		var anchor = FindAnchor(body, target, RangeOf(hook.shoot));
		if (anchor == null)
			return false;
		NoteLatch(body);
		Anchor = anchor;
		pullTo = anchor.Value;
		lastAnchor = anchor;
		lastAnchorAt = Main.GameUpdateCount;
		hangTicks = 0;
		closestPull = float.MaxValue;
		snaggedTicks = 0;
		body.noGravity = true;
		SoundEngine.PlaySound(SoundID.Item1, body.Center);
		BotDiagnostics.Log(bot, $"grappled to {anchor.Value.ToTileCoordinates()}");
		return true;
	}

	public void Tick(NPC body, Vector2? destination)
	{
		if (Anchor is not Vector2 anchor)
			return;
		var toAnchor = pullTo - body.Center;
		float distance = toAnchor.Length();
		if (hangMode)
		{
			IsHanging = distance <= HangSnap;
			body.velocity = IsHanging ? Vector2.Zero : toAnchor / distance * System.Math.Min(PullSpeed, distance);
			if (!IsHanging && Snagged(distance) && !DigThroughSnag(body, toAnchor / distance))
				Release(body, jump: false);
			else if (IsHanging && ++hangTicks > MaxHangTicks)
				Release(body, jump: false);
			return;
		}
		IsHanging = distance <= HangDistance;
		body.velocity = IsHanging ? Vector2.Zero : toAnchor / distance * PullSpeed;
		if (!IsHanging && Snagged(distance) && !DigThroughSnag(body, toAnchor / distance))
		{
			if (distance > SnagLogDistance)
				BotDiagnostics.Log(bot, $"hook pull snagged on an edge {distance / 16f:0.0} tiles short of {anchor.ToTileCoordinates()}, letting go");
			Release(body, jump: true);
			return;
		}
		if (destination is Vector2 above && DigsUpward(anchor, above))
		{
			if (!HoldsAnchor(anchor) || IsHanging && !DigThroughSnag(body, -Vector2.UnitY))
				Release(body, jump: true);
			return;
		}
		if (climbing)
		{
			if (IsHanging || ++hangTicks > ClimbPullTicks)
			{
				Release(body, jump: true);
				climbReleased = true;
			}
			return;
		}
		if (IsHanging && ++hangTicks > MaxHangTicks)
			Release(body, jump: false);
		else if (destination is Vector2 target && ShouldLetGo(anchor, target) && !DigsUpward(anchor, target))
			Release(body, jump: target.Y < anchor.Y);
	}

	const int SnagTicks = 30;
	const float SnagLogDistance = 16f * 3f;
	const float PullProgress = 2f;
	float closestPull;
	int snaggedTicks;

	const int MaxSnagDigTicks = 240;
	int snagDigTicks;

	bool Snagged(float distance)
	{
		if (distance < closestPull - PullProgress)
		{
			closestPull = distance;
			snaggedTicks = 0;
			snagDigTicks = 0;
			return false;
		}
		return ++snaggedTicks > SnagTicks;
	}

	static bool HoldsAnchor(Vector2 anchor) =>
		TileQuery.IsSolid(anchor.ToTileCoordinates()) || TileQuery.IsSolid((anchor - new Vector2(0f, 2f)).ToTileCoordinates());

	bool DigsUpward(Vector2 anchor, Vector2 target) =>
		!climbing && !hangMode && target.Y < anchor.Y - 24f && bot.Hands.BestPickaxe != null && snagDigTicks <= MaxSnagDigTicks
		&& bot.Faction.Settlement?.Contains((int)(anchor.X / 16f), (int)(anchor.Y / 16f)) != true;

	bool DigThroughSnag(NPC body, Vector2 heading)
	{
		if (++snagDigTicks > MaxSnagDigTicks || bot.Hands.BestPickaxe == null)
			return false;
		var ahead = body.Hitbox;
		ahead.Offset((int)(heading.X * 16f), (int)(heading.Y * 16f));
		var settlement = bot.Faction.Settlement;
		for (int x = ahead.Left / 16; x <= (ahead.Right - 1) / 16; x++)
		{
			for (int y = ahead.Top / 16; y <= (ahead.Bottom - 1) / 16; y++)
			{
				if (!TileQuery.IsSolid(x, y) || settlement?.Contains(x, y) == true)
					continue;
				if (bot.Hands.Dig(new Point(x, y)) != ToolResult.Working)
					return false;
				snaggedTicks = SnagTicks;
				return true;
			}
		}
		return false;
	}

	public void Release(NPC body, bool jump)
	{
		Anchor = null;
		IsHanging = false;
		climbing = false;
		hangMode = false;
		body.noGravity = false;
		if (jump)
			body.velocity.Y = -5f;
	}

	public bool TryArrestFall(NPC body)
	{
		var hook = FindHook();
		if (hook == null)
			return false;
		var anchor = FindHold(body, RangeOf(hook.shoot));
		if (anchor == null)
			return false;
		Anchor = anchor;
		pullTo = anchor.Value;
		hangTicks = 0;
		closestPull = float.MaxValue;
		snaggedTicks = 0;
		body.noGravity = true;
		SoundEngine.PlaySound(SoundID.Item1, body.Center);
		BotDiagnostics.Log(bot, $"hooked {anchor.Value.ToTileCoordinates()} to break a fall");
		return true;
	}

	const float BelowPenalty = 400f;

	static Vector2? FindHold(NPC body, float range)
	{
		var origin = body.Center;
		int reach = (int)(range / 16f);
		var originTile = origin.ToTileCoordinates();
		Vector2? best = null;
		float bestScore = float.MaxValue;
		for (int x = originTile.X - reach; x <= originTile.X + reach; x++)
		{
			for (int y = originTile.Y - reach; y <= originTile.Y + 2; y++)
			{
				if (!TileQuery.IsSolid(x, y) && !TileQuery.IsPlatform(x, y))
					continue;
				var point = new Vector2(x * 16 + 8, y * 16 + 8);
				float distance = Vector2.Distance(origin, point);
				if (distance > range || !Collision.CanHitLine(origin, 1, 1, point + Vector2.Normalize(origin - point) * 10f, 1, 1))
					continue;
				float score = distance + (y > originTile.Y ? BelowPenalty : 0f);
				if (score < bestScore)
				{
					bestScore = score;
					best = point;
				}
			}
		}
		return best;
	}

	static bool ShouldLetGo(Vector2 anchor, Vector2 target) =>
		target.Y > anchor.Y + 48f || target.Y < anchor.Y - 24f || System.Math.Abs(target.X - anchor.X) > 16f * 6f;

	Item FindHook()
	{
		var worn = bot.Avatar.miscEquips[Equipment.HookSlot];
		Item best = Equipment.PowerOf(worn, GearKind.Hook) > 0 ? worn : null;
		foreach (var item in Inventory.Items(bot.Avatar))
		{
			if (item.shoot > ProjectileID.None && item.shoot < Main.projHook.Length && Main.projHook[item.shoot] && (best == null || RangeOf(item.shoot) > RangeOf(best.shoot)))
				best = item;
		}
		return best;
	}

	const float MinimumUpwardGain = 16f * 4f;
	const float RepeatAnchorDistance = 16f * 2f;
	const uint RepeatAnchorTicks = 600;

	Vector2? lastAnchor;
	uint lastAnchorAt;

	Vector2? FindAnchor(NPC body, Vector2 target, float range)
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
				if (target.Y < origin.Y && point.Y > origin.Y - MinimumUpwardGain)
					continue;
				if (lastAnchor is Vector2 previous && Vector2.Distance(previous, point) < RepeatAnchorDistance && Main.GameUpdateCount - lastAnchorAt < RepeatAnchorTicks)
					continue;
				bool capped = TileQuery.IsSolid(x, y - 1) && TileQuery.IsSolid(x, y - 2);
				float score = Vector2.Distance(point, target) + (capped && target.Y < point.Y ? CappedAnchorPenalty : 0f);
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
