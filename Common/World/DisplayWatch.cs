using Terraria.ModLoader;

namespace AutoTerraMod.Common.World;

public sealed class DisplayWatch : GlobalTile
{
	public override void PlaceInWorld(int i, int j, int type, Terraria.Item item)
	{
		if (Bots.Tasks.DecorateTask.IsDisplayTile(type))
			Bots.Tasks.DecorateTask.DisplaysChanged();
	}

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (!fail && !effectOnly && Bots.Tasks.DecorateTask.IsDisplayTile(type))
			Bots.Tasks.DecorateTask.DisplaysChanged();
	}
}

public sealed class DisplayReset : ModSystem
{
	public override void OnWorldLoad() => Bots.Tasks.DecorateTask.DisplaysChanged();
}
