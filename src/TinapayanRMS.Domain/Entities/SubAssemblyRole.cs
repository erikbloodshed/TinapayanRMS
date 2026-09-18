namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An editable lookup, like UnitOfMeasure and RawMaterialCategory — new roles (e.g. Glaze)
/// can be added by the shop owner. Unlike them, it's seeded with five starting roles.
/// </summary>
public class SubAssemblyRole
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Sets the order of a product's recipe tabs.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// The Packaging role (seeded, Id 6): a standalone sub-assembly in it is a packaging set — packaging materials
    /// per set rather than ingredients in grams — which a product's Packaging tab can link. A product's own
    /// sub-assemblies never take it. Checked by this flag, not the name, so renaming the role is safe; it can't be
    /// deleted, and nothing clears the flag.
    /// </summary>
    public bool IsPackaging { get; set; }
}
