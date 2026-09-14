namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An in-house recipe (dough, a filling, a topping, …), made inside the product that owns it. Other
/// products can use it too: a ProductSubAssemblyUsage of another product's sub-assembly is a link.
/// </summary>
public class SubAssembly
{
    public int Id { get; set; }

    /// <summary>Unique within its owner: two products can each have a "Sweet Dough".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Required. Decides which section of every recipe using it the sub-assembly shows in.</summary>
    public int RoleId { get; set; }

    public SubAssemblyRole? Role { get; set; }

    /// <summary>The product it was made in. Only the owner edits its lines, and the owner always uses it.</summary>
    public int OwnerProductId { get; set; }

    public Product? OwnerProduct { get; set; }

    /// <summary>Stored, not computed: the sum of its lines' Grams. Refreshed whenever its lines are saved.</summary>
    public decimal TotalWeight { get; set; }

    /// <summary>Stored, not computed: the sum of (line Grams × that line's Ingredient.BufferedCostPerBaseUnit, its buffered cost per gram). Refreshed whenever its lines are saved.</summary>
    public decimal TotalCost { get; set; }
}
