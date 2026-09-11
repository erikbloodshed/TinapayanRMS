using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

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
    /// The entry CurrentCost is recalculated from: the latest by date, and among
    /// same-dated entries, the most recently added (highest id).
    /// </summary>
    public static T? FindLatest<T>(IEnumerable<T> entries) where T : IPriceHistoryEntry =>
        entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .FirstOrDefault();
}
