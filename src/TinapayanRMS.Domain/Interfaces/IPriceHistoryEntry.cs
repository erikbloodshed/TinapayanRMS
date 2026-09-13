namespace TinapayanRMS.Domain.Interfaces;

/// <summary>
/// The shape of a price history entry (RawMaterialPriceHistory) that
/// ApplicationCore.PriceHistoryRules.FindLatest works over, without depending on the entity.
/// </summary>
public interface IPriceHistoryEntry
{
    int Id { get; }

    DateOnly Date { get; }

    decimal Price { get; }
}
