using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
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
    /// <paramref name="fold"/> is what goes into it whole from the recipe's other sections, its weight and cost, in its batch
    /// too, and the name of the one it goes into whole, if it does: it has no pieces of its own then.
    /// </summary>
    public static UsageFormFigures Of(
        IReadOnlyList<(decimal Grams, decimal CostPerGram)> batch,
        decimal addInWeight,
        (decimal Weight, decimal Cost, string? GoesIntoName) fold,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        decimal formulaBase,
        string recipeName)
    {
        var (linesWeight, linesCost) = SubAssemblyRules.CalculateTotals(batch);
        var (totalWeight, totalCost) = (linesWeight + fold.Weight, linesCost + fold.Cost);
        var (weightPerPiece, _) = fold.GoesIntoName is null ? ProductRules.ResolvePieceSize(authoredField, authoredValue, totalWeight) : (0m, 0m);

        // Neither figure is needed to put the section in (ProductRules.EnsureValidUsages), so with none typed the
        // caption says what that costs: nothing per piece, until one is. A yield of 1 is what a section in a
        // product-owned role starts at (StartingPieceSize), so it says how to size a bigger batch.
        var pieceSizeCaption = fold.GoesIntoName is { } into
            ? $"Its whole batch, {totalWeight:#,0.##} g, goes into {into}, whose weight per piece or yield shares it out. It has no pieces of its own."
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
        var baseWeight = linesWeight - addInWeight;
        var addInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {linesWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";

        return new UsageFormFigures(
            $"{totalWeight:#,0.##} g",
            pieceSizeCaption,
            baseCaption,
            addInsCaption,
            // Two decimals, as Batch cost beside it; packaging's, above, keeps four.
            costPerPiece is { } figure ? $"{figure:N2}" : "—",
            batch.Count > 0 || fold.Weight > 0 ? $"{totalCost:N2}" : "—");
    }
}
