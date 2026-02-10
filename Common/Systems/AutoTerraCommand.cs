using System;
using AutoTerraMod.Common.Config;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class AutoTerraCommand : ModCommand
{
	public override CommandType Type => CommandType.World;
	public override string Command => "autoterra";
	public override string Usage => "/autoterra [factions <1-4> | bots <1-6> | relation <race|cooperate|war>]";
	public override string Description => Text("Description");

	static string Text(string key, params object[] args) => Language.GetTextValue($"Mods.AutoTerraMod.Commands.{key}", args);

	public override void Action(CommandCaller caller, string input, string[] args)
	{
		var current = BotWorldSystem.Settings;
		if (current == null)
		{
			caller.Reply(Text("NotStarted"));
			return;
		}
		if (args.Length == 0)
		{
			caller.Reply(Describe(current));
			return;
		}
		if (args.Length != 2 || Change(current, args[0].ToLowerInvariant(), args[1]) is not WorldFactionSettings changed)
		{
			caller.Reply(Text("Usage", Usage));
			return;
		}
		BotWorldSystem.ChangeSettings(changed);
		caller.Reply(Describe(changed));
	}

	static WorldFactionSettings Change(WorldFactionSettings current, string setting, string value)
	{
		switch (setting)
		{
			case "factions" when int.TryParse(value, out int factions) && factions >= current.FactionCount && factions <= WorldFactionSettings.MaxFactions:
				return current with { FactionCount = factions };
			case "bots" when int.TryParse(value, out int bots) && bots >= current.BotsPerFaction && bots <= WorldFactionSettings.MaxBotsPerFaction:
				return current with { BotsPerFaction = bots };
			case "relation" when Enum.TryParse<FactionRelation>(value, true, out var relation) && Enum.IsDefined(relation):
				return current with { Relation = relation };
			default:
				return null;
		}
	}

	static string Describe(WorldFactionSettings settings) =>
		Text("Current", settings.FactionCount, settings.BotsPerFaction, Language.GetTextValue($"Mods.AutoTerraMod.Configs.FactionRelation.{settings.Relation}.Label"));
}
