namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One row of the Products screen's Scaling tab (an SfTreeGrid): a section, a product a composite holds, or the packaging,
/// with what's in it under it; or a line, which a made ingredient's BOM opens under. Built whole each time the tab is
/// worked out again (RecipeScaler), so nothing in it changes.
/// </summary>
/// <param name="Name">What it is: "Dough", "Flour", "Ensaymada × 6", "Packaging".</param>
/// <param name="Detail">What it comes to beside its name, muted: "2 batches · 200 pcs (40 extra)", "add-in"; empty for none.</param>
/// <param name="AmountText">How much of it, in its unit: "2,000 g", "20 box"; empty for a group of things in different units.</param>
/// <param name="Cost">What that much costs at the screen's cost basis; a group's is its rows'.</param>
/// <param name="IsGroup">A section, a product or the packaging: its name is in bold.</param>
public sealed record ScalingRow(string Name, string Detail, string AmountText, decimal Cost, bool IsGroup, IReadOnlyList<ScalingRow> Children)
{
    /// <summary>A line with nothing under it.</summary>
    public static ScalingRow Line(string name, string detail, string amountText, decimal cost) => new(name, detail, amountText, cost, false, []);

    /// <summary>Two rows are equal when they show the same, their rows under them included.</summary>
    public bool Equals(ScalingRow? other) =>
        other is not null
        && Name == other.Name
        && Detail == other.Detail
        && AmountText == other.AmountText
        && Cost == other.Cost
        && IsGroup == other.IsGroup
        && Children.SequenceEqual(other.Children);

    public override int GetHashCode() => HashCode.Combine(Name, Detail, AmountText, Cost, IsGroup, Children.Count);
}
