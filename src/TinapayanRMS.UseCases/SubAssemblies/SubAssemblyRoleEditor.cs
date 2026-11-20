using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>
/// What the Sub-Assemblies list does to a role, its folders: adds one after the others, renames one, deletes one. A refusal
/// is a <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface ISubAssemblyRoleEditor
{
    /// <summary>Adds a role named <paramref name="name"/> after the others; the role as saved, with its id.</summary>
    Task<SubAssemblyRole> AddAsync(string name, CancellationToken cancellationToken);

    /// <summary>Renames <paramref name="role"/>, keeping its place and whether products own it.</summary>
    Task RenameAsync(SubAssemblyRole role, string name, CancellationToken cancellationToken);

    /// <summary>Deletes a role; the database refuses one anything is in.</summary>
    Task DeleteAsync(int roleId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ISubAssemblyRoleEditor"/>
public sealed class SubAssemblyRoleEditor(ISubAssemblyRoleRepository roleRepository, IValidator<SubAssemblyRole> validator) : ISubAssemblyRoleEditor
{
    public async Task<SubAssemblyRole> AddAsync(string name, CancellationToken cancellationToken)
    {
        var added = new SubAssemblyRole { Name = name };
        EnsureValid(added);

        // AddAsync fills in the saved id.
        await roleRepository.AddAsync(added, cancellationToken);
        return added;
    }

    public Task RenameAsync(SubAssemblyRole role, string name, CancellationToken cancellationToken)
    {
        var renamed = new SubAssemblyRole
        {
            Id = role.Id,
            Name = name,
            SortOrder = role.SortOrder,
            IsPackaging = role.IsPackaging,
            IsProductOwned = role.IsProductOwned,
        };
        EnsureValid(renamed);
        return roleRepository.UpdateAsync(renamed, cancellationToken);
    }

    public Task DeleteAsync(int roleId, CancellationToken cancellationToken) => roleRepository.DeleteAsync(roleId, cancellationToken);

    private void EnsureValid(SubAssemblyRole role)
    {
        var validation = validator.Validate(role);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
