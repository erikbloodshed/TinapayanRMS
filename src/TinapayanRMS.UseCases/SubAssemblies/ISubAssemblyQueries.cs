using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>What the Sub-Assemblies screen reads.</summary>
public interface ISubAssemblyQueries
{
    /// <summary>The roles, the raw materials, the standalone sub-assemblies, and what links and is made from each, read side by side.</summary>
    Task<SubAssembliesCatalog> ReadCatalogAsync(CancellationToken cancellationToken);

    /// <summary>A sub-assembly's lines as saved: a packaging set's lines, or a recipe's ingredient lines.</summary>
    Task<SubAssemblyContent> ReadContentAsync(int subAssemblyId, bool isPackagingSet, CancellationToken cancellationToken);

    /// <summary>The Made categories a made ingredient can be filed in (Make In-House Ingredient).</summary>
    Task<IReadOnlyList<RawMaterialCategory>> ReadMadeCategoriesAsync(CancellationToken cancellationToken);
}
