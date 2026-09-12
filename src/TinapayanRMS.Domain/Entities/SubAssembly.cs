namespace TinapayanRMS.Domain.Entities;

/// <summary>An in-house recipe (dough, a filling, a topping, …) that products use directly.</summary>
public class SubAssembly
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Required, and — once Phase 8 adds ProductSubAssemblyUsage — only changeable
    /// while no product uses this sub-assembly (changing it would move the sub-assembly
    /// to a different tab in every product using it, and break that tab's links).
    /// </summary>
    public int RoleId { get; set; }

    public SubAssemblyRole? Role { get; set; }

    /// <summary>Stored, not computed: the sum of its lines' Grams. Refreshed whenever its lines are saved.</summary>
    public decimal TotalWeight { get; set; }

    /// <summary>Stored, not computed: the sum of (line Grams × that line's Ingredient.CostPerGram). Refreshed whenever its lines are saved.</summary>
    public decimal TotalCost { get; set; }
}
