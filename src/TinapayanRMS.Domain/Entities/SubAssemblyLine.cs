namespace TinapayanRMS.Domain.Entities;

/// <summary>One ingredient (a raw material of the Ingredient type) composing a sub-assembly, in grams.</summary>
public class SubAssemblyLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }

    /// <summary>
    /// Part of the formula's 100% base, for baker's percentage: every line is read as a percentage of the
    /// ticked lines' total weight (SubAssemblyRules.CalculateBakersPercent). Usually the flour, and usually
    /// more than one — two flours tick together and make 100% between them. It's per formula, not per
    /// ingredient: sugar is the base of a syrup and an extra in a dough. None ticked is fine; a glaze or a
    /// filling has no meaningful base, and then no line has a percentage.
    /// </summary>
    public bool IsFormulaBase { get; set; }

    /// <summary>
    /// The baker's percentage typed for this line, or null when its grams were typed — which is always the case for a
    /// base line, whose share is 100% between them by definition. A typed percentage is what stays put: when the base
    /// weight changes, the line's grams are worked out again from it (SubAssemblyRules.GramsFromBakersPercent), the way
    /// a usage's typed yield or weight per piece stays put. Grams are still stored either way, and are what everything
    /// is costed from; this only says which of the two the user gave.
    /// </summary>
    public decimal? AuthoredBakersPercent { get; set; }

    /// <summary>
    /// Where the line sits in its sub-assembly's table, from 0: the user orders the lines by dragging them, and every save
    /// writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }
}
