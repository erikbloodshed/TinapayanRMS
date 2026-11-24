using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the section form (<see cref="ProductUsageForm"/>) shows worked out from its tables and its piece size: the batch's
/// weight, which figure stays put, what a piece and the batch cost, and the tables' captions. Worked out together, when read
/// (the form's Derived).
/// </summary>
internal sealed record UsageFormFigures(
    string TotalWeightText, string PieceSizeCaption, string BaseCaption, string AddInsCaption, string CostPerPieceText, string BatchCostText)
{
    /// <summary>
    /// The figures of a form whose tables come to <paramref name="batch"/> (its lines and add-ins, each as grams and cost per
    /// gram), <paramref name="addInWeight"/> of it add-ins, cut into pieces by the figure typed, with <paramref name="formulaBase"/>
    /// the grams its ticked lines weigh. <paramref name="recipeName"/> is what the add-ins caption calls the recipe.
    /// <paramref name="used"/> is what the recipe's other sections use of it on their lines, and their names: its pieces come
    /// of what's left.
    /// </summary>
    public static UsageFormFigures Of(
        IReadOnlyList<(decimal Grams, decimal CostPerGram)> batch,
        decimal addInWeight,
        (decimal Grams, IReadOnlyList<string> Users) used,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        decimal formulaBase,
        string recipeName)
    {
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(batch);
        var left = Math.Max(totalWeight - used.Grams, 0m);
        var (weightPerPiece, _) = SectionFigures.PieceSizeOfWhatsLeft(authoredField, authoredValue, totalWeight, used.Grams);
        var users = string.Join(" and ", used.Users);
        var use = used.Users.Count == 1 ? "uses" : "use";

        // Neither figure is needed to put the section in (ProductRules.EnsureValidUsages), so with none typed the
        // caption says what that costs: nothing per piece, until one is. A yield of 1 is what a section in a
        // product-owned role starts at (StartingPieceSize), so it says how to size a bigger batch. One the recipe's other
        // sections use needs none: they cost it, and its pieces would only be of what they leave.
        var pieceSizeCaption = used.Grams > 0 && authoredValue <= 0
            ? $"{users} {use} {used.Grams:#,0.##} g of it. For pieces of the {left:#,0.##} g left, type the weight per piece or the yield."
            : used.Grams > 0 && authoredField == UsageAuthoredField.Yield
            ? $"The yield stays at {authoredValue:#,0.##} {(authoredValue == 1 ? "piece" : "pieces")} of the {left:#,0.##} g that {users} leave{(used.Users.Count == 1 ? "s" : "")}, if the batch changes."
            : used.Grams > 0
            ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes. {users} {use} {used.Grams:#,0.##} g of it."
            : authoredValue <= 0
            ? "Type the weight per piece or the yield; the other is worked out. Until then this section costs nothing per piece."
            : authoredField == UsageAuthoredField.Yield && authoredValue == 1
                ? "One piece takes the whole batch, and still does if the batch changes. For a batch that makes more, type the weight per piece or the yield."
            : authoredField == UsageAuthoredField.WeightPerPiece
                ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes."
                : $"The yield stays at {authoredValue:#,0.##} pieces if the batch changes.";

        // No pieces yet shows as a dash rather than 0.00, which would read as a section that costs nothing.
        var costPerPiece = ProductRules.HasPieces(totalWeight, weightPerPiece)
            ? ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight)
            : (decimal?)null;

        var baseCaption = formulaBase > 0
            ? $"Base {formulaBase:#,0.##} g · whole formula {totalWeight / formulaBase * 100m:#,0.#}%"
            : SubAssemblyBatch.NoBaseCaption;

        // The arithmetic of the two tables, in words: the recipe's grams, this product's on top, the batch.
        var baseWeight = totalWeight - addInWeight;
        var addInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {totalWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";

        return new UsageFormFigures(
            $"{totalWeight:#,0.##} g",
            pieceSizeCaption,
            baseCaption,
            addInsCaption,
            // Two decimals, as Batch cost beside it; packaging's, above, keeps four.
            costPerPiece is { } figure ? $"{figure:N2}" : "—",
            batch.Count > 0 ? $"{totalCost:N2}" : "—");
    }
}
