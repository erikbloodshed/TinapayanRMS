using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// The print preview's ticks, as they were last left: Include's Recipe and Costing, and Production copy. Kept for the
/// Windows user on this computer, with the lists' open folders (IViewStateStore), since one person prints for production
/// and another for the office. Recipe and Costing until they're changed, at least one of them always, as the preview can't
/// clear the last; Production copy off until it's ticked.
/// </summary>
public sealed class PrintIncludes(IViewStateStore viewState)
{
    /// <summary>The view-state key they're kept under: what's ticked, "recipe", "costing" and "production".</summary>
    public const string Key = "print-includes";

    private const string RecipePart = "recipe";
    private const string CostingPart = "costing";
    private const string ProductionPart = "production";

    public bool Recipe
    {
        get => Includes(RecipePart);
        set => Keep(value, Costing, ProductionCopy);
    }

    public bool Costing
    {
        get => Includes(CostingPart);
        set => Keep(Recipe, value, ProductionCopy);
    }

    /// <summary>The recipe alone, for the production floor, in large type with a box to tick beside each line.</summary>
    public bool ProductionCopy
    {
        get => viewState.ReadList(Key)?.Contains(ProductionPart) ?? false;
        set => Keep(Recipe, Costing, value);
    }

    // Nothing kept, or nothing that's a part (a file from a later version, or edited), is both.
    private bool Includes(string part) =>
        viewState.ReadList(Key) is { } parts && parts.Any(p => p is RecipePart or CostingPart) ? parts.Contains(part) : true;

    private void Keep(bool recipe, bool costing, bool production)
    {
        if (!recipe && !costing)
        {
            return;
        }

        List<string> parts = [];
        if (recipe)
        {
            parts.Add(RecipePart);
        }

        if (costing)
        {
            parts.Add(CostingPart);
        }

        if (production)
        {
            parts.Add(ProductionPart);
        }

        viewState.WriteList(Key, parts);
    }
}
