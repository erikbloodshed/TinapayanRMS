using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public static class IngredientRules
{
    /// <summary>
    /// A Purchased ingredient needs a purchase unit and a positive purchase quantity,
    /// and no weight loss percent; its CurrentCost/CostPerGram come from price history
    /// (ApplyLatestPrice below), not from this check. A Made ingredient has none of the
    /// purchase fields, and needs a weight loss percent — its cost comes from a BOM
    /// instead (IngredientBomRules).
    /// </summary>
    public static void EnsureFieldsMatchSource(Ingredient ingredient)
    {
        if (ingredient.Source == IngredientSource.Purchased)
        {
            if (ingredient.PurchaseUnitId is null)
            {
                throw new InvalidOperationException("A purchased ingredient needs a purchase unit.");
            }

            if (ingredient.PurchaseUnitQty is not > 0)
            {
                throw new InvalidOperationException("A purchased ingredient needs a positive purchase quantity.");
            }

            if (ingredient.WeightLossPercent is not null)
            {
                throw new InvalidOperationException("A purchased ingredient has no weight loss percent.");
            }
        }
        else
        {
            if (ingredient.PurchaseUnitId is not null || ingredient.PurchaseUnitQty is not null || ingredient.CurrentCost is not null)
            {
                throw new InvalidOperationException("A made ingredient has no purchase unit, purchase quantity, or price.");
            }

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
    /// Recomputes CurrentCost and CostPerGram from the latest price history entry
    /// (null once the last entry is deleted, reverting the ingredient to unpriced).
    /// Call after every add/update/delete of one of the ingredient's price entries.
    /// </summary>
    public static void ApplyLatestPrice(Ingredient ingredient, IngredientPriceHistory? latest)
    {
        ingredient.CurrentCost = latest?.Price;
        RecomputeCostPerGramFromCurrentCost(ingredient);
    }

    /// <summary>
    /// Purchased only: CostPerGram = CurrentCost / PurchaseUnitQty. Call whenever
    /// PurchaseUnitQty changes on its own (no new price entry) — e.g. correcting a
    /// mistyped sack size — since CostPerGram would otherwise stay stale until the
    /// next price entry recomputes it via ApplyLatestPrice.
    /// </summary>
    public static void RecomputeCostPerGramFromCurrentCost(Ingredient ingredient)
    {
        ingredient.CostPerGram = ingredient.CurrentCost is > 0 && ingredient.PurchaseUnitQty is > 0
            ? ingredient.CurrentCost.Value / ingredient.PurchaseUnitQty.Value
            : 0m;
    }
}
