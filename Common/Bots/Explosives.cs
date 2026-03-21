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
	const int FuseTicks = 60;
	const int MaxExplodablePick = 65;
	public const float SafeDistance = 16f * 7f;

	static readonly (int item, int radius)[] Charges =
	{
		(ItemID.Bomb, 4), (ItemID.StickyBomb, 4), (ItemID.BouncyBomb, 4),
		(ItemID.Dynamite, 7), (ItemID.StickyDynamite, 7), (ItemID.BouncyDynamite, 7)
	};

	readonly Bot bot;
	Point? fused;
	int radius;
	int fuse;

	public Explosives(Bot bot) => this.bot = bot;

	public static bool IsExplosive(Item item) => Charges.Any(charge => charge.item == item.type);

	public bool Carries => Inventory.Items(bot.Avatar).Any(IsExplosive);

	public bool IsFused => fused != null;

	public Vector2? BlastCenter => fused?.ToWorldCoordinates();

	public static bool CanBlast(int x, int y)
	{
		if (!TileQuery.IsSolid(x, y))
			return false;
		int type = Main.tile[x, y].TileType;
		return !Main.tileFrameImportant[type] && !Main.tileDungeon[type] && TileLoader.CanExplode(x, y)
			&& Navigation.PickRequirement.MinimumPick(type, x, y) <= MaxExplodablePick
			&& (Main.hardMode || type != TileID.Hellstone)
			&& !Building.HouseInspector.IsProtected(x, y);
	}

	public bool Light(Point tile)
	{
		if (fused != null || bot.Faction.Settlement?.Contains(tile.X, tile.Y) == true || !CanBlast(tile.X, tile.Y))
			return false;
		var charge = Charges.FirstOrDefault(candidate => Inventory.Count(bot.Avatar, candidate.item) > 0);
		if (charge.item == 0)
			return false;
		var item = Inventory.Items(bot.Avatar).First(candidate => candidate.type == charge.item);
		string name = item.Name;
		Inventory.ConsumeOne(bot.Avatar, item);
		fused = tile;
		radius = charge.radius;
		fuse = FuseTicks;
		SoundEngine.PlaySound(SoundID.Item1, tile.ToWorldCoordinates());
		BotDiagnostics.Log(bot, $"lit {name} at {tile} to blast through");
		return true;
	}

	public void Tick()
	{
		if (fused is not Point center || --fuse > 0)
			return;
		fused = null;
		SoundEngine.PlaySound(SoundID.Item14, center.ToWorldCoordinates());
		for (int i = 0; i < 20; i++)
			Dust.NewDust(center.ToWorldCoordinates() - new Vector2(radius * 8f), radius * 16, radius * 16, DustID.Smoke);
		for (int x = center.X - radius; x <= center.X + radius; x++)
		{
			for (int y = center.Y - radius; y <= center.Y + radius; y++)
			{
				if ((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius && CanBlast(x, y))
					WorldGen.KillTile(x, y);
			}
		}
		bot.Watchdog.RecordProgress();
	}
}
