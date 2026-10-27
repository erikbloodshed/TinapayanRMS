namespace TinapayanRMS.Domain.Enums;

/// <summary>
/// Which materials a raw material category holds (TinapayanRMS.ApplicationCore.RawMaterialRules.CategoryTypeFor): each
/// material goes in a category of exactly one of these.
/// </summary>
public enum RawMaterialCategoryType
{
    /// <summary>Purchased and linked ingredients (Flour, Dairy).</summary>
    Ingredient,

    /// <summary>Made ingredients, and only them (In-House). Its folders sit among the ingredient ones.</summary>
    Made,

    /// <summary>Packaging materials (Boxes, Labels).</summary>
    Packaging,
}
