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
    /// The entry CurrentCost is recalculated from: the latest by date, and among
    /// same-dated entries, the most recently added (highest id).
    /// </summary>
    public static T? FindLatest<T>(IEnumerable<T> entries) where T : IPriceHistoryEntry =>
        entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .FirstOrDefault();
}
