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
	public Point BaseTile { get; init; }
	public HouseDesign HouseDesign { get; set; }
	public FactionKnowledge Knowledge { get; } = new();
	public HashSet<Point> Scaffolding { get; } = new();
	public HashSet<int> DefeatedBosses { get; } = new();
	public Dictionary<int, float> BossDpsCalibration { get; } = new();
	public HashSet<int> GearPlateauBosses { get; } = new();
	public (Vector2 point, uint until)? Rally { get; set; }
	public uint RallyCooldownUntil { get; set; }
	public int RallyLeaderId { get; set; } = -1;
	public bool RallyAssembled { get; set; }
	public int RallyBossType { get; set; }
	public uint RallyLeaderSeenAt { get; set; }
	public System.Collections.Generic.List<World.Highway> RallyRoute { get; set; }
	public World.Hellevator RallyShaft { get; set; }
	public uint RallyRouteUntil { get; set; }
	public bool RallyBuilding => RallyRoute != null && Terraria.Main.GameUpdateCount < RallyRouteUntil;
	public uint PoolUntil { get; set; }
	public Vector2? ArenaPoint { get; set; }
	public Point? ArenaBed { get; set; }

	public List<Point> Outposts { get; } = new();

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
