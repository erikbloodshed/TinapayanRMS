namespace TinapayanRMS.Domain.Entities;

/// <summary>One ingredient composing a sub-assembly.</summary>
public class SubAssemblyLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    public decimal Grams { get; set; }
}
