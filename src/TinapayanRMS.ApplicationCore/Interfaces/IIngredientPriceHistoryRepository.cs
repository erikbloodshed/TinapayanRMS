using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IIngredientPriceHistoryRepository
{
    Task<IReadOnlyList<IngredientPriceHistory>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    Task AddAsync(IngredientPriceHistory entry, CancellationToken cancellationToken);

    Task UpdateAsync(IngredientPriceHistory entry, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
