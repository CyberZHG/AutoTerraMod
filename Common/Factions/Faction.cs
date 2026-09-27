using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Building;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Factions;

public sealed class Faction
{
	static readonly int[] PvpTeams = { 1, 3, 2, 4, 5, 0 };

	Settlement settlement;

	public int Id { get; init; }
	public Point BaseTile { get; set; }
	public HouseDesign HouseDesign { get; set; }
	public FactionKnowledge Knowledge { get; } = new();
	public HashSet<Point> Scaffolding { get; } = new();
	public HashSet<int> DefeatedBosses { get; } = new();
	public Dictionary<int, float> BossDpsCalibration { get; } = new();
	public HashSet<int> GearPlateauBosses { get; } = new();
	public Dictionary<int, (float best, uint improvedAt)> PlateauProgress { get; } = new();
	public (Vector2 point, uint until)? Rally { get; set; }
	public uint RallyCooldownUntil { get; set; }
	public int RallyLeaderId { get; set; } = -1;
	public bool RallyAssembled { get; set; }
	public int RallyBossType { get; set; }
	public uint RallyLeaderSeenAt { get; set; }
	public System.Collections.Generic.List<World.Highway> RallyRoute { get; set; }
	public World.Hellevator RallyShaft { get; set; }
	public System.Collections.Generic.HashSet<int> RallyCheckedIn { get; } = new();
	public uint RallyRouteUntil { get; set; }
	public bool RallyBuilding => RallyRoute != null && Terraria.Main.GameUpdateCount < RallyRouteUntil;
	public uint PoolUntil { get; set; }
	public Vector2? ArenaPoint { get; set; }
	public Point? ArenaBed { get; set; }

	public List<Point> Outposts { get; } = new();
	readonly Dictionary<Point, uint> outpostUsedAt = new();

	public static int MaxOutposts => (int)(3 * Planning.ProgressionPlanner.WorldScale);

	public void TouchOutpost(Point bed) => outpostUsedAt[bed] = Terraria.Main.GameUpdateCount;

	public void AddOutpost(Point bed)
	{
		TouchOutpost(bed);
		if (!Outposts.Contains(bed))
			Outposts.Add(bed);
		while (Outposts.Count > MaxOutposts)
		{
			var stale = Outposts.MinBy(outpost => outpostUsedAt.GetValueOrDefault(outpost));
			Outposts.Remove(stale);
			outpostUsedAt.Remove(stale);
			foreach (var member in Systems.BotWorldSystem.Bots)
				if (member.FactionId == Id && member.OutpostBed == stale)
					member.OutpostBed = null;
			Bots.BotDiagnostics.LogWorld($"[{Name}] dropped the least recently used outpost at {stale} ({MaxOutposts} at most)");
		}
	}

	public Dictionary<Point, Point> OutpostFrames { get; } = new();
	public Dictionary<int, (uint until, bool forGear)> RefightWishes { get; } = new();

	const uint RefightWishTicks = 20 * 3600;

	public void WishRefight(int bossType, bool forGear) =>
		RefightWishes[bossType] = (Terraria.Main.GameUpdateCount + RefightWishTicks, forGear || WantsRefight(bossType, gearOnly: true));

	public bool WantsRefight(int bossType, bool gearOnly = false) =>
		RefightWishes.TryGetValue(bossType, out var wish) && Terraria.Main.GameUpdateCount < wish.until && (wish.forGear || !gearOnly);
	public World.Hellbridge Hellbridge { get; set; }
	public World.Hellevator Hellevator { get; set; }
	public Settlement Settlement => HouseDesign == null ? null : settlement ??= new Settlement(BaseTile, HouseDesign, MemberCount);
	public HouseBlueprint Blueprint => Settlement?.Hall;

	public void ForgetSettlement() => settlement = null;
	int MemberCount => System.Math.Max(1, Systems.BotWorldSystem.Bots.Count(bot => bot.FactionId == Id));

	public int PvpTeam => PvpTeams[Id % PvpTeams.Length];

	public Color Color => Terraria.Main.teamColor[PvpTeam];
	public string Name => Language.GetTextValue($"Mods.AutoTerraMod.Factions.Name{Id}");
	public Vector2 BaseFeet => new(BaseTile.X * 16 + 8, (BaseTile.Y + 1) * 16);
	public Vector2 BaseCenter => BaseFeet - new Vector2(0, 21);

	public TagCompound Save()
	{
		var tag = new TagCompound
		{
			["id"] = Id,
			["baseX"] = BaseTile.X,
			["baseY"] = BaseTile.Y
		};
		if (HouseDesign != null)
			tag["house"] = HouseDesign.Save();
		tag["scaffolding"] = Scaffolding.Select(tile => new Vector2(tile.X, tile.Y)).ToList();
		tag["outposts"] = Outposts.Select(bed => new Vector2(bed.X, bed.Y)).ToList();
		if (ArenaBed is Point arena)
			tag["arenaBed"] = new Vector2(arena.X, arena.Y);
		tag["outpostFrames"] = OutpostFrames.SelectMany(entry => new[] { entry.Key.X, entry.Key.Y, entry.Value.X, entry.Value.Y }).ToList();
		tag["bosses"] = DefeatedBosses.ToList();
		tag["calibration"] = BossDpsCalibration.SelectMany(entry => new[] { (float)entry.Key, entry.Value }).ToList();
		tag["plateaus"] = GearPlateauBosses.ToList();
		uint now = Terraria.Main.GameUpdateCount;
		tag["plateauBosses"] = PlateauProgress.Keys.ToList();
		tag["plateauBest"] = PlateauProgress.Values.Select(entry => entry.best).ToList();
		tag["plateauWaited"] = PlateauProgress.Values.Select(entry => (int)System.Math.Min(now - entry.improvedAt, int.MaxValue)).ToList();
		if (Rally is { } rally && rally.until > now)
		{
			tag["rallyPoint"] = rally.point;
			tag["rallyLeft"] = (int)(rally.until - now);
			tag["rallyLeader"] = RallyLeaderId;
			tag["rallyAssembled"] = RallyAssembled;
			tag["rallyBoss"] = RallyBossType;
			tag["rallyCheckedIn"] = RallyCheckedIn.ToList();
		}
		if (RallyCooldownUntil > now)
			tag["rallyCooldownLeft"] = (int)(RallyCooldownUntil - now);
		tag["knowledge"] = Knowledge.Save();
		if (Hellevator != null)
		{
			tag["shaftX"] = Hellevator.X;
			tag["shaftY"] = Hellevator.TopY;
		}
		if (Hellbridge != null)
		{
			tag["bridgeX"] = Hellbridge.CenterX;
			tag["bridgeY"] = Hellbridge.Y;
		}
		return tag;
	}

	public static Faction Load(TagCompound tag)
	{
		var faction = new Faction
		{
			Id = tag.GetInt("id"),
			BaseTile = new Point(tag.GetInt("baseX"), tag.GetInt("baseY")),
			HouseDesign = tag.ContainsKey("house") ? HouseDesign.Load(tag.GetCompound("house")) : null
		};
		foreach (var tile in tag.GetList<Vector2>("scaffolding"))
			faction.Scaffolding.Add(new Point((int)tile.X, (int)tile.Y));
		if (tag.ContainsKey("arenaBed"))
		{
			var arena = tag.Get<Vector2>("arenaBed");
			faction.ArenaBed = new Point((int)arena.X, (int)arena.Y);
		}
		foreach (var bed in tag.GetList<Vector2>("outposts"))
			faction.Outposts.Add(new Point((int)bed.X, (int)bed.Y));
		var frames = tag.GetList<int>("outpostFrames");
		for (int i = 0; i + 3 < frames.Count; i += 4)
			faction.OutpostFrames[new Point(frames[i], frames[i + 1])] = new Point(frames[i + 2], frames[i + 3]);
		foreach (int boss in tag.GetList<int>("bosses"))
			faction.DefeatedBosses.Add(boss);
		var calibration = tag.GetList<float>("calibration");
		for (int i = 0; i + 1 < calibration.Count; i += 2)
			faction.BossDpsCalibration[(int)calibration[i]] = calibration[i + 1];
		foreach (int boss in tag.GetList<int>("plateaus"))
			faction.GearPlateauBosses.Add(boss);
		uint now = Terraria.Main.GameUpdateCount;
		var plateauBosses = tag.GetList<int>("plateauBosses");
		var plateauBest = tag.GetList<float>("plateauBest");
		var plateauWaited = tag.GetList<int>("plateauWaited");
		for (int i = 0; i < plateauBosses.Count && i < plateauBest.Count && i < plateauWaited.Count; i++)
			faction.PlateauProgress[plateauBosses[i]] = (plateauBest[i], unchecked(now - (uint)plateauWaited[i]));
		if (tag.ContainsKey("rallyPoint"))
		{
			faction.Rally = (tag.Get<Vector2>("rallyPoint"), now + (uint)tag.GetInt("rallyLeft"));
			faction.RallyLeaderId = tag.GetInt("rallyLeader");
			faction.RallyAssembled = tag.GetBool("rallyAssembled");
			faction.RallyBossType = tag.GetInt("rallyBoss");
			faction.RallyLeaderSeenAt = now;
			foreach (int bot in tag.GetList<int>("rallyCheckedIn"))
				faction.RallyCheckedIn.Add(bot);
		}
		if (tag.ContainsKey("rallyCooldownLeft"))
			faction.RallyCooldownUntil = now + (uint)tag.GetInt("rallyCooldownLeft");
		if (tag.ContainsKey("knowledge"))
			faction.Knowledge.Load(tag.GetCompound("knowledge"));
		if (tag.ContainsKey("shaftX"))
			faction.Hellevator = new World.Hellevator(tag.GetInt("shaftX"), tag.GetInt("shaftY"));
		if (tag.ContainsKey("bridgeX"))
			faction.Hellbridge = new World.Hellbridge(tag.GetInt("bridgeX"), tag.GetInt("bridgeY"));
		if (faction.Hellevator != null && faction.Hellbridge != null)
			faction.Hellevator.LandingRow = faction.Hellbridge.Y;
		return faction;
	}
}
