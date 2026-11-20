using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <inheritdoc cref="ISubAssemblyQueries"/>
public sealed class SubAssemblyQueries(
    ISubAssemblyRepository subAssemblyRepository,
    ISubAssemblyLineRepository lineRepository,
    ISubAssemblyRoleRepository roleRepository,
    IRawMaterialRepository rawMaterialRepository,
    IRawMaterialCategoryRepository rawMaterialCategoryRepository) : ISubAssemblyQueries
{
    public async Task<SubAssembliesCatalog> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        // Side by side: each read makes its own short-lived context.
        var roles = roleRepository.GetAllAsync(cancellationToken);
        var rawMaterials = rawMaterialRepository.GetAllAsync(cancellationToken);
        var subAssemblies = subAssemblyRepository.GetStandaloneAsync(cancellationToken);
        var linkedBy = subAssemblyRepository.GetStandaloneLinkedByAsync(cancellationToken);
        var madeFrom = subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(cancellationToken);
        await Task.WhenAll(roles, rawMaterials, subAssemblies, linkedBy, madeFrom);

        return new SubAssembliesCatalog(await roles, await rawMaterials, await subAssemblies, await linkedBy, await madeFrom);
    }

    public async Task<SubAssemblyContent> ReadContentAsync(int subAssemblyId, bool isPackagingSet, CancellationToken cancellationToken) =>
        isPackagingSet
            ? new SubAssemblyContent([], await subAssemblyRepository.GetPackagingLinesAsync(subAssemblyId, cancellationToken))
            : new SubAssemblyContent(await lineRepository.GetForSubAssembliesAsync([subAssemblyId], cancellationToken), []);

    public async Task<IReadOnlyList<RawMaterialCategory>> ReadMadeCategoriesAsync(CancellationToken cancellationToken) =>
        [.. (await rawMaterialCategoryRepository.GetAllAsync(cancellationToken)).Where(c => c.Type == RawMaterialCategoryType.Made)];
}
