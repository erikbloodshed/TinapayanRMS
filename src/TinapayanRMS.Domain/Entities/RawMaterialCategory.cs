using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for raw materials (Flour, Dairy, Boxes, …), each for one type of material.</summary>
public class RawMaterialCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which materials it groups: ingredients (Flour, Dairy) or packaging (Boxes, Labels). A material only goes in a
    /// category of its own type (RawMaterialRules.EnsureCategoryMatchesType).
    /// </summary>
    public RawMaterialType Type { get; set; }
}
