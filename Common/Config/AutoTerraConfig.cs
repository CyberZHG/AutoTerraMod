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

	[Range(1, 4)]
	[DefaultValue(2)]
	[Slider]
	public int FactionCount;

	[Range(1, 6)]
	[DefaultValue(3)]
	[Slider]
	public int BotsPerFaction;

	[DefaultValue(FactionRelation.Race)]
	public FactionRelation Relation;

	[DefaultValue(true)]
	public bool SpectatorMode;

	[DefaultValue(true)]
	public bool DiagnosticLogging;
}
