using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
using Terraria.Utilities;

namespace AutoTerraMod.Common.Bots;

public enum Archetype
{
	Miner,
	Explorer,
	Builder,
	Warrior
}

public enum CombatStyle
{
	Melee,
	Ranged,
	Magic,
	Summon
}

public enum Aspiration
{
	MonsterHunter,
	Collector,
	Naturalist,
	Angler
}

public enum BuildingTaste
{
	Rustic,
	StoneBrick,
	RedBrick
}

public sealed class Personality
{
	public CombatStyle Style { get; init; }
	public BuildingTaste Taste { get; init; }
	public Aspiration Aspiration { get; set; }
	public float Mining { get; init; }
	public float Exploring { get; init; }
	public float Building { get; init; }
	public float Combat { get; init; }
	public float Courage { get; init; }
	public float Cooperation { get; init; }

	public Archetype Archetype
	{
		get
		{
			var archetype = Archetype.Miner;
			float strongest = Mining;
			if (Exploring > strongest) (archetype, strongest) = (Archetype.Explorer, Exploring);
			if (Building > strongest) (archetype, strongest) = (Archetype.Builder, Building);
			if (Combat > strongest) archetype = Archetype.Warrior;
			return archetype;
		}
	}

	public float FleeHealthFraction => MathHelper.Lerp(0.6f, 0.15f, Courage);

	public static Personality Roll(UnifiedRandom rand)
	{
		var favorite = (Archetype)rand.Next(4);
		float Trait(Archetype archetype) => archetype == favorite ? rand.NextFloat(0.7f, 1f) : rand.NextFloat(0.1f, 0.6f);
		return new Personality
		{
			Mining = Trait(Archetype.Miner),
			Exploring = Trait(Archetype.Explorer),
			Building = Trait(Archetype.Builder),
			Combat = Trait(Archetype.Warrior),
			Courage = rand.NextFloat(),
			Cooperation = rand.NextFloat(),
			Style = (CombatStyle)rand.Next(4),
			Aspiration = (Aspiration)rand.Next(System.Enum.GetValues<Aspiration>().Length),
			Taste = (BuildingTaste)rand.Next(3)
		};
	}

	public TagCompound Save() => new()
	{
		["mining"] = Mining,
		["exploring"] = Exploring,
		["building"] = Building,
		["combat"] = Combat,
		["courage"] = Courage,
		["cooperation"] = Cooperation,
		["style"] = (int)Style,
		["aspiration"] = (int)Aspiration,
		["taste"] = (int)Taste
	};

	public static Personality Load(TagCompound tag) => new()
	{
		Mining = tag.GetFloat("mining"),
		Exploring = tag.GetFloat("exploring"),
		Building = tag.GetFloat("building"),
		Combat = tag.GetFloat("combat"),
		Courage = tag.GetFloat("courage"),
		Cooperation = tag.GetFloat("cooperation"),
		Style = (CombatStyle)tag.GetInt("style"),
		Aspiration = (Aspiration)tag.GetInt("aspiration"),
		Taste = (BuildingTaste)tag.GetInt("taste")
	};
}
