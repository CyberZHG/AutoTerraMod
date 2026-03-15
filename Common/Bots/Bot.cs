using System;
using System.Linq;
using AutoTerraMod.Common.Factions;
using AutoTerraMod.Common.Systems;
using AutoTerraMod.Content.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader.IO;
using Terraria.Utilities;

namespace AutoTerraMod.Common.Bots;

public sealed class Bot
{
	static readonly string[] NamePool =
	{
		"Alder", "Briar", "Cobble", "Dusk", "Ember", "Flint", "Garnet", "Hollow", "Ivy", "Jasper",
		"Kestrel", "Loam", "Moss", "Nettle", "Onyx", "Pike", "Quill", "Rowan", "Slate", "Thorn",
		"Umber", "Vale", "Wren", "Yarrow", "Zinc", "Ash", "Birch", "Clay", "Drift", "Fern"
	};

	public Bot(int id, int factionId, string name, Personality personality, Player avatar)
	{
		Id = id;
		FactionId = factionId;
		Name = name;
		Personality = personality;
		Avatar = avatar;
		Motor = new BotMotor(this);
		Grapple = new BotGrapple(this);
		Hands = new BotHands(this);
		Vitals = new BotVitals(this);
		Brain = new BotBrain(this);
		Watchdog = new BotWatchdog(this);
	}

	public int Id { get; }
	public int FactionId { get; }
	public string Name { get; }
	public Personality Personality { get; }
	public Player Avatar { get; }
	public BotMotor Motor { get; }
	public BotGrapple Grapple { get; }
	public BotHands Hands { get; }
	public BotVitals Vitals { get; }
	public BotBrain Brain { get; }
	public BotWatchdog Watchdog { get; }

	const int IgnoreUnreachableTicks = 1200;

	readonly System.Collections.Generic.Dictionary<int, uint> ignoredTargets = new();

	public void IgnoreTarget(NPC target)
	{
		ignoredTargets[target.whoAmI] = Main.GameUpdateCount + IgnoreUnreachableTicks;
		BotDiagnostics.Log(this, $"ignoring unreachable {target.GivenOrTypeName}");
	}

	public bool IsIgnoring(NPC target) =>
		ignoredTargets.TryGetValue(target.whoAmI, out uint until) && Main.GameUpdateCount < until;

	public int NpcIndex { get; set; } = -1;
	public int RespawnTicks { get; set; }
	public Vector2? SavedFeetPosition { get; set; }
	public Point? OutpostBed { get; set; }

	const uint MiningCommitmentTicks = 20 * 60 * 60;
	const int UndergroundDepth = 20;
	const uint SessionCheckTicks = 60;
	uint undergroundSince;
	uint sessionCheckedAt;

	const uint ChoresAfterDeathTicks = 2 * 60 * 60;
	uint diedAt;

	public bool UpgradeAfterDeath { get; set; }
	public void FinishChoresAfterDeath() => diedAt = 0;
	public bool StoreAfterDeath => diedAt != 0 && Main.GameUpdateCount - diedAt < ChoresAfterDeathTicks;

	public bool CommittedToMining => undergroundSince != 0 && Main.GameUpdateCount - undergroundSince < MiningCommitmentTicks;

	public void TrackMiningSession()
	{
		if (Main.GameUpdateCount - sessionCheckedAt < SessionCheckTicks)
			return;
		sessionCheckedAt = Main.GameUpdateCount;
		var here = CenterTile;
		bool underground = here.Y > World.TileQuery.SurfaceY(here.X) + UndergroundDepth && Faction.Settlement?.Contains(here.X, here.Y) != true;
		if (!underground)
			undergroundSince = 0;
		else if (undergroundSince == 0)
			undergroundSince = Math.Max(1u, Main.GameUpdateCount);
	}

	public Faction Faction => BotWorldSystem.Factions[FactionId];
	public bool IsAlive => Npc != null;
	public Vector2 Center => Npc?.Center ?? Avatar.Center;
	public Point CenterTile => Center.ToTileCoordinates();

	public NPC Npc
	{
		get
		{
			if (NpcIndex < 0)
				return null;
			var npc = Main.npc[NpcIndex];
			return npc.active && npc.ModNPC is BotNPC body && body.BotId == Id ? npc : null;
		}
	}

	public static Bot Create(int id, Faction faction, UnifiedRandom rand, System.Collections.Generic.ISet<string> takenNames)
	{
		var available = NamePool.Where(candidate => !takenNames.Contains(candidate)).ToArray();
		string name = available.Length > 0 ? available[rand.Next(available.Length)] : $"{NamePool[rand.Next(NamePool.Length)]}{id}";
		return new Bot(id, faction.Id, name, Personality.Roll(rand), AvatarFactory.Create(name, faction.Color, rand));
	}

	string WallDistance() =>
		Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active ? $"{Math.Abs(Main.npc[Main.wofNPCIndex].Center.X - Center.X) / 16f:0}" : "-";

	public void Die()
	{
		var npc = Npc;
		if (npc == null)
			return;
		BotDiagnostics.Log(this, $"died at {CenterTile} from {Vitals.LastDamageSource} while {Brain.Current?.NameKey} ({Brain.GoalName}); obsidianSkin={Avatar.HasBuff(Terraria.ID.BuffID.ObsidianSkin)} fireWalk={Avatar.fireWalk} heals={Inventory.Items(Avatar).Where(item => item.potion && item.healLife > 0).Sum(item => item.stack)} defense={Avatar.statDefense} shots={Hands.ShotsFired} wall={WallDistance()} lifeRegen={Avatar.lifeRegen} buffs=[{string.Join(",", Avatar.buffType.Where((type, slot) => type > 0 && Avatar.buffTime[slot] > 0).Select(type => Lang.GetBuffName(type)))}] wet={npc.wet} lavaWet={npc.lavaWet}");
		Hands.ShotsFired = 0;
		BotVitals.SyncAvatar(npc, Avatar);
		long coins = Utils.CoinsCount(out _, Avatar.inventory);
		Avatar.DropTombstone(coins, NetworkText.FromLiteral(Language.GetTextValue("Mods.AutoTerraMod.UI.Epitaph", Name, Faction.Name)), Vitals.LastHitDirection);
		DropHalfCoins(npc.Center);
		Main.NewText(Language.GetTextValue("Mods.AutoTerraMod.UI.BotDied", Name, Faction.Name), Faction.Color);
		RespawnTicks = BotVitals.RespawnDelay(npc.Center);
		Vitals.OnDeath();
		npc.active = false;
		NpcIndex = -1;
		SavedFeetPosition = null;
		Brain.Reset();
		diedAt = Math.Max(1u, Main.GameUpdateCount);
		UpgradeAfterDeath = true;
		undergroundSince = 0;
		Motor.Stop();
		Grapple.Release(npc, jump: false);
	}

	void DropHalfCoins(Vector2 where)
	{
		for (int slot = 0; slot < Inventory.StorageSlots; slot++)
		{
			var item = Avatar.inventory[slot];
			if (!item.IsACoin || item.stack < 2)
				continue;
			int dropped = item.stack / 2;
			item.stack -= dropped;
			Item.NewItem(new EntitySource_Misc("AutoTerraMod:BotDeath"), where, item.type, dropped);
		}
	}

	public TagCompound Save()
	{
		var tag = new TagCompound
		{
			["id"] = Id,
			["faction"] = FactionId,
			["name"] = Name,
			["personality"] = Personality.Save(),
			["avatar"] = AvatarStorage.Save(Avatar)
		};
		var npc = Npc;
		if (npc != null)
			tag["feet"] = npc.Bottom;
		if (OutpostBed is Point bed)
		{
			tag["outpostX"] = bed.X;
			tag["outpostY"] = bed.Y;
		}
		return tag;
	}

	public static Bot Load(TagCompound tag)
	{
		var bot = new Bot(
			tag.GetInt("id"),
			tag.GetInt("faction"),
			tag.GetString("name"),
			Personality.Load(tag.GetCompound("personality")),
			AvatarStorage.Load(tag.GetCompound("avatar")));
		if (tag.ContainsKey("feet"))
			bot.SavedFeetPosition = tag.Get<Vector2>("feet");
		if (tag.ContainsKey("outpostX"))
			bot.OutpostBed = new Point(tag.GetInt("outpostX"), tag.GetInt("outpostY"));
		return bot;
	}
}
