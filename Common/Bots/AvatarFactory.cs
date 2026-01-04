using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;

namespace AutoTerraMod.Common.Bots;

public static class AvatarFactory
{
	static readonly Color[] SkinTones =
	{
		new(255, 213, 178), new(240, 190, 150), new(214, 160, 120), new(180, 124, 88), new(140, 92, 62), new(100, 64, 44)
	};

	public static Player Create(string name, Color factionColor, UnifiedRandom rand)
	{
		var avatar = new Player
		{
			name = name,
			skinVariant = rand.Next(8),
			hair = rand.Next(51),
			hairColor = new Color(rand.Next(40, 256), rand.Next(40, 200), rand.Next(20, 160)),
			skinColor = SkinTones[rand.Next(SkinTones.Length)],
			eyeColor = new Color(rand.Next(30, 120), rand.Next(60, 160), rand.Next(80, 200)),
			statLifeMax = 100,
			statLife = 100,
			statManaMax = 20,
			statMana = 20
		};
		Dress(avatar, factionColor);
		GiveStarterKit(avatar);
		return avatar;
	}

	public static void Dress(Player avatar, Color factionColor)
	{
		avatar.shirtColor = factionColor;
		avatar.underShirtColor = Color.Lerp(factionColor, Color.White, 0.4f);
		avatar.pantsColor = Color.Lerp(factionColor, Color.Black, 0.55f);
		avatar.shoeColor = new Color(90, 64, 44);
	}

	static void GiveStarterKit(Player avatar)
	{
		avatar.inventory[0].SetDefaults(ItemID.CopperShortsword);
		avatar.inventory[1].SetDefaults(ItemID.CopperPickaxe);
		avatar.inventory[2].SetDefaults(ItemID.CopperAxe);
	}
}
