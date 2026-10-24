namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A Production Plan saved under a name ("Saturday", "Fiesta order"), to open on the Production Plan screen again
/// (2026-10-24): the products to make and how many, and whether its mixes are made in half batches.
/// </summary>
public class SavedPlan
{
    public int Id { get; set; }

    /// <summary>Unique, ignoring case.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When it was last saved, in the shop's local time.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>Its mixes are rounded up to half batches rather than whole ones.</summary>
    public bool HalfBatches { get; set; }

    /// <summary>Its products, in the order the plan's table had them (SortOrder).</summary>
    public List<SavedPlanLine> Lines { get; set; } = [];
}
