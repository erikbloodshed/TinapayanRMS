namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One packaging material (a raw material of the Packaging type) in a packaging sub-assembly — a standalone set in
/// the Packaging role, such as a gift box with its ribbon and tag. A product whose packaging links the set takes one
/// set per piece, so these lines read as its per-piece packaging lines. Costed on read, like those.
/// </summary>
public sealed class SubAssemblyPackagingLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int PackagingMaterialId { get; set; }

    public RawMaterial? PackagingMaterial { get; set; }

    /// <summary>The unit this line's quantity is entered in: one of the material's own units, or null for its base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>How much of the material one set takes, in the line's unit: 1 box, 30 cm of ribbon, 1 roll.</summary>
    public decimal QtyPerSet { get; set; }

    /// <summary>
    /// Where the line sits in its set's table, from 0: the user orders the lines by dragging them, and every save
    /// writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }
}
