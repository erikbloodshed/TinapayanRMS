using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientRepository
{
    Task<IReadOnlyList<Ingredient>> GetAllAsync(CancellationToken cancellationToken);

    Task<Ingredient?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(Ingredient ingredient, CancellationToken cancellationToken);

    Task UpdateAsync(Ingredient ingredient, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
