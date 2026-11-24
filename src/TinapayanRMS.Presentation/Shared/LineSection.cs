using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// Another section of the product's recipe as a line of the one the form edits can use it (UsageSectionLine): which it is,
/// by its key in the form (ProductUsageEdit.Key), what it's called, and what a gram of its batch costs, as the recipe prices
/// it now (ProductRecipeDraft.Priced).
/// </summary>
public sealed record LineSection(long Key, string Name, decimal BufferedCostPerGram, decimal UnbufferedCostPerGram)
{
    /// <summary>What a gram of it costs at <paramref name="basis"/>.</summary>
    public decimal CostPerGramAt(CostBasis basis) => basis == CostBasis.Unbuffered ? UnbufferedCostPerGram : BufferedCostPerGram;

    /// <summary>What a line of it reads as, in the ingredients' place: "Sponge (section)".</summary>
    public string LineName => $"{Name} (section)";
}

/// <summary>
/// What a line of a product's section can be, as its picker lists it (ProductUsageForm.FormLinePicks): an ingredient, or
/// another of the recipe's sections. Two are the same pick when they're the same ingredient or the same section, whatever
/// either costs, so the picker's list isn't rebuilt by a reprice.
/// </summary>
public sealed record LinePick(string Name, RawMaterial? Ingredient, LineSection? Section)
{
    public static LinePick Of(RawMaterial ingredient) => new(ingredient.Name, ingredient, null);

    public static LinePick Of(LineSection section) => new(section.LineName, null, section);

    public bool Equals(LinePick? other) =>
        other is not null && Ingredient?.Id == other.Ingredient?.Id && Section?.Key == other.Section?.Key;

    public override int GetHashCode() => HashCode.Combine(Ingredient?.Id, Section?.Key);
}
