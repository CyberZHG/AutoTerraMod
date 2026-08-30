using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Net;

public enum BotMessage : byte
{
	Appearance,
	State
}

public sealed class BotNet : ModSystem
{
	const int StateInterval = 2;
	const int AppearanceInterval = 60;
	const int MaxExtrapolateTicks = 10;
	const float MotionSmooth = 0.4f;
	const float MotionSnap = 16f * 12f;
	const uint StaleTicks = 180;
	const int ArmorSlots = 20;

	const byte FlagFacingRight = 1, FlagPulley = 2, FlagFlying = 4, FlagDead = 8;

	sealed class ClientBot
	{
		public Player Avatar;
		public Vector2 AuthPosition, AuthVelocity, Drawn, Aim = Vector2.UnitX, YoyoTarget;
		public uint AuthAt, SeenAt;
		public bool HasState, Flying, Dead;
		public string Name = "", TaskKey = "";
		public int Team, HookType;
		public Vector2? HookAnchor;
	}

	public readonly record struct ClientLabel(string Name, int Team, string TaskKey, Vector2 Top);

	public readonly record struct ClientHook(int Type, Vector2 From, Vector2 Anchor);

	public static IEnumerable<ClientLabel> ClientLabels() => clientBots.Values.Where(Shown).Select(entry => new ClientLabel(entry.Name, entry.Team, entry.TaskKey, entry.Avatar.Top));

	public static IEnumerable<ClientHook> ClientHooks() => clientBots.Values.Where(entry => Shown(entry) && entry.HookType > 0 && entry.HookAnchor != null)
		.Select(entry => new ClientHook(entry.HookType, entry.Avatar.Center, entry.HookAnchor.Value));

	static bool Shown(ClientBot entry) => entry.HasState && !entry.Dead && Main.GameUpdateCount - entry.SeenAt <= StaleTicks;

	static readonly bool[] avatarSlots = new bool[Main.maxPlayers];

	public static bool IsClientAvatar(int slot) => slot >= 0 && slot < avatarSlots.Length && avatarSlots[slot] && Main.netMode == NetmodeID.MultiplayerClient;

	static readonly Dictionary<int, ClientBot> clientBots = new();
	static readonly List<Player> visible = new();
	static readonly List<Systems.StabRenderer.Target> targets = new();

	public override void OnWorldUnload() => ReleaseClientBots();

	public override void Unload() => clientBots.Clear();

	public override void PostUpdateEverything()
	{
		if (Main.netMode == NetmodeID.Server)
			Broadcast();
		else if (Main.netMode == NetmodeID.MultiplayerClient)
			AdvanceClientBots();
	}

	static void Broadcast()
	{
		if (!BotWorldSystem.IsRunning || BotWorldSystem.Bots.Count == 0 || !Netplay.Clients.Any(client => client.IsActive))
			return;
		uint tick = Main.GameUpdateCount;
		if (tick % AppearanceInterval == 0)
			SendAppearance();
		if (tick % StateInterval == 0)
			SendState();
	}

	static ModPacket NewPacket(BotMessage message)
	{
		var packet = ModContent.GetInstance<AutoTerraMod>().GetPacket();
		packet.Write((byte)message);
		return packet;
	}

	static void SendAppearance()
	{
		var packet = NewPacket(BotMessage.Appearance);
		var bots = BotWorldSystem.Bots.Where(bot => bot.Npc != null).ToList();
		packet.Write((byte)bots.Count);
		foreach (var bot in bots)
		{
			var avatar = bot.Avatar;
			packet.Write((short)bot.Id);
			packet.Write((byte)avatar.whoAmI);
			packet.Write(bot.Name ?? "");
			packet.Write((byte)bot.Faction.PvpTeam);
			packet.Write((byte)avatar.skinVariant);
			packet.Write((short)avatar.hair);
			foreach (var color in new[] { avatar.hairColor, avatar.skinColor, avatar.eyeColor, avatar.shirtColor, avatar.underShirtColor, avatar.pantsColor, avatar.shoeColor })
				packet.WriteRGB(color);
			for (int slot = 0; slot < ArmorSlots; slot++)
				packet.Write((short)avatar.armor[slot].type);
			for (int slot = 0; slot < avatar.dye.Length; slot++)
				packet.Write((short)avatar.dye[slot].type);
			foreach (int visual in Visuals(avatar))
				packet.Write((short)visual);
			packet.Write(bot.Brain.Current?.NameKey ?? "");
		}
		packet.Send();
	}

	static int[] Visuals(Player avatar) => new[]
	{
		avatar.head, avatar.body, avatar.legs, avatar.handon, avatar.handoff, avatar.back, avatar.front, avatar.shoe,
		avatar.waist, avatar.shield, avatar.neck, avatar.face, avatar.balloon, avatar.wings, avatar.faceHead, avatar.faceFlower, avatar.beard
	};

	static void SetVisuals(Player avatar, short[] values)
	{
		avatar.head = values[0];
		avatar.body = values[1];
		avatar.legs = values[2];
		avatar.handon = values[3];
		avatar.handoff = values[4];
		avatar.back = values[5];
		avatar.front = values[6];
		avatar.shoe = values[7];
		avatar.waist = values[8];
		avatar.shield = values[9];
		avatar.neck = values[10];
		avatar.face = values[11];
		avatar.balloon = values[12];
		avatar.wings = values[13];
		avatar.faceHead = values[14];
		avatar.faceFlower = values[15];
		avatar.beard = values[16];
	}

	const int VisualCount = 17;

	static void SendState()
	{
		var packet = NewPacket(BotMessage.State);
		var bots = BotWorldSystem.Bots.Where(bot => bot.Npc != null).ToList();
		packet.Write((byte)bots.Count);
		foreach (var bot in bots)
		{
			var body = bot.Npc;
			var avatar = bot.Avatar;
			packet.Write((short)bot.Id);
			packet.WriteVector2(body.position);
			packet.WriteVector2(body.velocity);
			byte flags = 0;
			if (body.direction > 0)
				flags |= FlagFacingRight;
			if (avatar.pulley)
				flags |= FlagPulley;
			if (bot.Motor.IsFlying)
				flags |= FlagFlying;
			if (!bot.IsAlive)
				flags |= FlagDead;
			packet.Write(flags);
			var held = avatar.itemAnimation > 0 || avatar.HeldItem?.holdStyle > 0 || avatar.HeldItem?.fishingPole > 0 ? avatar.lastVisualizedSelectedItem ?? avatar.HeldItem : null;
			packet.Write((short)(held is { IsAir: false } ? held.type : 0));
			packet.Write((short)avatar.itemAnimation);
			packet.Write((short)avatar.itemAnimationMax);
			packet.Write(avatar.itemRotation);
			packet.Write(bot.Hands.AimDirection.ToRotation());
			packet.WriteVector2(bot.Hands.YoyoTarget);
			bool hooked = bot.Grapple.Anchor != null && bot.Grapple.HookProjectile > 0;
			packet.Write((short)(hooked ? bot.Grapple.HookProjectile : 0));
			if (hooked)
				packet.WriteVector2(bot.Grapple.Anchor.Value);
		}
		packet.Send();
	}

	public static void Handle(BinaryReader reader)
	{
		var message = (BotMessage)reader.ReadByte();
		if (Main.netMode != NetmodeID.MultiplayerClient)
			return;
		switch (message)
		{
			case BotMessage.Appearance:
				ReadAppearance(reader);
				break;
			case BotMessage.State:
				ReadState(reader);
				break;
		}
	}

	static void ReadAppearance(BinaryReader reader)
	{
		int count = reader.ReadByte();
		for (int index = 0; index < count; index++)
		{
			int id = reader.ReadInt16();
			int slot = reader.ReadByte();
			var entry = Track(id, slot);
			var avatar = entry.Avatar;
			avatar.name = reader.ReadString();
			avatar.team = reader.ReadByte();
			avatar.skinVariant = reader.ReadByte();
			avatar.hair = reader.ReadInt16();
			avatar.hairColor = reader.ReadRGB();
			avatar.skinColor = reader.ReadRGB();
			avatar.eyeColor = reader.ReadRGB();
			avatar.shirtColor = reader.ReadRGB();
			avatar.underShirtColor = reader.ReadRGB();
			avatar.pantsColor = reader.ReadRGB();
			avatar.shoeColor = reader.ReadRGB();
			for (int armor = 0; armor < ArmorSlots; armor++)
				SetItem(avatar.armor, armor, reader.ReadInt16());
			for (int dye = 0; dye < avatar.dye.Length; dye++)
				SetItem(avatar.dye, dye, reader.ReadInt16());
			var visuals = new short[VisualCount];
			for (int visual = 0; visual < VisualCount; visual++)
				visuals[visual] = reader.ReadInt16();
			SetVisuals(avatar, visuals);
			entry.TaskKey = reader.ReadString();
			entry.Name = avatar.name;
			entry.Team = avatar.team;
			try
			{
				avatar.UpdateDyes();
			}
			catch (Exception)
			{
			}
		}
	}

	static void SetItem(Item[] slots, int index, int type)
	{
		if (slots[index].type == type)
			return;
		slots[index] = new Item();
		if (type > ItemID.None)
			slots[index].SetDefaults(type);
	}

	static ClientBot Track(int id, int slot)
	{
		if (clientBots.TryGetValue(id, out var entry) && entry.Avatar.whoAmI == slot)
			return entry;
		entry ??= new ClientBot();
		entry.Avatar ??= new Player();
		entry.Avatar.whoAmI = slot;
		entry.Avatar.active = false;
		if (slot >= BotWorldSystem.FirstAvatarSlot && slot < Main.maxPlayers - 1)
		{
			Main.player[slot] = entry.Avatar;
			avatarSlots[slot] = true;
		}
		clientBots[id] = entry;
		return entry;
	}

	static void ReadState(BinaryReader reader)
	{
		int count = reader.ReadByte();
		uint now = Main.GameUpdateCount;
		for (int index = 0; index < count; index++)
		{
			int id = reader.ReadInt16();
			var position = reader.ReadVector2();
			var velocity = reader.ReadVector2();
			byte flags = reader.ReadByte();
			int held = reader.ReadInt16();
			int animation = reader.ReadInt16();
			int animationMax = reader.ReadInt16();
			float rotation = reader.ReadSingle();
			float aim = reader.ReadSingle();
			var yoyoTarget = reader.ReadVector2();
			int hookType = reader.ReadInt16();
			Vector2? hookAnchor = hookType > 0 ? reader.ReadVector2() : null;
			if (!clientBots.TryGetValue(id, out var entry))
				continue;
			var avatar = entry.Avatar;
			if (!entry.HasState || Vector2.Distance(entry.Drawn, position) > MotionSnap)
				entry.Drawn = position;
			entry.AuthPosition = position;
			entry.AuthVelocity = velocity;
			entry.AuthAt = now;
			entry.SeenAt = now;
			entry.HasState = true;
			entry.Flying = (flags & FlagFlying) != 0;
			entry.Dead = (flags & FlagDead) != 0;
			avatar.direction = (flags & FlagFacingRight) != 0 ? 1 : -1;
			avatar.pulley = (flags & FlagPulley) != 0;
			if (avatar.pulley)
				avatar.pulleyDir = 1;
			SetItem(avatar.inventory, 0, held);
			avatar.selectedItem = 0;
			avatar.lastVisualizedSelectedItem = avatar.inventory[0];
			avatar.itemAnimation = animation;
			avatar.itemAnimationMax = animationMax;
			avatar.itemTime = animation;
			avatar.itemRotation = rotation;
			entry.Aim = aim.ToRotationVector2();
			entry.YoyoTarget = yoyoTarget;
			entry.HookType = hookType;
			entry.HookAnchor = hookAnchor;
		}
	}

	static void AdvanceClientBots()
	{
		uint now = Main.GameUpdateCount;
		foreach (var entry in clientBots.Values)
		{
			if (!entry.HasState)
				continue;
			var avatar = entry.Avatar;
			float ahead = Math.Min(now - entry.AuthAt, MaxExtrapolateTicks);
			var predicted = entry.AuthPosition + entry.AuthVelocity * ahead;
			entry.Drawn = Vector2.Distance(entry.Drawn, predicted) > MotionSnap ? predicted : Vector2.Lerp(entry.Drawn, predicted, MotionSmooth);
			avatar.oldPosition = avatar.position;
			avatar.position = entry.Drawn;
			avatar.velocity = entry.AuthVelocity;
			if (avatar.itemAnimation > 0)
				avatar.itemAnimation--;
			try
			{
				var item = avatar.lastVisualizedSelectedItem;
				if (avatar.itemAnimation > 0 && item is { IsAir: false })
				{
					avatar.ItemCheck_ApplyUseStyle(0f, item, Item.GetDrawHitbox(item.type, avatar));
					if (item.useStyle == ItemUseStyleID.Shoot)
						avatar.itemRotation = (float)Math.Atan2(entry.Aim.Y * avatar.direction, entry.Aim.X * avatar.direction);
				}
				avatar.PlayerFrame();
				if (avatar.wings > 0)
				{
					if (avatar.velocity.Y != 0f)
						avatar.WingFrame(entry.Flying);
					else
						avatar.wingFrame = 0;
				}
			}
			catch (Exception)
			{
			}
		}
	}

	public static void DrawClientAvatars()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient || clientBots.Count == 0)
			return;
		var view = new Rectangle((int)Main.screenPosition.X - 64, (int)Main.screenPosition.Y - 64, Main.screenWidth + 128, Main.screenHeight + 128);
		uint now = Main.GameUpdateCount;
		visible.Clear();
		targets.Clear();
		foreach (var entry in clientBots.Values)
		{
			if (!entry.HasState || entry.Dead || now - entry.SeenAt > StaleTicks || !view.Intersects(entry.Avatar.Hitbox))
				continue;
			visible.Add(entry.Avatar);
			targets.Add(new Systems.StabRenderer.Target(entry.Avatar, entry.Avatar.Center, entry.Aim, entry.YoyoTarget));
		}
		if (visible.Count == 0)
			return;
		try
		{
			Main.PlayerRenderer.DrawPlayers(Main.Camera, visible);
			Systems.StabRenderer.DrawTargets(targets);
		}
		catch (Exception exception)
		{
			if (drawFailureLogged)
				return;
			drawFailureLogged = true;
			ModContent.GetInstance<AutoTerraMod>().Logger.Warn("Drawing synced bots failed", exception);
		}
	}

	static bool drawFailureLogged;

	static void ReleaseClientBots()
	{
		foreach (var entry in clientBots.Values)
		{
			int slot = entry.Avatar.whoAmI;
			if (slot >= 0 && slot < Main.maxPlayers && Main.player[slot] == entry.Avatar)
				Main.player[slot] = new Player();
		}
		clientBots.Clear();
		Array.Clear(avatarSlots);
	}
}
