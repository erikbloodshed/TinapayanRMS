namespace TinapayanRMS.ApplicationCore;

/// <summary>
/// Read-only access to a simple lookup table (UnitOfMeasure, IngredientCategory,
/// PackagingCategory, ...) for populating pickers. One generic implementation in
/// Infrastructure covers every lookup type, since they all need the same query.
/// </summary>
public interface ILookupRepository<T> where T : class
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken);
}
