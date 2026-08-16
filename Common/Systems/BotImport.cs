using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AutoTerraMod.Common.Systems;

public static class BotImport
{
	public static TagCompound ReadBotData(string worldPath, out string error)
	{
		error = null;
		string path = Path.ChangeExtension(worldPath, ".twld");
		if (!File.Exists(path))
		{
			error = Text("NoFile", Path.GetFileName(path));
			return null;
		}
		TagCompound world;
		try
		{
			world = TagIO.FromStream(new MemoryStream(File.ReadAllBytes(path)));
		}
		catch (System.Exception exception)
		{
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn($"cannot read {path}: {exception}");
			error = Text("Unreadable", Path.GetFileName(path));
			return null;
		}
		var data = world.GetList<TagCompound>("modData")
			.FirstOrDefault(entry => entry.GetString("mod") == nameof(AutoTerraMod) && entry.GetString("name") == nameof(BotWorldSystem))
			?.GetCompound("data");
		if (data == null || !data.ContainsKey("bots") || data.GetList<TagCompound>("bots").Count == 0)
		{
			error = Text("NoBots", Path.GetFileNameWithoutExtension(worldPath));
			return null;
		}
		return data;
	}

	public static string Text(string key, params object[] args) => Language.GetTextValue($"Mods.AutoTerraMod.Import.{key}", args);

	public static void Run(string source, Config.AutoTerraConfig config)
	{
		var data = ReadBotData(source, out string error);
		var notes = data == null ? new List<string> { error } : BotWorldSystem.ImportFrom(data);
		foreach (var note in notes)
		{
			ModContent.GetInstance<AutoTerraMod>().Logger.Info($"bot import from {source}: {note}");
			Terraria.Main.NewText(note, Microsoft.Xna.Framework.Color.Gold);
		}
		if (BotWorldSystem.Settings is not { } settings)
			return;
		config.BotsEnabled = settings.BotsEnabled;
		config.FactionCount = settings.FactionCount;
		config.BotsPerFaction = settings.BotsPerFaction;
		config.Relation = settings.Relation;
	}
}
