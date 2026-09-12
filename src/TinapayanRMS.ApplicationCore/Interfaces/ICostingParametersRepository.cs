using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingParametersRepository
{
    /// <summary>Null until the shop owner has entered costing parameters for this product.</summary>
    Task<CostingParameters?> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Adds or updates the product's one CostingParameters row (a 1-to-1 settings record, not a list).</summary>
    Task SetAsync(int productId, decimal laborCost, decimal overheadCost, decimal marginPercent, decimal vatPercent, CancellationToken cancellationToken);
}
