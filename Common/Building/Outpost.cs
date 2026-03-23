using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Building;

public static class Outpost
{
	public const int Width = 11;
	public const int Height = 6;
	public const int BlocksNeeded = 30;
	public const int WallsNeeded = Height - 2;
	const float RebuildDistance = 16f * 150f;

	public static bool IsBlock(Item item)
	{
		int tile = item.createTile;
		return tile >= 0 && Main.tileSolid[tile] && !Main.tileSolidTop[tile] && !Main.tileFrameImportant[tile]
			&& !TileID.Sets.Falling[tile] && !TileID.Sets.Ore[tile];
	}

	public static bool IsHouseWall(Item item) => item.createWall > WallID.None && Main.wallHouse[item.createWall];

	public static bool IsDoor(Item item) => item.createTile == TileID.ClosedDoor;

	public static int Count(Player avatar, System.Func<Item, bool> kind) => Inventory.Items(avatar).Where(kind).Sum(item => item.stack);

	public static bool CarriesKit(Player avatar) => Inventory.Count(avatar, ItemID.Bed) > 0 && Count(avatar, IsDoor) > 0 && Count(avatar, IsHouseWall) >= WallsNeeded;

	public static bool IsDeep(Bot bot) => bot.CenterTile.Y > Main.rockLayer && bot.CenterTile.Y < Main.UnderworldLayer - 10;

	const int AfieldTiles = 100;

	public static bool IsAfield(Bot bot) => IsDeep(bot) || System.Math.Abs(bot.CenterTile.X - bot.Faction.BaseTile.X) > AfieldTiles;

	public static bool WantsNew(Bot bot)
	{
		if (!IsAfield(bot))
			return false;
		var anchor = bot.OutpostBed is Point bed ? bed.ToWorldCoordinates() : BotWorldSystem.RespawnFeet(bot);
		return Vector2.Distance(bot.Center, anchor) > RebuildDistance;
	}

	public static bool IsUsable(Point bed) =>
		WorldGen.InWorld(bed.X, bed.Y, 10) && Main.tile[bed.X, bed.Y].HasTile && Main.tile[bed.X, bed.Y].TileType == TileID.Beds
		&& WorldGen.StartRoomCheck(bed.X, bed.Y);

	public sealed class Site
	{
		public Site(Point feet)
		{
			Left = feet.X - Width / 2;
			Floor = feet.Y + 1;
		}

		public int Left { get; }
		public int Right => Left + Width - 1;
		public int Floor { get; }
		public int Ceiling => Floor - Height + 1;
		public int WallColumn => Left + Width / 2;
		public Point DoorTile => new(Right, Floor - 2);
		public Point StandingNear(Point tile) => new(System.Math.Clamp(tile.X, Left + 1, Right - 2), Floor - 1);

		public bool IsDoorway(int x, int y) => x == Right && y >= Floor - 3 && y < Floor;

		public IEnumerable<Point> Interior()
		{
			for (int y = Ceiling + 1; y < Floor; y++)
				for (int x = Left + 1; x < Right; x++)
					yield return new Point(x, y);
		}

		public IEnumerable<Point> Shell()
		{
			for (int x = Left; x <= Right; x++)
			{
				yield return new Point(x, Floor);
				yield return new Point(x, Ceiling);
			}
			for (int y = Ceiling + 1; y < Floor; y++)
			{
				yield return new Point(Left, y);
				if (!IsDoorway(Right, y))
					yield return new Point(Right, y);
			}
		}

		public IEnumerable<Point> FloorRow()
		{
			for (int x = Left; x <= Right; x++)
				yield return new Point(x, Floor);
		}

		public IEnumerable<Point> Doorway()
		{
			for (int y = Floor - 3; y < Floor; y++)
				yield return new Point(Right, y);
		}

		public IEnumerable<Point> WallSpots()
		{
			for (int y = Ceiling + 1; y < Floor; y++)
				yield return new Point(WallColumn, y);
		}

		const int Spacing = 3;

		public bool Overlaps(Site other) =>
			Left - Spacing <= other.Right && other.Left - Spacing <= Right && Ceiling - Spacing <= other.Floor && other.Ceiling - Spacing <= Floor;

		public bool NearBed()
		{
			for (int x = Left - Width; x <= Right + Width; x++)
				for (int y = Ceiling - Height; y <= Floor + Height; y++)
					if (World.TileQuery.TypeAt(new Point(x, y)) == TileID.Beds)
						return true;
			return false;
		}

		public bool HasGround()
		{
			int solid = 0;
			for (int x = Left; x <= Right; x++)
			{
				if (World.TileQuery.IsSolid(x, Floor))
					solid++;
			}
			return solid * 2 >= Width;
		}

		public bool Buildable()
		{
			if (!WorldGen.InWorld(Left, Ceiling, 20) || !WorldGen.InWorld(Right, Floor, 20))
				return false;
			foreach (var tile in Interior().Concat(Shell()))
			{
				var here = Main.tile[tile.X, tile.Y];
				if (here.LiquidAmount > 0 || HouseInspector.IsProtected(tile.X, tile.Y))
					return false;
				if (here.HasTile && (TileID.Sets.BasicChest[here.TileType] || !WorldGen.CanKillTile(tile.X, tile.Y)))
					return false;
			}
			return true;
		}
	}
}
