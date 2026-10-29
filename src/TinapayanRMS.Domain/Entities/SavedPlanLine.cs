namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One line of a <see cref="SavedPlan"/>: a product and how many of its sellable units to make. A product can be on
/// more than one line, as on the screen, where its units are added together.
/// </summary>
public sealed class SavedPlanLine
{
    public int Id { get; set; }

    public int SavedPlanId { get; set; }

    public SavedPlan? SavedPlan { get; set; }

    /// <summary>Deleting the product for good takes its lines out of every saved plan.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Sellable units to make: pieces, or boxes for a product sold by the box. Always more than 0.</summary>
    public decimal Units { get; set; }

    /// <summary>
    /// The sellable units actually made, typed once the plan's been baked (2026-10-29, at the user's request: record actual
    /// results); null until then. Reports' Production Results sets it against <see cref="Units"/>.
    /// </summary>
    public decimal? MadeUnits { get; set; }

    /// <summary>Of <see cref="MadeUnits"/>, how many were left over unsold; null when not recorded.</summary>
    public decimal? LeftoverUnits { get; set; }

    /// <summary>Where the line sits in the plan, from 0.</summary>
    public int SortOrder { get; set; }
}
