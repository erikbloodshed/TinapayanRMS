using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A raw material's base unit as text: the gram for an ingredient, which has no unit of measure row
/// (RawMaterial.BaseUnitId is null), or a packaging material's own base unit (its BaseUnit must be loaded).
/// </summary>
public static class BaseUnitText
{
    public static string NameOf(RawMaterial? material) =>
        material is null ? "gram" : NameOf(material.Type, material.BaseUnit);

    public static string AbbreviationOf(RawMaterial? material) =>
        material is null ? "g" : AbbreviationOf(material.Type, material.BaseUnit);

    /// <summary>For a material still being filled in: its type and the base unit picked so far.</summary>
    public static string NameOf(RawMaterialType type, UnitOfMeasure? baseUnit) =>
        type == RawMaterialType.Packaging ? baseUnit?.Name ?? "?" : "gram";

    public static string AbbreviationOf(RawMaterialType type, UnitOfMeasure? baseUnit) =>
        type == RawMaterialType.Packaging ? baseUnit?.Abbreviation ?? "?" : "g";
}
