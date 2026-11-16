using System.Reactive.Linq;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// A standalone sub-assembly in the Sub-Assemblies list, with the products that use it and the made ingredients made from
/// it. "Used by" is kept as text: CollectionMerge compares a row's scalar properties, so a changed list of products or
/// made ingredients shows up as a changed row.
/// </summary>
public sealed class StandaloneSubAssemblyRow(SubAssembly subAssembly, IReadOnlyList<LinkingProduct> dependents, IReadOnlyList<MadeIngredientUse>? madeIngredients = null)
{
    private readonly IReadOnlyList<MadeIngredientUse> _madeIngredients = madeIngredients ?? [];

    public SubAssembly SubAssembly { get; } = subAssembly;

    public int Id => SubAssembly.Id;

    public string Name => SubAssembly.Name ?? string.Empty;

    public string RoleName => SubAssembly.Role?.Name ?? string.Empty;

    /// <summary>The products linking it, e.g. "Cream Loaf, Cream Loaf Mini", or empty when none do.</summary>
    public string DependentsText { get; } = string.Join(", ", dependents.Select(p => p.Name));

    /// <summary>The products linking it, by name. A method, not a property: CollectionMerge would compare the list by its scalar properties only.</summary>
    public IReadOnlyList<LinkingProduct> GetDependents() => dependents;

    /// <summary>The made ingredients made from it, e.g. "Butter Icing, Breads", or empty when none are.</summary>
    public string MadeIngredientsText { get; } = string.Join(", ", (madeIngredients ?? []).Select(m => m.Name));

    /// <summary>The made ingredients made from it, by name. A method for the same reason as <see cref="GetDependents"/>.</summary>
    public IReadOnlyList<MadeIngredientUse> GetMadeIngredients() => _madeIngredients;
}
