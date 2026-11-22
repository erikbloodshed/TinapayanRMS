namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A "Live Template" link for packaging: a product's packaging reads through to another product's, or to a
/// standalone packaging set (a sub-assembly in the Packaging role), instead of owning its own lines. Exactly one
/// of <see cref="LinkedProductId"/> and <see cref="LinkedSubAssemblyId"/> is set. A product with no PackagingLink
/// row owns its PackagingLine rows. A product has one link for its packaging per piece and one for its bulk packaging
/// (<see cref="IsBulk"/>) at most. (Recipe sub-assemblies are shared by linking the sub-assembly itself: see
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

    /// <summary>
    /// A link for the product's bulk packaging (PackagingLine.IsBulk): the set's lines are what one bulk pack takes, each
    /// quantity per pack, shared out among the units the product's pack holds (Product.BulkPackagingQuantity), which stays
    /// the product's own. Always to a set, never to another product's packaging (CK_PackagingLinks_BulkLinksASet), so
    /// it's in no chain. False for the link of its packaging per piece.
    /// </summary>
    public bool IsBulk { get; set; }
}
