using System.Linq;
using System.Reflection;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots;

public readonly record struct PylonHop(TeleportPylonInfo From, TeleportPylonInfo To)
{
	public Vector2 StandingSpot => PylonTravel.StandingSpot(From);
}

public static class PylonTravel
{
	const float MaxWalkToPylon = 16f * 80f;
	const float MinSaving = 16f * 150f;
	const int TownNpcsNeeded = 2;

	public static Vector2 StandingSpot(TeleportPylonInfo pylon) => new((pylon.PositionInTiles.X + 1) * 16f + 8f, (pylon.PositionInTiles.Y + 3) * 16f - 13f);

	static Vector2 Center(TeleportPylonInfo pylon) => new((pylon.PositionInTiles.X + 1.5f) * 16f, (pylon.PositionInTiles.Y + 2f) * 16f);

	public static PylonHop? Plan(Vector2 from, Vector2 to)
	{
		var pylons = Main.PylonSystem.Pylons.Where(pylon => HasEnoughNpcs(pylon) && !RecentlyRefused(pylon) && MeetsBiome(pylon.TypeOfPylon, pylon.PositionInTiles)).ToList();
		if (pylons.Count < 2)
			return null;
		var near = pylons.MinBy(pylon => Vector2.Distance(Center(pylon), from));
		var far = pylons.MinBy(pylon => Vector2.Distance(Center(pylon), to));
		if (near.Equals(far))
			return null;
		float walkToPylon = Vector2.Distance(Center(near), from);
		float hopped = walkToPylon + Vector2.Distance(Center(far), to);
		if (walkToPylon > MaxWalkToPylon || hopped + MinSaving > Vector2.Distance(from, to))
			return null;
		return new PylonHop(near, far);
	}

	const uint RefusalMemoryTicks = 10 * 3600;

	static readonly System.Collections.Generic.Dictionary<Point16, uint> refusedAt = new();

	static bool RecentlyRefused(TeleportPylonInfo pylon) =>
		refusedAt.TryGetValue(pylon.PositionInTiles, out uint at) && Main.GameUpdateCount - at < RefusalMemoryTicks;

	static string RefusalReason(Bot bot, PylonHop hop)
	{
		if (!TeleportPylonsSystem.IsPlayerNearAPylon(bot.Avatar))
			return "not near a pylon";
		if (!MeetsBiome(hop.From.TypeOfPylon, hop.From.PositionInTiles))
			return $"source {hop.From.TypeOfPylon} biome requirement";
		if (!HasEnoughNpcs(hop.To))
			return "not enough NPCs";
		if (NPC.AnyDanger(quickBossNPCCheck: false, ignorePillarsAndMoonlordCountdown: true))
			return "danger";
		return $"{hop.To.TypeOfPylon} biome requirement";
	}

	public static bool HasEnoughNpcs(TeleportPylonInfo pylon) =>
		pylon.TypeOfPylon == TeleportPylonType.Victory || TeleportPylonsSystem.DoesPositionHaveEnoughNPCs(TownNpcsNeeded, pylon.PositionInTiles);

	static readonly FieldInfo SceneMetricsField = typeof(TeleportPylonsSystem).GetField("_sceneMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
	static readonly MethodInfo AcceptsTeleportation = typeof(TeleportPylonsSystem).GetMethod("DoesPylonAcceptTeleportation", BindingFlags.NonPublic | BindingFlags.Instance);
	const uint BiomeCacheTicks = 10 * 3600;
	static readonly System.Collections.Generic.Dictionary<Point16, (uint at, bool[] meets)> biomeCache = new();
	static readonly TeleportPylonType[] PylonTypes = System.Enum.GetValues<TeleportPylonType>().Where(type => type < TeleportPylonType.Count).ToArray();

	public static bool MeetsBiome(TeleportPylonType type, Point16 position)
	{
		if (type < 0 || type >= TeleportPylonType.Count)
			return true;
		if (!biomeCache.TryGetValue(position, out var cached) || Main.GameUpdateCount - cached.at >= BiomeCacheTicks)
		{
			cached = (Main.GameUpdateCount, ScanBiomes(position));
			biomeCache[position] = cached;
		}
		return cached.meets[(int)type];
	}

	static bool[] ScanBiomes(Point16 position)
	{
		var meets = new bool[(int)TeleportPylonType.Count];
		System.Array.Fill(meets, true);
		if (SceneMetricsField?.GetValue(Main.PylonSystem) is not SceneMetrics metrics || AcceptsTeleportation == null)
			return meets;
		metrics.ScanAndExportToMain(new SceneMetricsScanSettings { VisualScanArea = null, BiomeScanCenterPositionInWorld = position.ToWorldCoordinates(), ScanOreFinderData = false });
		foreach (var type in PylonTypes)
			meets[(int)type] = (bool)AcceptsTeleportation.Invoke(Main.PylonSystem, new object[] { new TeleportPylonInfo { PositionInTiles = position, TypeOfPylon = type }, Main.player[Main.myPlayer] });
		return meets;
	}

	public static TeleportPylonType TypeOfItem(int itemType) => (TeleportPylonType)ContentSamples.ItemsByType[itemType].placeStyle;

	const uint StrandedMemoryTicks = 60 * 3600;

	const int StrandedReach = 150;

	static readonly System.Collections.Generic.Dictionary<TeleportPylonType, (Point16 where, uint at)> strandedAt = new();

	public static bool StrandedRecently(TeleportPylonType type, Point16 near) =>
		strandedAt.TryGetValue(type, out var stranded) && Main.GameUpdateCount - stranded.at < StrandedMemoryTicks && System.Math.Abs(stranded.where.X - near.X) < StrandedReach;

	public static bool IsStranded(int x, int y)
	{
		if (Main.tile[x, y].TileType != TileID.TeleportationPylon)
			return false;
		foreach (var pylon in Main.PylonSystem.Pylons)
		{
			var origin = pylon.PositionInTiles;
			if (x >= origin.X && x < origin.X + 3 && y >= origin.Y && y < origin.Y + 4)
			{
				if (MeetsBiome(pylon.TypeOfPylon, origin))
					return false;
				strandedAt[pylon.TypeOfPylon] = (origin, Main.GameUpdateCount);
				return true;
			}
		}
		return false;
	}

	public static bool InRange(Bot bot, PylonHop hop)
	{
		if (bot.Npc is not NPC body)
			return false;
		BotVitals.SyncAvatar(body, bot.Avatar);
		return bot.Avatar.InInteractionRange(hop.From.PositionInTiles.X, hop.From.PositionInTiles.Y, TileReachCheckSettings.Pylons);
	}

	public static bool TryTeleport(Bot bot, PylonHop hop)
	{
		if (bot.Npc is not NPC body || NPC.AnyDanger(quickBossNPCCheck: false, ignorePillarsAndMoonlordCountdown: true))
			return false;
		var before = bot.Avatar.position;
		using (AvatarPresence.Enter())
			Main.PylonSystem.HandleTeleportRequest(hop.To, bot.Avatar.whoAmI);
		if (bot.Avatar.position == before)
		{
			string reason;
			using (AvatarPresence.Enter())
				reason = RefusalReason(bot, hop);
			if (reason != "not near a pylon")
				refusedAt[hop.To.PositionInTiles] = Main.GameUpdateCount;
			BotDiagnostics.Log(bot, $"pylon at {hop.From.PositionInTiles} refused to send me to {hop.To.PositionInTiles}: {reason}");
			return false;
		}
		body.Bottom = bot.Avatar.Bottom;
		body.velocity = Vector2.Zero;
		bot.Vitals.Teleported(body);
		BotDiagnostics.Log(bot, $"teleported by pylon from {hop.From.PositionInTiles} to {hop.To.PositionInTiles}");
		Spectating.SpectatorSystem.AnnounceJump(bot, "PylonHop", bot.Name);
		return true;
	}
}
