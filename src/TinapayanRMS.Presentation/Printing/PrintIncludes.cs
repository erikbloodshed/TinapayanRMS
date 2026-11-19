using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// The print preview's Include ticks, Recipe and Costing, as they were last left: kept for the Windows user on this
/// computer, with the lists' open folders (IViewStateStore), since one person prints for the kitchen and another for the
/// office. Both until they're changed; at least one always, as the preview can't clear the last.
/// </summary>
public sealed class PrintIncludes(IViewStateStore viewState)
{
    /// <summary>The view-state key they're kept under: the parts included, "recipe" and "costing".</summary>
    public const string Key = "print-includes";

    private const string RecipePart = "recipe";
    private const string CostingPart = "costing";

    public bool Recipe
    {
        get => Includes(RecipePart);
        set => Keep(value, Costing);
    }

    public bool Costing
    {
        get => Includes(CostingPart);
        set => Keep(Recipe, value);
    }

    // Nothing kept, or nothing that's a part (a file from a later version, or edited), is both.
    private bool Includes(string part) =>
        viewState.ReadList(Key) is { } parts && parts.Any(p => p is RecipePart or CostingPart) ? parts.Contains(part) : true;

    private void Keep(bool recipe, bool costing)
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

        viewState.WriteList(Key, parts);
    }
}
