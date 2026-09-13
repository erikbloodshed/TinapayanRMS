namespace TinapayanRMS.Domain.Enums;

/// <summary>What a raw material is for, which decides where it can be used (TinapayanRMS.ApplicationCore.RawMaterialRules).</summary>
public enum RawMaterialType
{
    /// <summary>Goes into recipes (sub-assembly and BOM lines), measured in grams; purchased or made.</summary>
    Ingredient,

    /// <summary>Goes on a product's packaging lines, in a base unit of its own; always purchased.</summary>
    Packaging,
}
