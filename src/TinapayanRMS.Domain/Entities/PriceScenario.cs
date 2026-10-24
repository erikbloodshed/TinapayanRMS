namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A set of raw material price changes saved from the Price Simulator under a name ("Supplier A quote, Nov"), to open
/// there again later. Saving it changes no real price.
/// </summary>
public class PriceScenario
{
    public int Id { get; set; }

    /// <summary>Unique, ignoring case.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When it was last saved, in the shop's local time.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>Its price changes, in the order the table had them (SortOrder).</summary>
    public List<PriceScenarioLine> Lines { get; set; } = [];
}
