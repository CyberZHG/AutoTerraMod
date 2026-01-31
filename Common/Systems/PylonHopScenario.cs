using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AutoTerraMod.Common.Systems;

public sealed class PylonHopScenario : ModSystem
{
	const int PylonOffset = 30;
	const int PurityStyle = 0;
	const int UniversalStyle = 8;
	const int ProbeIntervalTicks = 600;

	static readonly int[] Residents = { NPCID.Merchant, NPCID.Nurse };
	static bool hopped;

	static bool Active => HeadlessTestMode.Scenario == "pylonhop";

	public static void Prepare()
	{
		if (!Active)
			return;
		foreach (var faction in BotWorldSystem.Factions)
		{
			var spot = Point.Zero;
			bool placed = false;
			int style = faction.Id == BotWorldSystem.Factions[0].Id ? PurityStyle : UniversalStyle;
			for (int offset = PylonOffset; offset < PylonOffset + 60 && !placed; offset++)
			{
				int x = faction.BaseTile.X + (faction.Id % 2 == 0 ? offset : -offset);
				spot = new Point(x, TileQuery.SurfaceY(x) - 1);
				placed = TileObject.CanPlace(spot.X, spot.Y, TileID.TeleportationPylon, style, 1, out var objectData) && TileObject.Place(objectData);
				if (placed)
					TileObjectData.CallPostPlacementPlayerHook(spot.X, spot.Y, TileID.TeleportationPylon, style, 1, objectData.alternate, objectData);
			}
			foreach (int type in Residents)
			{
				int index = NPC.NewNPC(new EntitySource_WorldEvent(), spot.X * 16, spot.Y * 16, type);
				var resident = Main.npc[index];
				resident.homeless = false;
				resident.homeTileX = spot.X;
				resident.homeTileY = spot.Y;
			}
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"pylon hop scenario: {faction.Name} pylon at {spot} placed={placed}");
		}
		Main.PylonSystem.RequestImmediateUpdate();
	}

	public override void PostUpdateWorld()
	{
		if (!Active || Main.GameUpdateCount % ProbeIntervalTicks != 0 || BotWorldSystem.Factions.Count < 2)
			return;
		var pylons = string.Join(", ", Main.PylonSystem.Pylons.Select(pylon => $"{pylon.TypeOfPylon}@{pylon.PositionInTiles}"));
		var traveller = BotWorldSystem.Bots.FirstOrDefault(bot => bot.IsAlive && bot.FactionId == BotWorldSystem.Factions[0].Id);
		var destination = BotWorldSystem.Factions[1].BaseCenter;
		var hop = traveller == null ? null : PylonTravel.Plan(traveller.Center, destination);
		ModContent.GetInstance<AutoTerraMod>().Logger.Info($"pylon hop report: pylons=[{pylons}] hop={hop?.From.PositionInTiles}->{hop?.To.PositionInTiles} inRange={hop is PylonHop h && PylonTravel.InRange(traveller, h)} hopped={hopped}");
		if (!hopped && hop is PylonHop ready && PylonTravel.InRange(traveller, ready))
			hopped = PylonTravel.TryTeleport(traveller, ready);
	}
}
