using System.IO;
using Terraria.ModLoader;

namespace AutoTerraMod;

public sealed class AutoTerraMod : Mod
{
	public override void HandlePacket(BinaryReader reader, int whoAmI) => Common.Net.BotNet.Handle(reader);
}
