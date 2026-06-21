using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Config;

public sealed record WorldFactionSettings(int FactionCount, int BotsPerFaction, FactionRelation Relation, bool BotsEnabled = true, int MaterialMultiplier = 1)
{
	public const int MaxFactions = 6;
	public const int MaxBotsPerFaction = 20;

	public static readonly int[] MaterialMultipliers = { 1, 5, 10, 20, 50 };

	public static WorldFactionSettings ForNewWorld(AutoTerraConfig config) =>
		new(config.FactionCount, config.BotsPerFaction, config.Relation, Systems.HeadlessTestMode.Enabled);

	public TagCompound Save() => new()
	{
		["factions"] = FactionCount,
		["bots"] = BotsPerFaction,
		["relation"] = (int)Relation,
		["enabled"] = BotsEnabled,
		["materials"] = MaterialMultiplier
	};

	public static WorldFactionSettings Load(TagCompound tag) =>
		new(tag.GetInt("factions"), tag.GetInt("bots"), (FactionRelation)tag.GetInt("relation"),
			!tag.ContainsKey("enabled") || tag.GetBool("enabled"), tag.ContainsKey("materials") ? tag.GetInt("materials") : 1);
}
