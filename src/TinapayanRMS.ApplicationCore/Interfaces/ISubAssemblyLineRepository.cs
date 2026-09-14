using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>Reads sub-assembly lines. They're written with their owner product, by IProductRepository.SaveAsync.</summary>
public interface ISubAssemblyLineRepository
{
    /// <summary>
    /// The lines of every sub-assembly in <paramref name="subAssemblyIds"/>, each with its Ingredient, in
    /// one query: a product's recipe shows the lines of all its sub-assemblies.
    /// </summary>
    Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssembliesAsync(IReadOnlyCollection<int> subAssemblyIds, CancellationToken cancellationToken);
}
