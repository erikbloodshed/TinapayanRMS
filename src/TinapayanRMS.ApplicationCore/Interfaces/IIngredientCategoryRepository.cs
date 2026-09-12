using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientCategoryRepository
{
    Task<IReadOnlyList<IngredientCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(IngredientCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(IngredientCategory category, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
