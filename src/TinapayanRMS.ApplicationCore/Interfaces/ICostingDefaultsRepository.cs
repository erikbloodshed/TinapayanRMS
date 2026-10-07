using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingDefaultsRepository
{
    /// <summary>
    /// The stage a new product goes in, and the percentages its costing, a new section's shrinkage and a new raw
    /// material's cost buffer start with: the stage CostingDefaults.DefaultNewProductStage (Production), the costing at
    /// zero, the shrinkage at CostingDefaults.DefaultShrinkagePercent and the buffer at
    /// CostingDefaults.DefaultCostBufferPercent until they're first saved.
    /// </summary>
    Task<CostingDefaults> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds or updates the one row from <paramref name="defaults"/>'s stage and percentages, refusing a negative one, a
    /// shrinkage of 100% or more, or a cost buffer over 100%. Saved products and materials are untouched.
    /// </summary>
    Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken);
}
