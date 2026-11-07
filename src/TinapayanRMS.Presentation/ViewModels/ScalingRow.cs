using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>What the Sub-Assemblies screen's Scale makes weigh what's typed: the whole batch, or the lines ticked as its base.</summary>
public enum BatchScaleBasis
{
    Batch,
    Base,
}

/// <summary>A choice in Scale's picker: the basis and how it reads, "the whole batch".</summary>
public sealed record BatchScaleOption(BatchScaleBasis Basis, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A unit an amount of an ingredient is typed in on the Scaling tab: "g", or one of the ingredient's own, "kg" (1,000 g), "bag" (25,000 g).</summary>
public sealed record IngredientAmountUnit(string Name, decimal Grams)
{
    /// <summary>The gram, which every ingredient is measured in.</summary>
    public static readonly IngredientAmountUnit Gram = new("g", 1m);

    /// <summary>Grams, then <paramref name="ingredient"/>'s alternative units by size; only grams for none.</summary>
    public static IReadOnlyList<IngredientAmountUnit> For(RawMaterial? ingredient) =>
    [
        Gram,
        .. (ingredient?.Units ?? [])
            .Where(u => u.UnitOfMeasure is not null && u.BaseUnitsPerUnit > 0m)
            .OrderBy(u => u.BaseUnitsPerUnit)
            .Select(u => new IngredientAmountUnit(u.UnitOfMeasure!.Abbreviation is { Length: > 0 } abbreviation ? abbreviation : u.UnitOfMeasure.Name, u.BaseUnitsPerUnit)),
    ];

    public override string ToString() => Name;
}

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
