using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>
/// What can be done to a standalone sub-assembly: saved with its lines and a version of each product it changes, deleted,
/// renamed, moved to another role, and a made ingredient made from it. A refusal is a <see cref="BusinessRuleException"/>,
/// in words for the shop's staff.
/// </summary>
public interface ISubAssemblyEditor
{
    /// <summary>Refuses a name or a role a sub-assembly can't have, first, before its lines are read off the form.</summary>
    void EnsureValid(string name, int? roleId);

    /// <summary>
    /// Refuses a request with no lines, and otherwise says what saving it does to the products linking it: each product
    /// whose base cost its lines move, the most moved first. None for a new one, or a packaging set.
    /// </summary>
    Task<IReadOnlyList<RecipeImpact>> CheckSaveAsync(SubAssemblySaveRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the sub-assembly with its lines, and a version of each product using it that the save changes, both or
    /// neither (VersionedSaves). Refused as <see cref="CheckSaveAsync"/> refuses, and over a save made since the form took it.
    /// </summary>
    Task<SubAssemblySaved> SaveAsync(SubAssemblySaveRequest request, DateTime savedAt, CancellationToken cancellationToken);

    Task DeleteAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>Gives sub-assemblies other roles, in one transaction, refused over a save made since <paramref name="checks"/> were taken.</summary>
    Task MoveAsync(IReadOnlyCollection<SubAssemblyMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Renames a sub-assembly, refused over a save made since <paramref name="checks"/> were taken.</summary>
    Task RenameAsync(int subAssemblyId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Adds a made ingredient made from the recipe, with no add-ins and no weight loss.</summary>
    Task AddMadeIngredientAsync(int subAssemblyId, MadeIngredientDraft made, CancellationToken cancellationToken);
}
