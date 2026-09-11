using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public static class IngredientRules
{
    /// <summary>
    /// A Purchased ingredient needs a purchase unit and a positive purchase quantity;
    /// its CurrentCost/CostPerGram come from price history (ApplyLatestPrice below),
    /// not from this check. A Made ingredient has none of those — its cost comes from
    /// a BOM instead (Phase 6).
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
        }
        else
        {
            if (ingredient.PurchaseUnitId is not null || ingredient.PurchaseUnitQty is not null || ingredient.CurrentCost is not null)
            {
                throw new InvalidOperationException("A made ingredient has no purchase unit, purchase quantity, or price.");
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
        ingredient.CostPerGram = latest is not null && ingredient.PurchaseUnitQty is > 0
            ? latest.Price / ingredient.PurchaseUnitQty.Value
            : 0m;
    }
}
