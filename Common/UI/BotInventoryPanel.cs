using AutoTerraMod.Common.Spectating;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.UI;

namespace AutoTerraMod.Common.UI;

public static class BotInventoryPanel
{
	const int Columns = 10;
	const int Rows = 5;
	const int EquipSlots = 10;
	const float SlotScale = 0.85f;
	const float SlotSpacing = 4f;

	public static bool Visible { get; set; }

	public static bool Draw()
	{
		var bot = SpectatorSystem.Followed;
		if (!Visible || bot == null)
			return true;
		var avatar = bot.Avatar;
		float slotSize = TextureAssets.InventoryBack.Width() * SlotScale;
		float step = slotSize + SlotSpacing;
		var origin = new Vector2(Main.screenWidth / 2f - Columns * step / 2f, 120f);
		Utils.DrawBorderString(Main.spriteBatch, Language.GetTextValue("Mods.AutoTerraMod.UI.InventoryOf", bot.Name), origin + new Vector2(0f, -28f), bot.Faction.Color);

		float oldScale = Main.inventoryScale;
		Main.inventoryScale = SlotScale;
		for (int slot = 0; slot < Columns * Rows; slot++)
		{
			var position = origin + new Vector2(slot % Columns * step, slot / Columns * step);
			DrawSlot(avatar.inventory, slot, ItemSlot.Context.InventoryItem, position, slotSize);
		}
		var equipOrigin = origin + new Vector2(Columns * step + 24f, 0f);
		for (int slot = 0; slot < EquipSlots; slot++)
		{
			int context = slot < 3 ? ItemSlot.Context.EquipArmor : ItemSlot.Context.EquipAccessory;
			var position = equipOrigin + new Vector2(slot / Rows * step, slot % Rows * step);
			DrawSlot(avatar.armor, slot, context, position, slotSize);
		}
		Main.inventoryScale = oldScale;
		return true;
	}

	static void DrawSlot(Item[] items, int slot, int context, Vector2 position, float slotSize)
	{
		ItemSlot.Draw(Main.spriteBatch, items, context, slot, position);
		var area = new Rectangle((int)position.X, (int)position.Y, (int)slotSize, (int)slotSize);
		if (!area.Contains(Main.MouseScreen.ToPoint()) || items[slot].IsAir)
			return;
		Main.LocalPlayer.mouseInterface = true;
		var hovered = items[slot];
		ItemSlot.MouseHover(ref hovered, context);
	}
}
