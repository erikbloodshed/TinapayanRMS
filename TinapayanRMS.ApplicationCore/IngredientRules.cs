using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public static class IngredientRules
{
    /// <summary>
    /// A Purchased ingredient needs a purchase unit and a positive purchase quantity,
    /// and has no price yet until Phase 5 adds one. A Made ingredient has none of those
    /// — its cost comes from a BOM (Phase 6) instead.
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
}
