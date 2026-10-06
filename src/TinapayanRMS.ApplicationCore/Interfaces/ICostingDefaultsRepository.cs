using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingDefaultsRepository
{
    /// <summary>
    /// The percentages a new product's costing and a new section's shrinkage start with: the costing at zero and the
    /// shrinkage at CostingDefaults.DefaultShrinkagePercent until they're first saved.
    /// </summary>
    Task<CostingDefaults> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds or updates the one row from <paramref name="defaults"/>'s percentages, refusing a negative one or a shrinkage
    /// of 100% or more. Saved products are untouched.
    /// </summary>
    Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken);
}
