using System.Linq;
using AutoTerraMod.Common.Bosses;
using AutoTerraMod.Common.Planning;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Common.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class FishingTask : BotTask
{
	const int NearbyWaterRadius = 60;
	const float CastRadius = 16f * 12f;
	const float TalkRadius = 16f * 4f;
	const int BiteThreshold = 660;
	const int LineBreakOdds = 7;
	const double MorningTicks = 3 * 3600;

	Projectile bobber;
	Vector2? water;
	NPC angler;

	public FishingTask(Bot bot) : base(bot) { }

	public override string NameKey => "Fish";

	Item Pole => Inventory.Items(Avatar).Where(item => item.fishingPole > 0).MaxBy(item => item.fishingPole);

	Item Bait => Inventory.Items(Avatar).Where(item => item.bait > 0 && item.type != ItemID.TruffleWorm).MaxBy(item => item.bait);

	static int QuestFish => Main.anglerQuestItemNetIDs[Main.anglerQuest];

	bool QuestDone => Main.anglerWhoFinishedToday.Contains(Avatar.name);

	public override float Evaluate()
	{
		angler = null;
		if (Traits.Aspiration != Aspiration.Angler)
			return 0f;
		int anglerIndex = NPC.FindFirstNPC(NPCID.Angler);
		if (anglerIndex >= 0 && !QuestDone && Inventory.Count(Avatar, QuestFish) > 0)
		{
			angler = Main.npc[anglerIndex];
			Detail = Lang.GetItemNameValue(QuestFish);
			return Aspire(85f);
		}
		if (Pole == null || Bait == null || !(Main.dayTime && Main.time < MorningTicks) && Bot.Brain.Plan is not IdleStep)
			return 0f;
		Detail = "";
		return Aspire(Main.dayTime && Main.time < MorningTicks ? 70f : 30f);
	}

	public override void Start()
	{
		base.Start();
		bobber = null;
		water = FindWater();
	}

	const int FullPowerPond = 300;

	Vector2? FindWater() =>
		WaterFinder.NearestPond(Bot.CenterTile, NearbyWaterRadius, FullPowerPond) is Point pond ? pond.ToWorldCoordinates() : BossRituals.Shoreline(Bot);

	public override void Tick()
	{
		if (angler != null)
		{
			TurnInQuest();
			return;
		}
		if (water is not Vector2 spot || Pole == null || Bait == null)
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, spot) > CastRadius)
		{
			if (!TravelOrTunnel(spot, CastRadius * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		Bot.Motor.Face(spot);
		Bot.Watchdog.RecordProgress();
		if (bobber == null)
		{
			bobber = new Projectile();
			bobber.SetDefaults(Pole.shoot);
			bobber.owner = Avatar.whoAmI;
			bobber.Center = new Vector2(spot.X, System.Math.Max(spot.Y + 8f, Bot.Center.Y + 8f));
			bobber.wet = true;
		}
		AwaitBite();
	}

	void AwaitBite()
	{
		int level = Avatar.GetFishingConditions().FinalFishingLevel;
		if (Main.rand.Next(300) < level)
			bobber.localAI[1] += Main.rand.Next(1, 3);
		bobber.localAI[1] += level / 30 + Main.rand.Next(1, 3);
		if (Main.rand.Next(60) == 0)
			bobber.localAI[1] += 60f;
		if (bobber.localAI[1] <= BiteThreshold)
			return;
		bobber.localAI[1] = 0f;
		using (AvatarPresence.Enter())
			bobber.FishingCheck();
		if (bobber.ai[1] >= 0f || bobber.localAI[1] == 0f)
			return;
		Reel();
	}

	void Reel()
	{
		int caught = (int)bobber.localAI[1];
		var bait = Bait;
		if (bait != null && Main.rand.Next(100) >= bait.bait)
			Inventory.ConsumeOne(Avatar, bait);
		if (caught < 0)
		{
			NPC.NewNPC(new Terraria.DataStructures.EntitySource_FishedOut(Avatar), (int)bobber.Center.X, (int)bobber.Center.Y, -caught);
			BotDiagnostics.Log(Bot, $"fished out {Lang.GetNPCNameValue(-caught)}");
		}
		else if (Main.rand.Next(LineBreakOdds) == 0 && !Avatar.accFishingLine)
		{
			BotDiagnostics.Log(Bot, "the fishing line snapped");
		}
		else
		{
			var item = new Item();
			item.SetDefaults(caught);
			Inventory.Add(Avatar, item);
			BotDiagnostics.Log(Bot, $"caught {item.Name}");
		}
		bobber = null;
	}

	void TurnInQuest()
	{
		if (!angler.active || QuestDone || Inventory.Count(Avatar, QuestFish) == 0)
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, angler.Center) > TalkRadius)
		{
			if (!TravelOrTunnel(angler.Center, TalkRadius * 0.5f))
				IsFinished = true;
			return;
		}
		int fish = QuestFish;
		Inventory.Consume(Avatar, new[] { fish }, 1);
		Main.anglerWhoFinishedToday.Add(Avatar.name);
		Avatar.anglerQuestsFinished++;
		using (AvatarPresence.Enter())
			Avatar.GetAnglerReward(angler, fish);
		BotDiagnostics.Log(Bot, $"turned in {Lang.GetItemNameValue(fish)} to the Angler (quests finished: {Avatar.anglerQuestsFinished})");
		IsFinished = true;
	}
}
