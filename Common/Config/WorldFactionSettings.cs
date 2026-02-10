using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Config;

public sealed record WorldFactionSettings(int FactionCount, int BotsPerFaction, FactionRelation Relation)
{
	public const int MaxFactions = 4;
	public const int MaxBotsPerFaction = 6;

	public static WorldFactionSettings FromConfig(AutoTerraConfig config) => new(config.FactionCount, config.BotsPerFaction, config.Relation);

	public TagCompound Save() => new()
	{
		["factions"] = FactionCount,
		["bots"] = BotsPerFaction,
		["relation"] = (int)Relation
	};

	public static WorldFactionSettings Load(TagCompound tag) =>
		new(tag.GetInt("factions"), tag.GetInt("bots"), (FactionRelation)tag.GetInt("relation"));
}
