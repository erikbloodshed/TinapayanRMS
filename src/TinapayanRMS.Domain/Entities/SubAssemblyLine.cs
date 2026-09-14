namespace TinapayanRMS.Domain.Entities;

/// <summary>One ingredient (a raw material of the Ingredient type) composing a sub-assembly, in grams.</summary>
public class SubAssemblyLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }
}
