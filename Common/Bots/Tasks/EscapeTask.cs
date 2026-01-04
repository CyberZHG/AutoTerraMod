using System.Linq;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class EscapeTask : BotTask
{
	const int MaxEscapeTicks = 3600;
	const int SurfaceMarginTiles = 5;
	const int SurfaceStepTiles = 8;
	const int OpenGroundTrip = 30;

	static readonly int[] RecallItems = { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone, ItemID.RecallPotion };

	Vector2 destination;
	bool headingForSurface;
	uint startedAt;

	public EscapeTask(Bot bot) : base(bot) { }

	public override string NameKey => "Escape";
	public override bool IsUrgent => true;

	public override float Evaluate() => Bot.Watchdog.EscapeRequested ? 100f : 0f;

	public override void Start()
	{
		base.Start();
		startedAt = Main.GameUpdateCount;
		if (TryRecall())
		{
			Finish();
			return;
		}
		var here = Bot.CenterTile;
		int x = Utils.Clamp(here.X + Bot.Watchdog.EscapeDirection * SurfaceStepTiles, 50, Main.maxTilesX - 50);
		int surfaceY = TileQuery.SurfaceY(x);
		headingForSurface = here.Y > surfaceY + SurfaceMarginTiles;
		destination = headingForSurface
			? new Vector2(x * 16f, (surfaceY - 2) * 16f)
			: Vector2.Distance(Bot.Center, Bot.Faction.BaseCenter) > 16f * 40f
				? Bot.Faction.BaseCenter
				: Bot.Center + new Vector2(Bot.Watchdog.EscapeDirection * OpenGroundTrip * 16f, 0f);
	}

	public override void Tick()
	{
		Bot.Motor.ScaffoldingAllowed = true;
		Bot.Motor.MoveTo(destination, 32f);
		var here = Bot.CenterTile;
		bool reachedSurface = headingForSurface && here.Y <= TileQuery.SurfaceY(here.X) + SurfaceMarginTiles;
		if (reachedSurface || Bot.Motor.HasArrived || Main.GameUpdateCount - startedAt > MaxEscapeTicks)
			Finish();
	}

	void Finish()
	{
		IsFinished = true;
		Bot.Watchdog.EscapeFinished();
	}

	bool TryRecall()
	{
		var recall = Inventory.Items(Avatar).FirstOrDefault(item => RecallItems.Contains(item.type));
		var body = Bot.Npc;
		if (recall == null || body == null)
			return false;
		if (recall.consumable)
			Inventory.ConsumeOne(Avatar, recall);
		SoundEngine.PlaySound(SoundID.Item6, body.Center);
		var home = BotWorldSystem.RespawnFeet(Bot);
		body.position = home - new Vector2(body.width / 2f, body.height);
		body.velocity = Vector2.Zero;
		BotDiagnostics.Log(Bot, $"used {recall.Name} to return home");
		return true;
	}
}
