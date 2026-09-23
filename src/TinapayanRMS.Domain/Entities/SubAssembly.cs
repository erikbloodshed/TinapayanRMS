namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An in-house recipe (dough, a filling, a topping, …), or a packaging set, kept on the Sub-Assemblies screen and
/// edited only there. Every product using one links it through a ProductSubAssemblyUsage: one first written in a
/// product's Recipe Structure is saved here, named after that product and its role, and linked by it.
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

    /// <summary>Stored, not computed: the sum of its lines' Grams. Refreshed whenever its lines are saved.</summary>
    public decimal TotalWeight { get; set; }

    /// <summary>Stored, not computed: the sum of (line Grams × that line's Ingredient.BufferedCostPerBaseUnit, its buffered cost per gram). Refreshed whenever its lines are saved.</summary>
    public decimal TotalCost { get; set; }
}
