using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UseCases.Settings;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>
/// What the Sub-Assemblies list does to a role, its folders: adds one after the others, renames one, deletes one; and
/// Settings' Roles tab (<see cref="ILookupListEditor{TItem}"/>), which also moves one and turns one product-owned. A
/// refusal is a <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface ISubAssemblyRoleEditor : ILookupListEditor<SubAssemblyRole>
{
    /// <summary>Adds a role named <paramref name="name"/> after the others; the role as saved, with its id.</summary>
    Task<SubAssemblyRole> AddAsync(string name, CancellationToken cancellationToken);

    /// <summary>Renames <paramref name="role"/>, keeping its place and whether products own it.</summary>
    Task RenameAsync(SubAssemblyRole role, string name, CancellationToken cancellationToken);

    /// <summary>
    /// How many sub-assemblies an update turning role <paramref name="roleId"/> product-owned (<paramref name="makeOwned"/>),
    /// or back, would move between products and the Sub-Assemblies screen.
    /// </summary>
    Task<int> CountOwnershipChangesAsync(int roleId, bool makeOwned, CancellationToken cancellationToken);

    /// <summary>Swaps role <paramref name="roleId"/> with the one above it (<paramref name="up"/>) or below it.</summary>
    Task MoveAsync(int roleId, bool up, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ISubAssemblyRoleEditor"/>
public sealed class SubAssemblyRoleEditor(ISubAssemblyRoleRepository roleRepository, IValidator<SubAssemblyRole> validator) : ISubAssemblyRoleEditor
{
    public Task<IReadOnlyList<SubAssemblyRole>> GetAllAsync(CancellationToken cancellationToken) => roleRepository.GetAllAsync(cancellationToken);

    public void EnsureValid(SubAssemblyRole item)
    {
        var validation = validator.Validate(item);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }

    public async Task AddAsync(SubAssemblyRole item, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task rather than throwing as it's called.
        EnsureValid(item);
        await roleRepository.AddAsync(item, cancellationToken);
    }

    public async Task UpdateAsync(SubAssemblyRole item, CancellationToken cancellationToken)
    {
        EnsureValid(item);
        await roleRepository.UpdateAsync(item, cancellationToken);
    }

    public Task<int> CountOwnershipChangesAsync(int roleId, bool makeOwned, CancellationToken cancellationToken) =>
        roleRepository.CountOwnershipChangesAsync(roleId, makeOwned, cancellationToken);

    public Task MoveAsync(int roleId, bool up, CancellationToken cancellationToken) => roleRepository.MoveAsync(roleId, up, cancellationToken);

    public async Task<SubAssemblyRole> AddAsync(string name, CancellationToken cancellationToken)
    {
        var added = new SubAssemblyRole { Name = name };
        EnsureValid(added);

        // AddAsync fills in the saved id.
        await roleRepository.AddAsync(added, cancellationToken);
        return added;
    }

    public async Task RenameAsync(SubAssemblyRole role, string name, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task, as AddAsync's does, rather than throwing as it's called.
        var renamed = new SubAssemblyRole
        {
            Id = role.Id,
            Name = name,
            SortOrder = role.SortOrder,
            IsPackaging = role.IsPackaging,
            IsProductOwned = role.IsProductOwned,
        };
        EnsureValid(renamed);
        await roleRepository.UpdateAsync(renamed, cancellationToken);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken) => roleRepository.DeleteAsync(id, cancellationToken);
}
