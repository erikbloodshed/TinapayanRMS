namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One packaging material (a raw material of the Packaging type) in a packaging sub-assembly — a standalone set in
/// the Packaging role, such as a gift box with its ribbon and tag. A product whose packaging links the set takes one
/// set per piece, so these lines read as its per-piece packaging lines. Costed on read, like those.
/// </summary>
public class SubAssemblyPackagingLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int PackagingMaterialId { get; set; }

    public RawMaterial? PackagingMaterial { get; set; }

    /// <summary>How much of the material one set takes, in the material's base unit: 1 box, 30 cm of ribbon.</summary>
    public decimal QtyPerSet { get; set; }
}
