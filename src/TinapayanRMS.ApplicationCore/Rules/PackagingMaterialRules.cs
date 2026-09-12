using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class PackagingMaterialRules
{
    /// <summary>
    /// Recomputes CurrentCost from the latest price history entry (null once the
    /// last entry is deleted, reverting the material to unpriced). Call after every
    /// add/update/delete of one of the material's price entries.
    /// </summary>
    public static void ApplyLatestPrice(PackagingMaterial packagingMaterial, PackagingPriceHistory? latest)
    {
        packagingMaterial.CurrentCost = latest?.Price ?? 0m;
    }
}
