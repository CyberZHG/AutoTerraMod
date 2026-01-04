using System.Linq;
using AutoTerraMod.Common.Bots;
using AutoTerraMod.Common.Config;
using AutoTerraMod.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AutoTerraMod.Common.Spectating;

public sealed class SpectatorSystem : ModSystem
{
	public static ModKeybind NextBotKey { get; private set; }
	public static ModKeybind PreviousBotKey { get; private set; }
	public static ModKeybind FreeCameraKey { get; private set; }
	public static ModKeybind InventoryKey { get; private set; }

	public static bool FreeCamera { get; private set; } = true;
	static int followedBotId = -1;

	public static bool IsSpectating => BotWorldSystem.IsRunning && Main.netMode == NetmodeID.SinglePlayer && ModContent.GetInstance<AutoTerraConfig>().SpectatorMode;

	public static Bot Followed
	{
		get
		{
			if (!IsSpectating || FreeCamera)
				return null;
			var bot = BotWorldSystem.FindBot(followedBotId);
			if (bot != null)
				return bot;
			bot = BotWorldSystem.Bots.FirstOrDefault(candidate => candidate.IsAlive) ?? BotWorldSystem.Bots.FirstOrDefault();
			followedBotId = bot?.Id ?? -1;
			return bot;
		}
	}

	public override void Load()
	{
		NextBotKey = KeybindLoader.RegisterKeybind(Mod, "NextBot", "OemCloseBrackets");
		PreviousBotKey = KeybindLoader.RegisterKeybind(Mod, "PreviousBot", "OemOpenBrackets");
		FreeCameraKey = KeybindLoader.RegisterKeybind(Mod, "FreeCamera", "OemPipe");
		InventoryKey = KeybindLoader.RegisterKeybind(Mod, "BotInventory", "I");
	}

	public override void Unload()
	{
		NextBotKey = null;
		PreviousBotKey = null;
		FreeCameraKey = null;
		InventoryKey = null;
	}

	public override void OnWorldLoad()
	{
		followedBotId = -1;
		FreeCamera = true;
	}

	public static void Cycle(int step)
	{
		var bots = BotWorldSystem.Bots;
		if (bots.Count == 0)
			return;
		int current = bots.FindIndex(bot => bot.Id == followedBotId);
		int next = ((current < 0 ? 0 : current + step) % bots.Count + bots.Count) % bots.Count;
		var followed = bots[next];
		followedBotId = followed.Id;
		FreeCamera = false;
		Main.NewText(Language.GetTextValue("Mods.AutoTerraMod.UI.NowFollowing", followed.Name, followed.Faction.Name), followed.Faction.Color);
	}

	public static void ToggleFreeCamera()
	{
		FreeCamera = !FreeCamera;
		Main.NewText(Language.GetTextValue(FreeCamera ? "Mods.AutoTerraMod.UI.FreeCameraOn" : "Mods.AutoTerraMod.UI.FreeCameraOff"));
	}

	public override void ModifyScreenPosition()
	{
		var bot = Followed;
		if (bot == null)
			return;
		Main.screenPosition = bot.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
	}
}

public sealed class SpectatorPlayer : ModPlayer
{
	bool ghostedBySpectator;

	public override void ProcessTriggers(TriggersSet triggersSet)
	{
		if (!SpectatorSystem.IsSpectating)
			return;
		if (SpectatorSystem.NextBotKey.JustPressed || SpectatorSystem.PreviousBotKey.JustPressed || SpectatorSystem.FreeCameraKey.JustPressed)
			Mod.Logger.Info($"Spectator key pressed: next={SpectatorSystem.NextBotKey.JustPressed} previous={SpectatorSystem.PreviousBotKey.JustPressed} free={SpectatorSystem.FreeCameraKey.JustPressed}");
		if (SpectatorSystem.NextBotKey.JustPressed)
			SpectatorSystem.Cycle(1);
		if (SpectatorSystem.PreviousBotKey.JustPressed)
			SpectatorSystem.Cycle(-1);
		if (SpectatorSystem.FreeCameraKey.JustPressed)
			SpectatorSystem.ToggleFreeCamera();
		if (SpectatorSystem.InventoryKey.JustPressed)
			UI.BotInventoryPanel.Visible = !UI.BotInventoryPanel.Visible;
	}

	public override void PreUpdate()
	{
		if (Player.whoAmI != Main.myPlayer)
			return;
		if (!SpectatorSystem.IsSpectating)
		{
			if (ghostedBySpectator)
			{
				Player.ghost = false;
				ghostedBySpectator = false;
			}
			return;
		}
		Player.ghost = true;
		ghostedBySpectator = true;
		var followed = SpectatorSystem.Followed;
		if (followed == null)
			return;
		Player.Center = followed.Center;
		Player.velocity = Vector2.Zero;
	}
}
