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

    /// <summary>
    /// A section in this role made in a product (new, copied, or Make a Copy) is saved as that product's own
    /// sub-assembly (SubAssembly.OwnerProductId): edited in its recipe, and not on the Sub-Assemblies screen. Set on the
    /// Settings dialog's Roles tab; never on the Packaging role. Only a default: in any role, a product can unlink a
    /// linked section into a copy of its own. It's also the roles a composite product can have sections in (Product.IsComposite),
    /// so it can't be cleared while a composite has one in it.
    /// </summary>
    public bool IsProductOwned { get; set; }
}
