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

    /// <summary>
    /// The day it's to be made (2026-10-29, at the user's request: plans by date), or null for a plan made any day, such as a
    /// weekday's usual. The Saved Plans list is in its order, and the screen opens the plan for today by itself.
    /// </summary>
    public DateOnly? MakeOn { get; set; }

    /// <summary>"Sat, Oct 31", as the Saved Plans list shows <see cref="MakeOn"/>; blank for a plan made any day.</summary>
    public string MakeOnText => MakeOn is { } day ? day.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>Its mixes are rounded up to half batches rather than whole ones.</summary>
    public bool HalfBatches { get; set; }

    /// <summary>Its products, in the order the plan's table had them (SortOrder).</summary>
    public List<SavedPlanLine> Lines { get; set; } = [];
}
