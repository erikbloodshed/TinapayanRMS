namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A "Live Template" link for packaging: a product's packaging reads through to another product's, or to a
/// standalone packaging set (a sub-assembly in the Packaging role), instead of owning its own lines. Exactly one
/// of <see cref="LinkedProductId"/> and <see cref="LinkedSubAssemblyId"/> is set. A product with no PackagingLink
/// row owns its PackagingLine rows. (Recipe sub-assemblies are shared by linking the sub-assembly itself: see
/// ProductSubAssemblyUsage.)
/// </summary>
public sealed class PackagingLink
{
    public int Id { get; set; }

    /// <summary>The derived product — the one whose packaging reads through to LinkedProduct.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The product whose packaging this one shows, and stays in sync with; null for a link to a set.</summary>
    public int? LinkedProductId { get; set; }

    public Product? LinkedProduct { get; set; }

    /// <summary>
    /// The standalone packaging set this one shows, one set per piece, and stays in sync with; null for a link to a
    /// product.
    /// </summary>
    public int? LinkedSubAssemblyId { get; set; }

    public SubAssembly? LinkedSubAssembly { get; set; }
}
