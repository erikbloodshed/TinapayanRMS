using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A unit a packaging line's quantity can be entered in: the material's base unit (<see cref="Key"/> 0), or one of its
/// alternative units by <see cref="RawMaterialUnit"/> id, with what one of it costs (<see cref="UnitCost"/>).
/// </summary>
public sealed record PackagingUnitChoice(int Key, string Abbreviation, decimal BaseUnitsPerUnit, decimal UnitCost)
{
    /// <summary>The base unit's key: it has no <see cref="RawMaterialUnit"/> row of its own, so a line in it stores no unit.</summary>
    public const int BaseUnitKey = 0;
}

/// <summary>
/// One packaging line, read and priced at one basis: how much of a material one sellable unit (or one set) takes, in the
/// unit it was entered in, and what that costs. Immutable; the screens' editable line is PackagingLineRow, which gives one
/// of these as it stands.
/// </summary>
/// <param name="PackagingMaterialId">The material's id; null for a row with none picked yet.</param>
/// <param name="QtyPerUnit">How much of the material, in <paramref name="UnitAbbreviation"/>'s unit; null while nothing is typed.</param>
/// <param name="UnitAbbreviation">The unit the quantity is in; null while no material is picked.</param>
/// <param name="BaseQty">The quantity in the material's base unit; null while nothing is typed.</param>
/// <param name="UnitCost">What one of the line's unit costs.</param>
/// <param name="CostPerUnit">What the line costs for one sellable unit (or one set).</param>
public sealed record PricedPackagingLine(
    int? PackagingMaterialId,
    RawMaterial? PackagingMaterial,
    decimal? QtyPerUnit,
    string? UnitAbbreviation,
    decimal? BaseQty,
    decimal UnitCost,
    decimal CostPerUnit)
{
    /// <summary>
    /// <paramref name="qtyPerUnit"/> of <paramref name="material"/> in <paramref name="unitKey"/>'s unit (the base unit when the
    /// material has no such unit), priced at <paramref name="basis"/>.
    /// </summary>
    public static PricedPackagingLine Price(int? materialId, RawMaterial? material, decimal? qtyPerUnit, int unitKey, CostBasis basis)
    {
        var choices = UnitChoices(material, basis);
        var choice = choices.FirstOrDefault(c => c.Key == unitKey) ?? (choices.Count > 0 ? choices[0] : null);
        var baseUnitsPerEntryUnit = choice is null or { Key: PackagingUnitChoice.BaseUnitKey } ? (decimal?)null : choice.BaseUnitsPerUnit;
        var costPerBaseUnit = material is null ? 0m : RawMaterialRules.CostPerBaseUnitAt(material, basis);
        return new PricedPackagingLine(
            materialId,
            material,
            qtyPerUnit,
            choice?.Abbreviation,
            qtyPerUnit is { } qty ? UnitConversionRules.ToBaseQuantity(qty, baseUnitsPerEntryUnit) : null,
            choice?.UnitCost ?? 0m,

            // From the cost per base unit, never from UnitCost, which is already scaled to the unit.
            ProductRules.CalculatePackagingLineCost(costPerBaseUnit, qtyPerUnit ?? 0m, baseUnitsPerEntryUnit));
    }

    /// <summary>A saved line at <paramref name="basis"/>, its material taken from <paramref name="materialsById"/> when that has it.</summary>
    public static PricedPackagingLine FromSaved(PackagingLine line, CostBasis basis, IReadOnlyDictionary<int, RawMaterial>? materialsById = null)
    {
        // A line saved as pieces per unit is priced at the quantity it comes to.
        var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
        var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
        var material = materialsById?.GetValueOrDefault(line.PackagingMaterialId) ?? line.PackagingMaterial;
        return Price(line.PackagingMaterialId, material, qtyPerUnit, line.UnitId ?? PackagingUnitChoice.BaseUnitKey, basis);
    }

    /// <summary>The units a line of <paramref name="material"/> can be entered in, priced at <paramref name="basis"/>: its base unit first, then its alternative units.</summary>
    public static IReadOnlyList<PackagingUnitChoice> UnitChoices(RawMaterial? material, CostBasis basis)
    {
        if (material is null)
        {
            return [];
        }

        var costPerBaseUnit = RawMaterialRules.CostPerBaseUnitAt(material, basis);
        return
        [
            new(PackagingUnitChoice.BaseUnitKey, BaseUnitText.AbbreviationOf(material), 1m, costPerBaseUnit),
            .. material.Units
                .Where(u => u.UnitOfMeasure is not null)
                .Select(u => new PackagingUnitChoice(u.Id, u.UnitOfMeasure!.Abbreviation, u.BaseUnitsPerUnit, costPerBaseUnit * u.BaseUnitsPerUnit)),
        ];
    }
}
