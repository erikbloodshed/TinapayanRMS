namespace TinapayanRMS.ApplicationCore;

/// <summary>
/// Read-only access to a simple, genuinely un-editable lookup table (UnitOfMeasure)
/// for populating pickers. One generic implementation in Infrastructure covers every
/// such lookup type, since they all need the same query. A lookup that's user-editable
/// (IngredientCategory, PackagingCategory, SubAssemblyRole) gets its own dedicated
/// repository with full CRUD instead — don't reach for this one just because a table
/// is small.
/// </summary>
public interface ILookupRepository<T> where T : class
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken);
}
