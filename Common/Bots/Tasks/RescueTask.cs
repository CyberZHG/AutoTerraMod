using System.Collections.Generic;
using System.Linq;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AutoTerraMod.Common.Bots.Tasks;

public sealed class RescueTask : BotTask
{
	const float SightRadius = 16f * 60f;
	const float TalkRadius = 16f * 4f;
	const float RescueRadius = 16f * 400f;

	public static readonly Dictionary<int, int> RescuedForms = new()
	{
		[NPCID.BoundGoblin] = NPCID.GoblinTinkerer,
		[NPCID.BoundWizard] = NPCID.Wizard,
		[NPCID.BoundMechanic] = NPCID.Mechanic,
		[NPCID.WebbedStylist] = NPCID.Stylist,
		[NPCID.SleepingAngler] = NPCID.Angler,
		[NPCID.BartenderUnconscious] = NPCID.DD2Bartender,
		[NPCID.GolferRescue] = NPCID.Golfer
	};

	static readonly Dictionary<int, HashSet<int>> sightings = new();

	NPC captive;

	public RescueTask(Bot bot) : base(bot) { }

	public override string NameKey => "Rescue";

	public static void NoticeCaptives(Bot bot)
	{
		var seen = sightings.TryGetValue(bot.FactionId, out var known) ? known : sightings[bot.FactionId] = new HashSet<int>();
		foreach (var npc in Main.npc)
		{
			if (npc.active && RescuedForms.ContainsKey(npc.type) && Vector2.Distance(npc.Center, bot.Center) < SightRadius)
				seen.Add(npc.whoAmI);
		}
	}

	public override float Evaluate()
	{
		NoticeCaptives(Bot);
		captive = sightings.TryGetValue(Bot.FactionId, out var seen)
			? seen.Select(index => Main.npc[index]).Where(npc => npc.active && RescuedForms.ContainsKey(npc.type) && Vector2.Distance(npc.Center, Bot.Center) < RescueRadius)
				.OrderBy(npc => Vector2.Distance(npc.Center, Bot.Center)).FirstOrDefault()
			: null;
		if (captive == null)
			return 0f;
		Detail = captive.GivenOrTypeName;
		return 55f + Traits.Cooperation * 20f;
	}

	public override void Tick()
	{
		if (captive == null || !captive.active || !RescuedForms.TryGetValue(captive.type, out int freed))
		{
			IsFinished = true;
			return;
		}
		if (Vector2.Distance(Bot.Center, captive.Center) > TalkRadius)
		{
			if (!TravelOrTunnel(captive.Center, TalkRadius * 0.5f))
				IsFinished = true;
			return;
		}
		Bot.Motor.Stop();
		captive.AI_000_TransformBoundNPC(Avatar.whoAmI, freed);
		Avatar.SetTalkNPC(-1);
		BotDiagnostics.Log(Bot, $"rescued {captive.GivenOrTypeName}");
		Bot.Watchdog.RecordProgress();
		IsFinished = true;
	}
}
