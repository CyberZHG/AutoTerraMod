using System.Collections.Generic;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class TreasureTask : BotTask
{
	const int SearchRadius = 250;

	readonly HashSet<Point> unreachable = new();
	Point? target;

	public TreasureTask(Bot bot) : base(bot) { }

	public override string NameKey => "Treasure";

	public override float Evaluate()
	{
		var wanted = WantedTreasure();
		if (wanted.Count == 0)
			return 0f;
		target = BotWorldSystem.KnowledgeOf(Bot.Faction).NearestKnown(Bot.CenterTile, wanted, unreachable, SearchRadius);
		if (target is not Point tile)
			return 0f;
		Detail = Lang.GetMapObjectName(Main.tile[tile.X, tile.Y].TileType) ?? "";
		return 70f;
	}

	HashSet<int> WantedTreasure()
	{
		var wanted = new HashSet<int>();
		if (StatBoosters.WantsLifeCrystals(Avatar))
			wanted.Add(TileID.Heart);
		if (StatBoosters.WantsLifeFruit(Avatar))
			wanted.Add(TileID.LifeFruit);
		return wanted;
	}

	public override void Tick()
	{
		if (target is not Point tile || !WorldScan.IsTreasure(TileQuery.TypeAt(tile)))
		{
			IsFinished = true;
			return;
		}
		var pickaxe = Bot.Hands.BestPickaxe;
		if (pickaxe != null && Bot.Hands.CanReach(pickaxe, tile))
		{
			Bot.Motor.Stop();
			if (Bot.Hands.Dig(tile) is ToolResult.Unbreakable or ToolResult.NoTool)
				GiveUp(tile);
			return;
		}
		Bot.Motor.MoveTo(tile.ToWorldCoordinates(), 24f);
		if (Bot.Motor.IsStuck)
			GiveUp(tile);
	}

	void GiveUp(Point tile)
	{
		unreachable.Add(tile);
		IsFinished = true;
	}
}
