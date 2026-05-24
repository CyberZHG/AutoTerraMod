using AutoTerraMod.Common.Spectating;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Chat;

namespace AutoTerraMod.Common.UI;

public static class BotInventoryPanel
{
	const int Columns = 10;
	const int Rows = 5;
	const int EquipSlots = 10;
	const int EquipColumns = 3;
	const float ChestScale = 0.755f;
	const float ChestLeft = 73f;
	const float SlotPitch = 56f;
	const float NameLeft = 504f;
	const float EquipLeft = 506f;
	const float EquipTopOffset = 40f;

	static readonly int[] MiscContexts =
	{
		ItemSlot.Context.EquipPet, ItemSlot.Context.EquipLight, ItemSlot.Context.EquipMinecart, ItemSlot.Context.EquipMount, ItemSlot.Context.EquipGrapple
	};

	public static bool Visible { get; set; }

	public static bool Draw()
	{
		var bot = SpectatorSystem.Followed;
		if (!Visible || bot == null)
			return true;
		var avatar = bot.Avatar;
		float top = Main.instance.invBottom;
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = ChestScale;
		float pitch = SlotPitch * ChestScale;
		if (Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, ChestLeft, top, Columns * pitch, Rows * pitch) && !PlayerInput.IgnoreMouseInterface)
			Main.LocalPlayer.mouseInterface = true;
		for (int slot = 0; slot < Columns * Rows; slot++)
			DrawSlot(avatar.inventory, slot, ItemSlot.Context.ChestItem, new Vector2(ChestLeft + slot % Columns * pitch, top + slot / Columns * pitch));
		DrawName(Language.GetTextValue("Mods.AutoTerraMod.UI.InventoryOf", bot.Name), top);
		var equipOrigin = new Vector2(EquipLeft, top + EquipTopOffset);
		for (int slot = 0; slot < EquipSlots; slot++)
		{
			int context = slot < 3 ? ItemSlot.Context.EquipArmor : ItemSlot.Context.EquipAccessory;
			DrawSlot(avatar.armor, slot, context, equipOrigin + new Vector2(slot / Rows * pitch, slot % Rows * pitch));
		}
		for (int slot = 0; slot < MiscContexts.Length; slot++)
			DrawSlot(avatar.miscEquips, slot, MiscContexts[slot], equipOrigin + new Vector2((EquipColumns - 1) * pitch, slot * pitch));
		Main.inventoryScale = oldScale;
		return true;
	}

	static void DrawName(string text, float top)
	{
		var color = Color.White * (1f - (255f - Main.mouseTextColor) / 255f * 0.5f);
		color.A = 255;
		ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, new Vector2(NameLeft, top), color, 0f, Vector2.Zero, Vector2.One, -1f, 1.5f);
	}

	static void DrawSlot(Item[] items, int slot, int context, Vector2 position)
	{
		ItemSlot.Draw(Main.spriteBatch, items, context, slot, position);
		float size = TextureAssets.InventoryBack.Width() * Main.inventoryScale;
		if (!Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, position.X, position.Y, size, size) || PlayerInput.IgnoreMouseInterface)
			return;
		Main.LocalPlayer.mouseInterface = true;
		if (items[slot].IsAir)
			return;
		var hovered = items[slot];
		ItemSlot.MouseHover(ref hovered, context);
	}
}
