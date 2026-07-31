using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace AutoTerraMod.Common.Config;

public enum FactionRelation
{
	Race,
	Cooperate,
	War
}

public sealed class AutoTerraConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ServerSide;

	[DefaultValue(false)]
	public bool BotsEnabled;

	[Range(1, WorldFactionSettings.MaxFactions)]
	[DefaultValue(2)]
	[Slider]
	public int FactionCount;

	[Range(1, WorldFactionSettings.MaxBotsPerFaction)]
	[DefaultValue(3)]
	[Slider]
	public int BotsPerFaction;

	[DefaultValue(FactionRelation.Race)]
	public FactionRelation Relation;

	[DefaultValue(true)]
	public bool SpectatorMode;

	[DefaultValue(true)]
	public bool ShowInfoPanel;

	[DefaultValue(false)]
	public bool DiagnosticLogging;

	[DefaultValue(false)]
	public bool WorkbenchBossSummons;

	public override void OnChanged()
	{
		var current = Systems.BotWorldSystem.Settings;
		if (current == null)
			return;
		var wanted = current with { FactionCount = FactionCount, BotsPerFaction = BotsPerFaction, Relation = Relation, BotsEnabled = BotsEnabled };
		if (wanted != current)
			Systems.BotWorldSystem.ChangeSettings(wanted);
	}
}
