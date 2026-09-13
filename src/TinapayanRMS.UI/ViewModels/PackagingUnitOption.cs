using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A unit a packaging material can be priced or used in: its base unit (no PackagingMaterialUnit)
/// or one of its alternative units. Used by the Packaging Materials pickers and the Products
/// packaging line editor.
/// </summary>
public sealed class PackagingUnitOption(PackagingMaterialUnit? unit, UnitOfMeasure? baseUnit)
{
    public PackagingMaterialUnit? Unit { get; } = unit;

    public int? UnitId => Unit?.Id;

    public string Name => Unit?.UnitOfMeasure?.Name ?? baseUnit?.Name ?? "?";

    public string Abbreviation => Unit?.UnitOfMeasure?.Abbreviation ?? baseUnit?.Abbreviation ?? "?";

    public decimal BaseUnitsPerUnit => Unit?.BaseUnitsPerUnit ?? 1m;

    /// <summary>The material's base unit first (its BaseUnit must be loaded), then its alternative units.</summary>
    public static List<PackagingUnitOption> For(PackagingMaterial? material) =>
        material is null ? [] : [new(null, material.BaseUnit), .. material.Units.Select(u => new PackagingUnitOption(u, material.BaseUnit))];
}
