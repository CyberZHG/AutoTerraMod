using System.Linq;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Bots;

public sealed class Explosives
{
	const int MaxExplodablePick = 65;
	public const float SafeDistance = 16f * 7f;

	static readonly (int item, int radius)[] Charges =
	{
		(ItemID.Bomb, 4), (ItemID.StickyBomb, 4), (ItemID.BouncyBomb, 4),
		(ItemID.Dynamite, 7), (ItemID.StickyDynamite, 7), (ItemID.BouncyDynamite, 7)
	};

	readonly Bot bot;
	int projectileIndex = -1;
	int projectileIdentity;
	Point target;

	public Explosives(Bot bot) => this.bot = bot;

	const int FuseTicks = 180;
	const float ThrowTicks = 10f;
	const float VolleySpread = 0.6f;

	public static bool IsExplosive(Item item) => Charges.Any(charge => charge.item == item.type);

	public bool Carries => Inventory.Items(bot.Avatar).Any(IsExplosive);

	Projectile Fused => projectileIndex >= 0 && Main.projectile[projectileIndex] is { active: true } projectile && projectile.identity == projectileIdentity ? projectile : null;

	public bool IsFused => Fused != null;

	public Vector2? BlastCenter => Fused?.Center;

	public static bool CanBlast(int x, int y)
	{
		if (!TileQuery.IsSolid(x, y) || TileQuery.InOcean(x, y))
			return false;
		int type = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[type] && !Main.tileDungeon[type] && TileLoader.CanExplode(x, y)
			&& Navigation.PickRequirement.MinimumPick(type, x, y) <= MaxExplodablePick
			&& (Main.hardMode || type != TileID.Hellstone)
			&& !Building.HouseInspector.IsProtected(x, y);
	}

	const int CampBlastMargin = 3;
	public const int ThrowRangeTiles = 20;

	public bool InThrowRange(Point tile) => Vector2.Distance(bot.Center, tile.ToWorldCoordinates()) <= ThrowRangeTiles * 16f;

	static bool NearAnyCamp(Point center, int reach) =>
		Systems.BotWorldSystem.Factions.Any(faction => faction.Settlement is { } camp
			&& center.X + reach >= camp.Left - Building.Settlement.ApronWidth && center.X - reach <= camp.Right + Building.Settlement.ApronWidth
			&& center.Y + reach >= camp.Top && center.Y - reach <= camp.Floor + reach);

	static bool SpareProtected(Point center, int radius)
	{
		for (int x = center.X - radius; x <= center.X + radius; x++)
		{
			for (int y = center.Y - radius; y <= center.Y + radius; y++)
			{
				if (WorldGen.InWorld(x, y, 2) && Main.tile[x, y].HasTile && Building.HouseInspector.IsProtected(x, y))
					return false;
			}
		}
		return true;
	}

	public bool Light(Point tile, bool spareProtected = true, int volley = 1)
	{
		if (IsFused || bot.Faction.Settlement?.Contains(tile.X, tile.Y) == true || !CanBlast(tile.X, tile.Y))
			return false;
		var charge = Charges.FirstOrDefault(candidate => Inventory.Count(bot.Avatar, candidate.item) > 0);
		if (charge.item == 0 || spareProtected && !SpareProtected(tile, charge.radius) || NearAnyCamp(tile, charge.radius + CampBlastMargin)
			|| !InThrowRange(tile) || NearAnyCamp(bot.CenterTile, charge.radius + CampBlastMargin))
			return false;
		var item = Inventory.Items(bot.Avatar).First(candidate => candidate.type == charge.item);
		string name = item.Name;
		var from = bot.Center;
		var aim = tile.ToWorldCoordinates() - from;
		var velocity = aim == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(aim) * System.Math.Min(item.shootSpeed, aim.Length() / ThrowTicks);
		int index = -1;
		int thrown = 0;
		for (; thrown < volley && !item.IsAir && item.type == charge.item; thrown++)
		{
			var spread = new Vector2((thrown - (volley - 1) / 2f) * VolleySpread, 0f);
			using (Systems.AvatarPresence.Enter())
				index = Projectile.NewProjectile(new Terraria.DataStructures.EntitySource_ItemUse(bot.Avatar, item), from, velocity + spread, item.shoot, item.damage, item.knockBack, bot.Avatar.whoAmI);
			if (Main.projectile[index].timeLeft > FuseTicks)
				Main.projectile[index].timeLeft = FuseTicks;
			Inventory.ConsumeOne(bot.Avatar, item);
		}
		projectileIndex = index;
		projectileIdentity = Main.projectile[index].identity;
		target = tile;
		SoundEngine.PlaySound(SoundID.Item1, tile.ToWorldCoordinates());
		BotDiagnostics.Log(bot, $"threw {thrown} {name} at {tile} to blast through");
		return true;
	}

	public void Tick()
	{
		if (projectileIndex >= 0 && Fused == null)
		{
			projectileIndex = -1;
			BotDiagnostics.Log(bot, $"bomb at {target} went off; the tile there is now {TileQuery.TypeAt(target)}");
			bot.Watchdog.RecordProgress();
		}
	}
}
