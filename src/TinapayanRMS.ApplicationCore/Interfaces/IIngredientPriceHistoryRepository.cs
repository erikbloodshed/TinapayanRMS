using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientPriceHistoryRepository
{
    Task<IReadOnlyList<IngredientPriceHistory>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    Task AddAsync(IngredientPriceHistory entry, CancellationToken cancellationToken);

    Task UpdateAsync(IngredientPriceHistory entry, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
