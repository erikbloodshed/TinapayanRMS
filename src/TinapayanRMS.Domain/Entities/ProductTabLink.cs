namespace TinapayanRMS.Domain;

/// <summary>
/// A "Live Template" link: one of a product's tabs (a SubAssemblyRole, or its one
/// Packaging tab) reads through to the same tab on another product instead of owning
/// its own rows. A tab with no ProductTabLink row is independent — its own
/// ProductSubAssemblyUsage/PackagingLine rows are the source of truth.
/// </summary>
public class ProductTabLink
{
    public int Id { get; set; }

    /// <summary>The derived product — the one whose tab reads through to LinkedProduct.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public ProductTabKind TabKind { get; set; }

    /// <summary>Set only when TabKind is SubAssemblyRole; null for the Packaging tab.</summary>
    public int? SubAssemblyRoleId { get; set; }

    public SubAssemblyRole? SubAssemblyRole { get; set; }

    /// <summary>The product this tab is linked to and stays in sync with.</summary>
    public int LinkedProductId { get; set; }

    public Product? LinkedProduct { get; set; }
}
