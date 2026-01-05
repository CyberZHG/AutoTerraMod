using System.Collections.Generic;
using Terraria;

namespace AutoTerraMod.Common.Bots;

public static class ItemReservations
{
	const int ReservationTicks = 300;

	static readonly Dictionary<int, (int botId, uint until)> reservations = new();

	public static void Reserve(int itemIndex, Bot recipient) =>
		reservations[itemIndex] = (recipient.Id, Main.GameUpdateCount + ReservationTicks);

	public static bool IsReservedForOther(int itemIndex, Bot bot) =>
		reservations.TryGetValue(itemIndex, out var reservation)
		&& Main.GameUpdateCount < reservation.until
		&& reservation.botId != bot.Id;

	public static void Clear() => reservations.Clear();
}
