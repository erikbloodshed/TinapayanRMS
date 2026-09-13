using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class IngredientRules
{
    /// <summary>
    /// A Purchased ingredient has no weight loss percent; its CostPerGram comes from price
    /// history (ApplyLatestPrice below). A Made ingredient needs a weight loss percent — its
    /// cost comes from a BOM instead (IngredientBomRules). Either kind can have alternative
    /// units (IngredientUnit); only a purchased one is ever priced in them.
    /// </summary>
    public static void EnsureFieldsMatchSource(Ingredient ingredient)
    {
        if (ingredient.Source == IngredientSource.Purchased)
        {
            if (ingredient.WeightLossPercent is not null)
            {
                throw new InvalidOperationException("A purchased ingredient has no weight loss percent.");
            }
        }
        else
        {
            if (ingredient.WeightLossPercent is not { } weightLossPercent)
            {
                throw new InvalidOperationException("A made ingredient needs a weight loss percent.");
            }

            if (weightLossPercent is < 0 or >= 100)
            {
                throw new InvalidOperationException("Weight loss percent must be at least 0 and less than 100.");
            }
        }
    }

    /// <summary>An ingredient's source is set at creation and can never change afterwards.</summary>
    public static void EnsureSourceUnchanged(IngredientSource original, IngredientSource updated)
    {
        if (original != updated)
        {
            throw new InvalidOperationException("An ingredient's source can't change after it's created.");
        }
    }

    /// <summary>
    /// Recomputes CostPerGram from the latest price history entry: its price divided by the
    /// grams in the unit it was priced in (<paramref name="gramsPerUnit"/>, null for a price per
    /// gram). No entry (<paramref name="latestPrice"/> null) reverts the ingredient to unpriced.
    /// Call after every add/update/delete of a price entry, and after the latest entry's unit
    /// changes its multiplier.
    /// </summary>
    public static void ApplyLatestPrice(Ingredient ingredient, decimal? latestPrice, decimal? gramsPerUnit)
    {
        ingredient.CostPerGram = latestPrice is { } price
            ? UnitConversionRules.CostPerBaseUnit(price, gramsPerUnit)
            : 0m;
    }
}
