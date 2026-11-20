using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UseCases.RawMaterials;

/// <summary>What the Raw Materials screen reads.</summary>
public interface IRawMaterialQueries
{
    /// <summary>
    /// The categories, units of measure, materials, costing defaults and standalone sub-assemblies, and the products using
    /// each of <paramref name="dependentsFor"/>, read side by side.
    /// </summary>
    Task<RawMaterialsCatalog> ReadCatalogAsync(IReadOnlyCollection<int> dependentsFor, CancellationToken cancellationToken);

    /// <summary>What <paramref name="material"/> shows beyond the list's copy: a purchased one's prices, a made one's BOM, and the products using it.</summary>
    Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>A material as saved, or null when it's gone.</summary>
    Task<RawMaterial?> ReadAsync(int rawMaterialId, CancellationToken cancellationToken);

    /// <summary>A purchased material's price history, newest first.</summary>
    Task<IReadOnlyList<RawMaterialPriceHistory>> ReadPriceHistoryAsync(int rawMaterialId, CancellationToken cancellationToken);

    /// <summary>A sub-assembly's lines as saved, which a made ingredient made from it shows.</summary>
    Task<IReadOnlyList<SubAssemblyLine>> ReadSubAssemblyLinesAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>
    /// What the material makes up of each of <paramref name="productIds"/>' material cost, from 0 to 1 (MaterialCostShares);
    /// null where nothing works the shares out.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>?> ReadCostSharesAsync(int rawMaterialId, IReadOnlyList<int> productIds, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IRawMaterialQueries"/>
/// <param name="costShares">What the Dependents tiles' shares are worked out by; none, and they aren't.</param>
public sealed class RawMaterialQueries(
    IRawMaterialRepository rawMaterialRepository,
    IRawMaterialPriceHistoryRepository priceHistoryRepository,
    IRawMaterialBrandPriceRepository brandPriceRepository,
    IIngredientBomLineRepository bomLineRepository,
    ISubAssemblyRepository subAssemblyRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository,
    IRawMaterialCategoryRepository categoryRepository,
    IUnitOfMeasureRepository unitOfMeasureRepository,
    ICostingDefaultsRepository costingDefaultsRepository,
    MaterialCostShares? costShares = null) : IRawMaterialQueries
{
    public async Task<RawMaterialsCatalog> ReadCatalogAsync(IReadOnlyCollection<int> dependentsFor, CancellationToken cancellationToken)
    {
        // Side by side, each on the thread pool from its start: each read makes its own short-lived context.
        var categories = Task.Run(() => categoryRepository.GetAllAsync(cancellationToken), cancellationToken);
        var units = Task.Run(() => unitOfMeasureRepository.GetAllAsync(cancellationToken), cancellationToken);
        var rawMaterials = Task.Run(() => rawMaterialRepository.GetAllAsync(cancellationToken), cancellationToken);
        var costingDefaults = Task.Run(() => costingDefaultsRepository.GetAsync(cancellationToken), cancellationToken);
        var subAssemblies = Task.Run(() => subAssemblyRepository.GetStandaloneAsync(cancellationToken), cancellationToken);
        var dependents = dependentsFor
            .Distinct()
            .ToDictionary(id => id, id => Task.Run(() => rawMaterialRepository.GetDependentsAsync(id, cancellationToken), cancellationToken));
        await Task.WhenAll([categories, units, rawMaterials, costingDefaults, subAssemblies, .. dependents.Values]);

        Dictionary<int, IReadOnlyList<LinkingProduct>> dependentsRead = [];
        foreach (var (id, read) in dependents)
        {
            dependentsRead[id] = await read;
        }

        return new RawMaterialsCatalog(await categories, await units, await rawMaterials, await costingDefaults, await subAssemblies, dependentsRead);
    }

    public async Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        // A purchased material has prices, a made one a BOM, and a linked one neither; any of them can be used by products.
        var dependents = await rawMaterialRepository.GetDependentsAsync(material.Id, cancellationToken);
        return material.Source switch
        {
            RawMaterialSource.Purchased => new RawMaterialDetail(
                await priceHistoryRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                await brandPriceRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                [],
                dependents),
            RawMaterialSource.Made => new RawMaterialDetail([], [], await bomLineRepository.GetForIngredientAsync(material.Id, cancellationToken), dependents),
            _ => RawMaterialDetail.None with { Dependents = dependents },
        };
    }

    public Task<RawMaterial?> ReadAsync(int rawMaterialId, CancellationToken cancellationToken) =>
        rawMaterialRepository.GetByIdAsync(rawMaterialId, cancellationToken);

    public Task<IReadOnlyList<RawMaterialPriceHistory>> ReadPriceHistoryAsync(int rawMaterialId, CancellationToken cancellationToken) =>
        priceHistoryRepository.GetForRawMaterialAsync(rawMaterialId, cancellationToken);

    public Task<IReadOnlyList<SubAssemblyLine>> ReadSubAssemblyLinesAsync(int subAssemblyId, CancellationToken cancellationToken) =>
        subAssemblyLineRepository.GetForSubAssembliesAsync([subAssemblyId], cancellationToken);

    public async Task<IReadOnlyDictionary<int, decimal>?> ReadCostSharesAsync(int rawMaterialId, IReadOnlyList<int> productIds, CancellationToken cancellationToken) =>
        costShares is null ? null : await costShares.ReadAsync(rawMaterialId, productIds, cancellationToken);
}
