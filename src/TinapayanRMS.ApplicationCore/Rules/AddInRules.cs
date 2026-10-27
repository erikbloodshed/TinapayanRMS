namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// What every set of add-ins must be, a product usage's (ProductRules.EnsureValidAddIns) or a made ingredient's
/// (IngredientBomRules.EnsureValidAddIns): extra ingredients on top of a sub-assembly's recipe, so an empty set is fine,
/// but each needs positive grams, comes once, and is something the recipe doesn't already have.
/// </summary>
internal static class AddInRules
{
    /// <param name="baseIngredientIds">The sub-assembly's own lines' ingredients.</param>
    /// <param name="alreadyThereMessage">The refusal for an add-in the recipe already has, given that ingredient's name.</param>
    public static void EnsureValid(
        IReadOnlyList<(int IngredientId, decimal Grams, string IngredientName)> addIns,
        IReadOnlyCollection<int> baseIngredientIds,
        Func<string, string> alreadyThereMessage)
    {
        if (addIns.Any(a => a.Grams <= 0))
        {
            throw new BusinessRuleException("An add-in's grams must be positive.");
        }

        var duplicate = addIns.GroupBy(a => a.IngredientId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleException(
                $"{duplicate.First().IngredientName} is on two add-in lines. Put its total on one line and delete the other.");
        }

        var alreadyThere = addIns.Where(a => baseIngredientIds.Contains(a.IngredientId)).Select(a => a.IngredientName).FirstOrDefault();
        if (alreadyThere is not null)
        {
            throw new BusinessRuleException(alreadyThereMessage(alreadyThere));
        }
    }
}
