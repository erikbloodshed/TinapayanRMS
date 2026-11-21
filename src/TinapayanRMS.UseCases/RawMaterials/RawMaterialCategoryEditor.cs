using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UseCases.Settings;

namespace TinapayanRMS.UseCases.RawMaterials;

/// <summary>
/// What the Raw Materials screen does to a category, its list's folders and its forms' Category picker: adds one of a kind,
/// renames one, deletes one; and Settings' Material Categories tab (<see cref="ILookupListEditor{TItem}"/>). A refusal is
/// a <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface IRawMaterialCategoryEditor : ILookupListEditor<RawMaterialCategory>
{
    /// <summary>Refuses a name a category of <paramref name="type"/> can't take, before anything is asked or written.</summary>
    void EnsureValid(string name, RawMaterialCategoryType type);

    /// <summary>Adds a category of <paramref name="type"/> named <paramref name="name"/>; the category as saved, with its id.</summary>
    Task<RawMaterialCategory> AddAsync(string name, RawMaterialCategoryType type, CancellationToken cancellationToken);

    /// <summary>Renames <paramref name="category"/>, keeping its type.</summary>
    Task RenameAsync(RawMaterialCategory category, string name, CancellationToken cancellationToken);

}

/// <inheritdoc cref="IRawMaterialCategoryEditor"/>
public sealed class RawMaterialCategoryEditor(IRawMaterialCategoryRepository categoryRepository, IValidator<RawMaterialCategory> validator)
    : IRawMaterialCategoryEditor
{
    public void EnsureValid(string name, RawMaterialCategoryType type) => Ensure(new RawMaterialCategory { Name = name, Type = type });

    public void EnsureValid(RawMaterialCategory item) => Ensure(item);

    public Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken) => categoryRepository.GetAllAsync(cancellationToken);

    public async Task<RawMaterialCategory> AddAsync(string name, RawMaterialCategoryType type, CancellationToken cancellationToken)
    {
        var added = new RawMaterialCategory { Name = name, Type = type };
        Ensure(added);

        // AddAsync fills in the saved id.
        await categoryRepository.AddAsync(added, cancellationToken);
        return added;
    }

    public async Task RenameAsync(RawMaterialCategory category, string name, CancellationToken cancellationToken)
    {
        var renamed = new RawMaterialCategory { Id = category.Id, Name = name, Type = category.Type };
        Ensure(renamed);
        await categoryRepository.UpdateAsync(renamed, cancellationToken);
    }

    public async Task AddAsync(RawMaterialCategory item, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task rather than throwing as it's called.
        Ensure(item);
        await categoryRepository.AddAsync(item, cancellationToken);
    }

    public async Task UpdateAsync(RawMaterialCategory item, CancellationToken cancellationToken)
    {
        Ensure(item);
        await categoryRepository.UpdateAsync(item, cancellationToken);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken) => categoryRepository.DeleteAsync(id, cancellationToken);

    private void Ensure(RawMaterialCategory category)
    {
        var validation = validator.Validate(category);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
