using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Building;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Factions;

public sealed class Faction
{
	static readonly Color[] Palette =
	{
		new(230, 76, 76),
		new(76, 136, 240),
		new(90, 200, 100),
		new(240, 196, 64)
	};

	Settlement settlement;

	public int Id { get; init; }
	public Point BaseTile { get; init; }
	public HouseDesign HouseDesign { get; set; }
	public FactionKnowledge Knowledge { get; } = new();
	public HashSet<Point> Scaffolding { get; } = new();
	public HashSet<int> DefeatedBosses { get; } = new();
	public (Vector2 point, uint until)? Rally { get; set; }
	public uint RallyCooldownUntil { get; set; }
	public int RallyLeaderId { get; set; } = -1;
	public bool RallyAssembled { get; set; }
	public Dictionary<int, uint> RefightWishes { get; } = new();

	const uint RefightWishTicks = 20 * 3600;

	public void WishRefight(int bossType) => RefightWishes[bossType] = Terraria.Main.GameUpdateCount + RefightWishTicks;

	public bool WantsRefight(int bossType) => RefightWishes.TryGetValue(bossType, out uint until) && Terraria.Main.GameUpdateCount < until;
	public World.Hellbridge Hellbridge { get; set; }
	public World.Hellevator Hellevator { get; set; }
	public Settlement Settlement => HouseDesign == null ? null : settlement ??= new Settlement(BaseTile, HouseDesign, MemberCount);
	public HouseBlueprint Blueprint => Settlement?.Hall;

	public void ForgetSettlement() => settlement = null;
	int MemberCount => System.Math.Max(1, Systems.BotWorldSystem.Bots.Count(bot => bot.FactionId == Id));

	public Color Color => Palette[Id % Palette.Length];
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
		tag["bosses"] = DefeatedBosses.ToList();
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
		foreach (int boss in tag.GetList<int>("bosses"))
			faction.DefeatedBosses.Add(boss);
		if (tag.ContainsKey("knowledge"))
			faction.Knowledge.Load(tag.GetCompound("knowledge"));
		if (tag.ContainsKey("shaftX"))
			faction.Hellevator = new World.Hellevator(tag.GetInt("shaftX"), tag.GetInt("shaftY"));
		if (tag.ContainsKey("bridgeX"))
			faction.Hellbridge = new World.Hellbridge(tag.GetInt("bridgeX"), tag.GetInt("bridgeY"));
		return faction;
	}
}
