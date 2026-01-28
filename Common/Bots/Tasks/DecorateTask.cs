using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class DecorateTask : BotTask
{
	const float PlaceReach = 16f * 3f;

	Item decor;
	Point? spot;

	public DecorateTask(Bot bot) : base(bot) { }

	public override string NameKey => "Decorate";

	const float NearBase = 16f * 30f;

	public static bool Leisure(Bot bot) =>
		bot.Brain.Plan is Planning.IdleStep || !Main.dayTime || Vector2.Distance(bot.Center, bot.Faction.BaseCenter) < NearBase;

	public static bool IsWantedDecor(Bot bot, Item item) =>
		bot.Personality.Aspiration == Aspiration.MonsterHunter && item.createTile == TileID.Banners;

	public override float Evaluate()
	{
		decor = Inventory.Items(Avatar).FirstOrDefault(item => IsWantedDecor(Bot, item));
		if (decor == null || !Leisure(Bot))
			return 0f;
		spot = CeilingSpot(decor);
		if (spot == null)
			return 0f;
		Detail = decor.Name;
		return 65f;
	}

	Point? CeilingSpot(Item item)
	{
		var settlement = Bot.Faction.Settlement;
		if (settlement == null)
			return null;
		foreach (var structure in settlement.Structures.Where(structure => structure.Purpose != Building.StructurePurpose.Storage))
		{
			for (int x = structure.Left + 1; x < structure.Right; x++)
			{
				var candidate = new Point(x, structure.Ceiling + 1);
				if (World.TileQuery.IsSolid(x, structure.Ceiling) && Bot.Hands.CanPlaceAt(item, candidate))
					return candidate;
			}
		}
		return null;
	}

	public override void Tick()
	{
		if (spot is not Point target || decor == null || decor.IsAir)
		{
			IsFinished = true;
			return;
		}
		var below = new Vector2(target.X * 16f + 8f, (target.Y + 3) * 16f);
		if (Vector2.Distance(Bot.Center, below) > PlaceReach)
		{
			if (!TravelOrTunnel(below, PlaceReach * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		string name = decor.Name;
		if (Bot.Hands.Place(decor, target))
		{
			BotDiagnostics.Log(Bot, $"hung {name} at {target}");
			IsFinished = true;
		}
	}
}
