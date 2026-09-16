namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// One ingredient line of a sub-assembly as a caller hands it over: the ingredient, its grams, and whether
/// it's part of the formula's 100% base (baker's percentage — see <c>SubAssemblyRules.CalculateBakersPercent</c>), and
/// the percentage typed for it when that, rather than its grams, is the figure the user gave.
/// <para>
/// The implicit conversion from a plain <c>(IngredientId, Grams)</c> tuple is what lets the many callers
/// that don't care about the base keep writing <c>[(flourId, 1000m)]</c>. It cuts both ways: a projection
/// that rebuilds lines as tuples compiles happily and silently drops the flag, so anything carrying lines
/// from one place to another has to name <see cref="IsFormulaBase"/> and <see cref="AuthoredBakersPercent"/> itself.
/// </para>
/// </summary>
public readonly record struct RecipeLine(int IngredientId, decimal Grams, bool IsFormulaBase = false, decimal? AuthoredBakersPercent = null)
{
    public static implicit operator RecipeLine((int IngredientId, decimal Grams) line) =>
        new(line.IngredientId, line.Grams);
}
