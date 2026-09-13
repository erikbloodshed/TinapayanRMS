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
}
