using TinapayanRMS.Domain.Interfaces;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class PriceHistoryRules
{
    public static void EnsureValidPrice(decimal price)
    {
        if (price <= 0)
        {
            throw new InvalidOperationException("A price history entry's price must be positive.");
        }
    }

    /// <summary>
    /// Whether saving an edit form should record <paramref name="enteredPrice"/> as a new price
    /// history entry, given the item's current price. An empty field and 0 both mean "no price",
    /// like an unpriced item's null CurrentCost, so saving an unpriced item with the field left
    /// empty records nothing. Any other difference counts, including clearing a priced item's
    /// field, which the positive-price check then rejects: a price is removed by deleting its
    /// history entry, never by blanking the field.
    /// </summary>
    public static bool IsNewPrice(decimal? currentPrice, decimal? enteredPrice) =>
        (enteredPrice ?? 0m) != (currentPrice ?? 0m);

    /// <summary>
    /// The entry an item's cost is recalculated from: the latest by date, and among
    /// same-dated entries, the most recently added (highest id). Only for saved entries;
    /// use FindLatestAfterAdding for one that hasn't been given an id yet.
    /// </summary>
    public static T? FindLatest<T>(IEnumerable<T> entries) where T : IPriceHistoryEntry =>
        entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .FirstOrDefault();

    /// <summary>
    /// The latest entry once <paramref name="added"/> is saved. It will get the highest id, so it
    /// also wins a tie on date — which FindLatest can't tell while its id is still 0. (It used to
    /// be asked anyway, so a second price entered on the same day never became the current one.)
    /// </summary>
    public static T FindLatestAfterAdding<T>(T? currentLatest, T added) where T : class, IPriceHistoryEntry =>
        currentLatest is null || added.Date >= currentLatest.Date ? added : currentLatest;
}
