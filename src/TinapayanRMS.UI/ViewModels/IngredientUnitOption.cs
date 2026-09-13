using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A unit an ingredient can be priced or measured in: the gram, its base unit (no IngredientUnit),
/// or one of its alternative units. Used by every picker that chooses one: the purchase unit, the
/// usage example, and the Ingredients BOM and Sub-Assemblies line editors.
/// </summary>
public sealed class IngredientUnitOption(IngredientUnit? unit)
{
    public IngredientUnit? Unit { get; } = unit;

    public int? UnitId => Unit?.Id;

    public string Name => Unit?.UnitOfMeasure?.Name ?? "gram";

    public string Abbreviation => Unit?.UnitOfMeasure?.Abbreviation ?? "g";

    public decimal GramsPerUnit => Unit?.GramsPerUnit ?? 1m;

    /// <summary>The gram first, then the ingredient's alternative units.</summary>
    public static List<IngredientUnitOption> For(Ingredient? ingredient) =>
        [new(null), .. (ingredient?.Units ?? []).Select(u => new IngredientUnitOption(u))];
}
