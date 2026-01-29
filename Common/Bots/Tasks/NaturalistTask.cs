using System.Linq;
using AutoTerraMod.Common.Planning;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class NaturalistTask : BotTask
{
	const float SightRadius = 16f * 40f;
	const float NetReach = 16f * 3f;

	const int MinRamble = 60;
	const int MaxRamble = 100;

	NPC critter;
	Vector2? ramble;

	public NaturalistTask(Bot bot) : base(bot) { }

	public override string NameKey => "Catch";

	public static bool StudiesCritters(Bot bot) => bot.Personality.Aspiration == Aspiration.Naturalist;

	public static bool IsCage(Item item) => item.createTile >= 0 && TileID.Sets.CritterCageLidStyle[item.createTile] != -1;

	public override float Evaluate()
	{
		critter = null;
		if (!StudiesCritters(Bot) || Inventory.Count(Avatar, ItemID.BugNet) == 0 && Inventory.Count(Avatar, ItemID.GoldenBugNet) == 0)
			return 0f;
		critter = Main.npc.Where(npc => npc.active && NPCID.Sets.CountsAsCritter[npc.type] && npc.catchItem > 0 && !npc.SpawnedFromStatue
				&& Inventory.Count(Avatar, npc.catchItem) == 0 && !AlreadyCaged(npc.catchItem) && CageGoal.CagesFor(npc.catchItem).Count > 0
				&& Vector2.Distance(npc.Center, Bot.Center) < SightRadius)
			.OrderBy(npc => Vector2.Distance(npc.Center, Bot.Center)).FirstOrDefault();
		if (critter == null)
		{
			if (!Main.dayTime || !DecorateTask.Leisure(Bot))
				return 0f;
			int x = Bot.Faction.BaseTile.X + (Main.rand.NextBool() ? 1 : -1) * Main.rand.Next(MinRamble, MaxRamble);
			ramble = new Vector2(x * 16f, (World.TileQuery.SurfaceY(x) - 2) * 16f);
			Detail = "";
			return 30f;
		}
		ramble = null;
		Detail = critter.GivenOrTypeName;
		return 55f;
	}

	bool AlreadyCaged(int critterItem) =>
		CageGoal.CagesFor(critterItem).Any(cage => Inventory.Count(Avatar, cage) > 0 || Stations.BaseHas(Bot.Faction, ContentSamples.ItemsByType[cage].createTile));

	public override void Tick()
	{
		if (ramble is Vector2 meadow)
		{
			Bot.Motor.MoveTo(meadow, 32f);
			if (Bot.Motor.HasArrived || Bot.Motor.IsStuck)
				IsFinished = true;
			return;
		}
		if (critter == null || !critter.active)
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, critter.Center) > NetReach)
		{
			Bot.Motor.MoveTo(critter.Center, NetReach * 0.5f);
			if (Bot.Motor.IsStuck)
				IsFinished = true;
			return;
		}
		var caught = new Item();
		caught.SetDefaults(critter.catchItem);
		critter.active = false;
		Inventory.Add(Avatar, caught);
		BotDiagnostics.Log(Bot, $"caught a {Lang.GetItemNameValue(caught.type)} with the bug net");
		Bot.Watchdog.RecordProgress();
		IsFinished = true;
	}
}
