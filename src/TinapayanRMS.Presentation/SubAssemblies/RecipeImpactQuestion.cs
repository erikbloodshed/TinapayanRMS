using System.Globalization;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>The question a Sub-Assemblies Save asks before it changes what the products linking the recipe cost.</summary>
public static class RecipeImpactQuestion
{
    /// <summary>How many products the question names; the rest are counted.</summary>
    private const int Named = 8;

    /// <summary>
    /// "Saving Sweet Dough changes the base cost of 2 products, for one sellable unit: Cheese Roll: 18.48 → 18.90 (+0.42) …",
    /// naming the first eight, and saying when made ingredients made from it change too.
    /// </summary>
    public static string For(string subAssemblyName, IReadOnlyList<RecipeImpact> impacts, int madeIngredientCount)
    {
        var lines = impacts.Take(Named).Select(i => string.Create(
            CultureInfo.CurrentCulture, $"{i.ProductName}: {i.BaseCostBefore:N2} → {i.BaseCostAfter:N2} ({i.Change:+0.00;-0.00})"));
        var more = impacts.Count > Named ? $"\nand {impacts.Count - Named} more" : string.Empty;
        var made = madeIngredientCount > 0
            ? $"\n\nThe {(madeIngredientCount == 1 ? "made ingredient" : $"{madeIngredientCount} made ingredients")} made from it change too, and the products using them."
            : string.Empty;
        var what = impacts.Count == 1 ? "1 product" : $"{impacts.Count} products";
        return $"Saving {subAssemblyName} changes the base cost of {what}, for one sellable unit:\n\n{string.Join("\n", lines)}{more}{made}\n\nSave it?";
    }
}
