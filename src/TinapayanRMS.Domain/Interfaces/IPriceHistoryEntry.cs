namespace TinapayanRMS.Domain;

/// <summary>
/// Shared shape of IngredientPriceHistory and PackagingPriceHistory, so
/// ApplicationCore.PriceHistoryRules.FindLatest can work generically over either.
/// </summary>
public interface IPriceHistoryEntry
{
    int Id { get; }

    DateOnly Date { get; }

    decimal Price { get; }
}
