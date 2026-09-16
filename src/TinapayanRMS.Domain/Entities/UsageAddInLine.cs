namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One of a product's own extra ingredients — a colouring, a flavouring, an inclusion — on top of a
/// sub-assembly it links, in grams. It belongs to the usage, not to the sub-assembly: two products
/// linking the same dough each keep their own, and neither changes the dough itself.
/// <para>
/// Add-ins go into the batch: the usage's effective batch is the sub-assembly's plus these
/// (<c>ProductRules.CalculateEffectiveBatch</c>), so a piece takes its weight-per-piece share of the
/// whole thing. Only a linked usage has them — a product edits its own sub-assembly's lines directly.
/// </para>
/// </summary>
public class UsageAddInLine
{
    public int Id { get; set; }

    public int UsageId { get; set; }

    public ProductSubAssemblyUsage? Usage { get; set; }

    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }
}
