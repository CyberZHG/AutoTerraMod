using System.Linq;
using AutoTerraMod.Common.Bots;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class AmmoScenario : ModSystem
{
	const int ReportIntervalTicks = 1800;

	static readonly int[] Shopkeepers = { NPCID.ArmsDealer, NPCID.Merchant };

	static bool Active => HeadlessTestMode.Scenario is "ammo" or "stockpile";

	public static void Prepare()
	{
		if (!Active)
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			foreach (int type in Shopkeepers)
			{
				int index = NPC.NewNPC(new EntitySource_WorldEvent(), (int)faction.BaseCenter.X, (int)faction.BaseCenter.Y, type);
				Main.npc[index].homeless = false;
				Main.npc[index].homeTileX = faction.BaseTile.X;
				Main.npc[index].homeTileY = faction.BaseTile.Y;
			}
		}
		foreach (var bot in BotWorldSystem.Bots)
		{
			HeadlessTestMode.Give(bot, ItemID.FlintlockPistol, 1);
			HeadlessTestMode.Give(bot, ItemID.MusketBall, 20);
			HeadlessTestMode.Give(bot, ItemID.GoldCoin, 10);
			HeadlessTestMode.Give(bot, ItemID.Bone, 99);
			HeadlessTestMode.Give(bot, ItemID.WoodenSword, 1);
		}
		if (HeadlessTestMode.Scenario == "stockpile")
		{
			foreach (var faction in BotWorldSystem.Factions)
				BuildInstantly(faction);
			foreach (var bot in BotWorldSystem.Bots)
			{
				HeadlessTestMode.Give(bot, ItemID.Chest, 2);
				HeadlessTestMode.Give(bot, ItemID.Torch, 60);
				HeadlessTestMode.Give(bot, ItemID.WorkBench, 1);
				HeadlessTestMode.Give(bot, ItemID.Furnace, 1);
				HeadlessTestMode.Give(bot, ItemID.IronAnvil, 1);
				HeadlessTestMode.Give(bot, ItemID.Rope, 100);
			}
		}
		ModContent.GetInstance<AutoTerraMod>().Logger.Info("ammo scenario prepared: flintlock + 20 musket balls + 10 gold each; Arms Dealer and Merchant at every base");
	}

	const int InstantPasses = 6;

	static void BuildInstantly(Factions.Faction faction)
	{
		var settlement = faction.Settlement;
		if (settlement == null)
			return;
		var design = Building.HouseInspector.MaterialsFor(faction);
		foreach (var structure in new[] { settlement.Hall, settlement.Storage })
		{
			for (int pass = 0; pass < InstantPasses; pass++)
			{
				var pending = structure.PendingOperations(design).ToList();
				if (pending.Count == 0)
					break;
				foreach (var operation in pending)
					Apply(structure, operation, design);
			}
		}
		Building.HouseInspector.Invalidate(faction);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"stockpile scenario: built hall and storage for {faction.Name}; storage complete={Building.HouseInspector.IsComplete(faction, settlement.Storage)}");
	}

	static void Apply(Building.HouseBlueprint structure, Building.BuildOperation operation, Building.HouseDesign design)
	{
		var (x, y) = (operation.Tile.X, operation.Tile.Y);
		var action = Building.HouseMaterials.ReplacementFor(operation.Action);
		if (action is Building.BuildAction.ChopTree or Building.BuildAction.ClearTile or Building.BuildAction.RemoveScaffold || operation.Action is Building.BuildAction.RenovateBlock)
			WorldGen.KillTile(x, y, noItem: true);
		if (operation.Action is Building.BuildAction.RenovateWall)
			WorldGen.KillWall(x, y);
		int itemType = Building.HouseMaterials.DefaultItem(action, design);
		if (itemType == ItemID.None)
			return;
		var sample = ContentSamples.ItemsByType[itemType];
		if (sample.createWall > 0)
			WorldGen.PlaceWall(x, y, sample.createWall, mute: true);
		else if (action is Building.BuildAction.PlaceTable or Building.BuildAction.PlaceChair or Building.BuildAction.PlaceBed)
			structure.FurnitureSpots(operation.Tile).Any(spot => BotHands.TryPlaceTile(sample, spot));
		else if (sample.createTile >= 0)
		{
			if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType != sample.createTile)
				WorldGen.KillTile(x, y, noItem: true);
			BotHands.TryPlaceTile(sample, operation.Tile);
		}
		WorldGen.SquareTileFrame(x, y);
	}

	const int ShortageTick = 3 * 3600;

	static void StageShortage()
	{
		foreach (var faction in BotWorldSystem.Factions)
		{
			var members = BotWorldSystem.Bots.Where(bot => bot.FactionId == faction.Id && bot.IsAlive).ToList();
			if (members.Count < 2)
				continue;
			var needy = members[0];
			Inventory.Consume(needy.Avatar, new int[] { ItemID.MusketBall }, Inventory.Count(needy.Avatar, ItemID.MusketBall));
			var giver = members.MaxBy(bot => bot.Personality.Cooperation == needy.Personality.Cooperation ? -1f : bot.Personality.Cooperation);
			HeadlessTestMode.Give(giver, ItemID.MusketBall, 500);
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"stockpile scenario: {needy.Name} ran dry, {giver.Name} (cooperation {giver.Personality.Cooperation:0.00}) carries 500 extra");
		}
	}

	public override void PostUpdateWorld()
	{
		if (HeadlessTestMode.Scenario == "stockpile" && Main.GameUpdateCount == ShortageTick)
			StageShortage();
		if (!Active || Main.GameUpdateCount % ReportIntervalTicks != 0)
			return;
		foreach (var bot in BotWorldSystem.Bots)
		{
			int bullets = Inventory.Items(bot.Avatar).Where(item => item.ammo == AmmoID.Bullet).Sum(item => item.stack);
			long coins = Utils.CoinsCount(out _, bot.Avatar.inventory);
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"ammo report {bot.Faction.Name}/{bot.Name}: bullets={bullets} coins={coins} weapon={bot.Hands.BestWeapon()?.Name} plan={bot.Brain.Plan} special=[{string.Join(",", bot.Avatar.inventory.Skip(Inventory.MainSlotCount).Take(8).Select(item => item.IsAir ? "-" : $"{item.Name}x{item.stack}"))}] strayCoinsOrAmmo={bot.Avatar.inventory.Take(Inventory.MainSlotCount).Count(item => item.IsACoin || item.ammo > 0 && !item.notAmmo)}");
		}
	}
}
