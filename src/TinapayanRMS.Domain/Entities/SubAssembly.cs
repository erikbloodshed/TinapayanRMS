namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An in-house recipe (dough, a filling, a topping, …), made inside the product that owns it, or on its
/// own (standalone) on the Sub-Assemblies screen. Other products can use it too: a
/// ProductSubAssemblyUsage of a sub-assembly the product doesn't own is a link.
/// </summary>
public class SubAssembly
{
    public int Id { get; set; }

    /// <summary>
    /// Unique within its owner, and among standalone sub-assemblies: two products can each have a
    /// "Sweet Dough", and so can one product and the standalone list.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Required. Decides which section of every recipe using it the sub-assembly shows in.</summary>
    public int RoleId { get; set; }

    public SubAssemblyRole? Role { get; set; }

    /// <summary>
    /// The product it was made in, which alone edits its lines and always uses it; or null for a
    /// standalone sub-assembly, edited on the Sub-Assemblies screen, which every product that uses it links.
    /// </summary>
    public int? OwnerProductId { get; set; }

    public Product? OwnerProduct { get; set; }

    /// <summary>Stored, not computed: the sum of its lines' Grams. Refreshed whenever its lines are saved.</summary>
    public decimal TotalWeight { get; set; }

    /// <summary>Stored, not computed: the sum of (line Grams × that line's Ingredient.BufferedCostPerBaseUnit, its buffered cost per gram). Refreshed whenever its lines are saved.</summary>
    public decimal TotalCost { get; set; }
}
