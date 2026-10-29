using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for raw materials (Flour, In-House, Boxes, …), each for one type of material.</summary>
public sealed class RawMaterialCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which materials it groups: purchased and linked ingredients (Flour, Dairy), made ingredients (In-House) or
    /// packaging (Boxes, Labels). A material only goes in a category of its own type (RawMaterialRules.EnsureCategoryMatchesType).
    /// </summary>
    public RawMaterialCategoryType Type { get; set; }
}
