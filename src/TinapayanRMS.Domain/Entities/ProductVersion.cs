namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One version in a product's history: its recipe, packaging, costing and price as one Save left them, with the costs of
/// everything it used at that moment. Self-contained (<see cref="Content"/> points at nothing), so a material, sub-assembly
/// or role deleted since leaves it readable. Recorded only when the recipe or price differs from the version before.
/// </summary>
public class ProductVersion
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>1 for the product's first version, then 2, 3 …: what the History tab calls it.</summary>
    public int Number { get; set; }

    /// <summary>When the Save it records was made.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>What the user wrote about it ("Cheaper butter"), or null.</summary>
    public string? Note { get; set; }

    /// <summary>The snapshot, as JSON (ProductVersionContent in ApplicationCore).</summary>
    public string Content { get; set; } = string.Empty;
}
