using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IIngredientRepository
{
    Task<IReadOnlyList<Ingredient>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(Ingredient ingredient, CancellationToken cancellationToken);

    Task UpdateAsync(Ingredient ingredient, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
