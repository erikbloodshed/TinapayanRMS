using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ICostingDefaultsRepository
{
    /// <summary>
    /// The stage a new product goes in, the percentages its costing and a new raw material's cost buffer start with, and
    /// the shop-wide wastage every product's costing adds: the stage CostingDefaults.DefaultNewProductStage (Production),
    /// the costing at zero, the buffer at CostingDefaults.DefaultCostBufferPercent and the wastage at
    /// CostingDefaults.DefaultWastagePercent until they're first saved.
    /// </summary>
    Task<CostingDefaults> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds or updates the one row from <paramref name="defaults"/>'s stage and percentages, refusing a negative one, or a
    /// cost buffer or wastage over 100%. Saved products' costing and saved materials are untouched, but the wastage
    /// reprices every product, since they all read it.
    /// </summary>
    Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken);
}
