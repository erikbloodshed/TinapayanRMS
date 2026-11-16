using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>Comparing sections, which the screen's undo and refresh do through CollectionMerge.</summary>
public static class ProductUsageEditComparison
{
    /// <summary>Whether <paramref name="other"/> shows the same thing: the same role and names, piece size, and lines and add-ins at the same costs.</summary>
    public static bool ShowsSameAs(this ProductUsageEdit usage, ProductUsageEdit other) =>
        usage.SubAssemblyId == other.SubAssemblyId
        && usage.IsOwned == other.IsOwned
        && usage.IsReleasing == other.IsReleasing
        && usage.ToMadeIngredient == other.ToMadeIngredient
        && CollectionMerge.HasSameValues(usage.Role, other.Role)
        && usage.LinkedName == other.LinkedName
        && usage.LinkedOwner == other.LinkedOwner
        && usage.Name == other.Name
        && usage.AuthoredField == other.AuthoredField
        && usage.AuthoredValue == other.AuthoredValue
        && usage.AreLinkedLinesEdited == other.AreLinkedLinesEdited
        && usage.Lines.Count == other.Lines.Count
        && usage.Lines.Zip(other.Lines).All(pair =>
            pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent
            && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient))
        && usage.AddIns.Count == other.AddIns.Count
        && usage.AddIns.Zip(other.AddIns).All(pair => pair.First.Grams == pair.Second.Grams && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient));
}
