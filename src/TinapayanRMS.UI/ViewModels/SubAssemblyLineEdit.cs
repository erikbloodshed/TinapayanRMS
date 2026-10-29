using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>One ingredient line of a sub-assembly, saved or not: grams of an ingredient, costed at its buffered (or true) cost per gram.</summary>
public sealed class SubAssemblyLineEdit(RawMaterial ingredient, decimal grams, bool isFormulaBase = false, decimal? authoredBakersPercent = null)
{
    public RawMaterial Ingredient { get; } = ingredient;

    public decimal Grams { get; } = grams;

    /// <summary>Part of the formula's 100% base, which every line's baker's percentage is read against.</summary>
    public bool IsFormulaBase { get; } = isFormulaBase;

    /// <summary>The percentage typed for the line, or null when its grams were: whichever was typed is what stays put.</summary>
    public decimal? AuthoredBakersPercent { get; } = authoredBakersPercent;

    /// <summary>What a gram of the ingredient costs at <paramref name="basis"/>.</summary>
    public decimal CostPerGramAt(CostBasis basis) => RawMaterialRules.CostPerBaseUnitAt(Ingredient, basis);

    /// <summary>What this line adds to the batch's cost, at the ingredient's cost per gram at <paramref name="basis"/>.</summary>
    public decimal CostAt(CostBasis basis) => Grams * CostPerGramAt(basis);
}
