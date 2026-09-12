using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IIngredientCategoryRepository
{
    Task<IReadOnlyList<IngredientCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(IngredientCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(IngredientCategory category, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
