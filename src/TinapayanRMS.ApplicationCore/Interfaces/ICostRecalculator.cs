namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Works every stored cost that's derived from others out again: each made and linked ingredient's costs, in dependency
/// order, then each sub-assembly's totals. Every save keeps them right as it goes, in its own transaction; this is for
/// after the database was changed outside the app, such as by a script. What changes is saved in one transaction.
/// </summary>
public interface ICostRecalculator
{
    Task<CostRecalculation> RecalculateAllAsync(CancellationToken cancellationToken);
}

/// <summary>How many made or linked ingredients, and how many sub-assemblies, had a stored cost that was wrong and was put right.</summary>
public sealed record CostRecalculation(int IngredientsCorrected, int SubAssembliesCorrected)
{
    public bool CorrectedAny => IngredientsCorrected > 0 || SubAssembliesCorrected > 0;
}
