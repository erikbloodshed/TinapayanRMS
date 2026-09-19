using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingDefaultsRepository
{
    /// <summary>The percentages a new product's costing starts with: all zero until they're first saved.</summary>
    Task<CostingDefaults> GetAsync(CancellationToken cancellationToken);

    /// <summary>Adds or updates the one row from <paramref name="defaults"/>'s percentages, refusing a negative one. Saved products are untouched.</summary>
    Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken);
}
