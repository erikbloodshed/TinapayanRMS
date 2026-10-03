using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingParametersRepository
{
    /// <summary>Null until the shop owner has entered costing parameters for this product.</summary>
    Task<CostingParameters?> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>GetForProductAsync for several products at once, in one query: the rows there are, one at most for each.</summary>
    Task<IReadOnlyList<CostingParameters>> GetForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>Adds or updates the product's one CostingParameters row (a 1-to-1 settings record, not a list).</summary>
    Task SetAsync(
        int productId, decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent, decimal vatPercent, CancellationToken cancellationToken);
}
