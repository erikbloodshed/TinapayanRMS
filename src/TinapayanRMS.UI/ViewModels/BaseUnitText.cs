using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A raw material's base unit as text: the gram for an ingredient, which has no unit of measure row
/// (RawMaterial.BaseUnitId is null), or a packaging material's own base unit (its BaseUnit must be loaded).
/// </summary>
public static class BaseUnitText
{
    public static string NameOf(RawMaterial? material) =>
        material is { Type: RawMaterialType.Packaging } ? material.BaseUnit?.Name ?? "?" : "gram";

    public static string AbbreviationOf(RawMaterial? material) =>
        material is { Type: RawMaterialType.Packaging } ? material.BaseUnit?.Abbreviation ?? "?" : "g";
}
