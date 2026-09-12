using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task UpdateAsync(Product product, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
