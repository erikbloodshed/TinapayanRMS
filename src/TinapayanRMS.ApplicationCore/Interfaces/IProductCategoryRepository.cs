using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductCategoryRepository
{
    Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(ProductCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(ProductCategory category, CancellationToken cancellationToken);

    /// <summary>The products in the category are left without one (a Set Null foreign key): a product's category is optional.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
