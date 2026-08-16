using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader.Config.UI;

namespace AutoTerraMod.Common.Config;

public sealed class WorldPickerElement : ConfigElement<string>
{
	List<string> paths;

	public override void OnBind()
	{
		base.OnBind();
		if (Main.WorldList.Count == 0)
			Main.LoadWorlds();
		string current = Main.ActiveWorldFileData?.Path;
		paths = new List<string> { "" };
		paths.AddRange(Main.WorldList.Where(world => world.Path != current).Select(world => world.Path));
		TextDisplayFunction = () => $"{Label}: {Shown(Value)}";
		OnLeftClick += (_, _) => Step(1);
		OnRightClick += (_, _) => Step(-1);
	}

	static string Shown(string path) => string.IsNullOrEmpty(path)
		? Language.GetTextValue("Mods.AutoTerraMod.Import.None")
		: Main.WorldList.FirstOrDefault(world => world.Path == path)?.Name ?? Path.GetFileNameWithoutExtension(path);

	void Step(int direction)
	{
		int index = paths.IndexOf(Value ?? "");
		Value = paths[((index < 0 ? 0 : index) + direction + paths.Count) % paths.Count];
	}
}
