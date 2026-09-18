using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One packaging material (a raw material of the Packaging type) used by a product, for one finished
/// piece or for one of its packs. Which of the two it packs is <see cref="PackId"/>, and that is what
/// "unit" means in the two quantity fields below.
/// </summary>
public class PackagingLine
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>
    /// The pack this line packs, or null when it packs a single finished piece. A pack's box, label
    /// and ribbon belong to the pack; the wrapper around each piece doesn't.
    /// </summary>
    public int? PackId { get; set; }

    public ProductPack? Pack { get; set; }

    public int PackagingMaterialId { get; set; }

    public RawMaterial? PackagingMaterial { get; set; }

    /// <summary>The unit this line's quantities are entered in: one of the material's own units, or null for its base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>Amount of the packaging material, in the line's unit, used by one of what it packs: one piece, or one pack.</summary>
    public decimal QtyPerUnit { get; set; }

    /// <summary>Pieces — or packs — served by one of the line's unit of the packaging material, exactly 1 / QtyPerUnit.</summary>
    public decimal UnitsPerPackagingUnit { get; set; }

    /// <summary>Which of the two fields above the user typed; the other is recalculated from it on every edit.</summary>
    public PackagingLineAuthoredField AuthoredField { get; set; }
}
