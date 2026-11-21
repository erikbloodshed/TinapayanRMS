using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.QuickFind;

/// <summary>What Find (Ctrl+K) reads: every record it can open, by name.</summary>
public interface IQuickFindQueries
{
    /// <summary>
    /// Every raw material, standalone sub-assembly (a product's own is opened with its product) and product, read side by
    /// side, off the calling thread.
    /// </summary>
    Task<FindableRecords> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The records Find can open, with the category, or a sub-assembly's role, each is listed with.</summary>
public sealed record FindableRecords(IReadOnlyList<RawMaterial> RawMaterials, IReadOnlyList<SubAssembly> SubAssemblies, IReadOnlyList<Product> Products);

/// <inheritdoc cref="IQuickFindQueries"/>
public sealed class QuickFindQueries(
    IRawMaterialRepository rawMaterialRepository, ISubAssemblyRepository subAssemblyRepository, IProductRepository productRepository) : IQuickFindQueries
{
    public async Task<FindableRecords> ReadAsync(CancellationToken cancellationToken)
    {
        // Together, off the calling thread, as the screens read their lists.
        var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(cancellationToken), cancellationToken);
        var subAssembliesRead = Task.Run(() => subAssemblyRepository.GetStandaloneAsync(cancellationToken), cancellationToken);
        var productsRead = Task.Run(() => productRepository.GetAllAsync(cancellationToken), cancellationToken);
        await Task.WhenAll(rawMaterialsRead, subAssembliesRead, productsRead);
        return new FindableRecords(await rawMaterialsRead, await subAssembliesRead, await productsRead);
    }
}
