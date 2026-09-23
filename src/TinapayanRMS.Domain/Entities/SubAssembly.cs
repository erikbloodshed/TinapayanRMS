namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An in-house recipe (dough, a filling, a topping, …), or a packaging set. Either kept on the Sub-Assemblies screen
/// and edited only there, which every product using it links through a ProductSubAssemblyUsage; or, in a role that's
/// product-owned (SubAssemblyRole.IsProductOwned), or unlinked in a product in any role, the own of that product
/// (<see cref="OwnerProductId"/>), edited in its recipe, which other products can copy but not link. Either way one
/// first written in a product's Recipe Structure is named after that product and its role.
/// </summary>
public class SubAssembly
{
    public int Id { get; set; }

    /// <summary>
    /// Required and unique (case- and space-insensitively): products pick it to link or copy by it, and a recipe with
    /// more than one in a role tells them apart by it (SubAssemblyRules.EnsureSectionsDistinct).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Required. Decides which section of every recipe using it the sub-assembly shows in, and what that
    /// section is called, with its name beside it when the recipe has more than one in the role.
    /// </summary>
    public int RoleId { get; set; }

    public SubAssemblyRole? Role { get; set; }

    /// <summary>
    /// The product it's the own of, which alone uses it and edits its lines, and which takes it with it when deleted; or
    /// null for one kept on the Sub-Assemblies screen. Set for a section made in a product-owned role, or for a linked
    /// one a product unlinks into a copy of its own, in any role.
    /// </summary>
    public int? OwnerProductId { get; set; }

    public Product? OwnerProduct { get; set; }

    /// <summary>Stored, not computed: the sum of its lines' Grams. Refreshed whenever its lines are saved.</summary>
    public decimal TotalWeight { get; set; }

    /// <summary>Stored, not computed: the sum of (line Grams × that line's Ingredient.BufferedCostPerBaseUnit, its buffered cost per gram). Refreshed whenever its lines are saved.</summary>
    public decimal TotalCost { get; set; }
}
