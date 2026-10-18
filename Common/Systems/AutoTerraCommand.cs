using System;
using AutoTerraMod.Common.Config;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Systems;

public sealed class AutoTerraCommand : ModCommand
{
	public override CommandType Type => CommandType.World;
	public override string Command => "autoterra";
	public override string Usage => "/autoterra [enabled <on|off> | factions <1-4> | bots <1-6> | relation <race|cooperate|war> | logging <on|off>]";
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
		if (args.Length == 2 && args[0].ToLowerInvariant() == "logging" && args[1].ToLowerInvariant() is "on" or "off")
		{
			Bots.BotDiagnostics.LoggingRequested = args[1].ToLowerInvariant() == "on";
			caller.Reply(Text("Logging", Text(Bots.BotDiagnostics.LoggingRequested ? "On" : "Off")));
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
			case "factions" when int.TryParse(value, out int factions) && factions >= 1 && factions <= WorldFactionSettings.MaxFactions:
				return current with { FactionCount = factions };
			case "bots" when int.TryParse(value, out int bots) && bots >= 1 && bots <= WorldFactionSettings.MaxBotsPerFaction:
				return current with { BotsPerFaction = bots };
			case "relation" when Enum.TryParse<FactionRelation>(value, true, out var relation) && Enum.IsDefined(relation):
				return current with { Relation = relation };
			case "enabled" when value.ToLowerInvariant() is "on" or "off":
				return current with { BotsEnabled = value.ToLowerInvariant() == "on" };
			case "materials" when int.TryParse(value, out int factor) && Array.IndexOf(WorldFactionSettings.MaterialMultipliers, factor) >= 0:
				return current with { MaterialMultiplier = factor };
			default:
				return null;
		}
	}

	static string Describe(WorldFactionSettings settings) =>
		Text("Current", settings.FactionCount, settings.BotsPerFaction, Language.GetTextValue($"Mods.AutoTerraMod.Configs.FactionRelation.{settings.Relation}.Label"),
			Text(settings.BotsEnabled ? "On" : "Off"));
}
