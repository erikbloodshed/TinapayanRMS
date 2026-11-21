using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// The print preview's ticks, as they were last left: Include's Recipe, Costing and Sub-assembly total, and Production
/// copy. Kept for the Windows user on this computer, with the lists' open folders (IViewStateStore), since one person
/// prints for production and another for the office. Recipe and Costing until they're changed, at least one of them
/// always, as the preview can't clear the last; Sub-assembly total and Production copy off until they're ticked.
/// Sub-assembly total is the recipe with only its sub-assembly costs, in place of the costing, so ticking it ticks Recipe
/// and clears Costing, and ticking Costing clears it.
/// </summary>
public sealed class PrintIncludes(IViewStateStore viewState)
{
    /// <summary>The view-state key they're kept under: what's ticked, "recipe", "costing", "sub-assembly-total" and "production".</summary>
    public const string Key = "print-includes";

    private const string RecipePart = "recipe";
    private const string CostingPart = "costing";
    private const string SubAssemblyTotalPart = "sub-assembly-total";
    private const string ProductionPart = "production";

    public bool Recipe
    {
        get => Includes(RecipePart);
        set => Keep(value, Costing, SubAssemblyTotal, ProductionCopy);
    }

    public bool Costing
    {
        get => Includes(CostingPart);
        set => Keep(Recipe, value, SubAssemblyTotal && !value, ProductionCopy);
    }

    /// <summary>
    /// The recipe with each sub-assembly's batch cost under its table and the total sub-assembly cost at the end, and no
    /// other cost: instead of the costing.
    /// </summary>
    public bool SubAssemblyTotal
    {
        get => Ticked(SubAssemblyTotalPart) && !Costing;
        set => Keep(Recipe || value, Costing && !value, value, ProductionCopy);
    }

    /// <summary>The recipe alone, for the production floor, in large type with a box to tick beside each line.</summary>
    public bool ProductionCopy
    {
        get => Ticked(ProductionPart);
        set => Keep(Recipe, Costing, SubAssemblyTotal, value);
    }

    private bool Ticked(string part) => viewState.ReadList(Key)?.Contains(part) ?? false;

    // Nothing kept, or nothing that's a part (a file from a later version, or edited), is both.
    private bool Includes(string part) =>
        viewState.ReadList(Key) is { } parts && parts.Any(p => p is RecipePart or CostingPart) ? parts.Contains(part) : true;

    private void Keep(bool recipe, bool costing, bool subAssemblyTotal, bool production)
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

        if (subAssemblyTotal)
        {
            parts.Add(SubAssemblyTotalPart);
        }

        if (production)
        {
            parts.Add(ProductionPart);
        }

        viewState.WriteList(Key, parts);
    }
}
