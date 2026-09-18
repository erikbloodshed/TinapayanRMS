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
    /// A standalone sub-assembly's name, unique among standalone ones: products pick it to link or copy by it.
    /// Optional for a product's own, which goes by its role, and unique within that product: it tells apart several
    /// in one role, whose tabs read "Filling: Ube" and "Filling: Cheese" (SubAssemblyRules.EnsureSectionsDistinct).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Required. Decides which section of every recipe using it the sub-assembly shows in, and what that
    /// section is called, with its name beside it when the recipe has more than one in the role.
    /// </summary>
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
