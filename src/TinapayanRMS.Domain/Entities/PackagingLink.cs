namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A "Live Template" link for packaging: a product's packaging reads through to another product's
/// instead of owning its own lines. A product with no PackagingLink row owns its PackagingLine rows.
/// (Sub-assemblies are shared by linking the sub-assembly itself: see ProductSubAssemblyUsage.)
/// </summary>
public class PackagingLink
{
    public int Id { get; set; }

    /// <summary>The derived product — the one whose packaging reads through to LinkedProduct.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The product whose packaging this one shows, and stays in sync with.</summary>
    public int LinkedProductId { get; set; }

    public Product? LinkedProduct { get; set; }
}
