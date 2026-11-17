using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Material Categories tab: the raw material categories, each for ingredients, made ingredients or packaging.
/// Picking a category loads it for renaming or retyping.
/// </summary>
public sealed class RawMaterialCategoriesState(IRawMaterialCategoryRepository repository, IValidator<RawMaterialCategory> validator, ILogger logger)
    : LookupListState<RawMaterialCategory>("Category", c => c.Id, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name), validator, logger)
{
    private RawMaterialCategoryType _editingType;
    /// <summary>Whether the category is for ingredients, made ingredients or packaging: a material only goes in a category of its own type.</summary>
    public RawMaterialCategoryType EditingType
    {
        get => _editingType;
        set => RaiseAndSetIfChanged(ref _editingType, value);
    }

    /// <summary>The Type picker's choices.</summary>
    public IReadOnlyList<RawMaterialCategoryType> Types { get; } = Enum.GetValues<RawMaterialCategoryType>();

    protected override Task<IReadOnlyList<RawMaterialCategory>> ReadAllAsync() => repository.GetAllAsync(CancellationToken.None);

    protected override Task AddAsync(RawMaterialCategory item) => repository.AddAsync(item, CancellationToken.None);

    protected override Task UpdateAsync(RawMaterialCategory item) => repository.UpdateAsync(item, CancellationToken.None);

    protected override Task DeleteAsync(int id) => repository.DeleteAsync(id, CancellationToken.None);

    protected override string DeleteQuestion(RawMaterialCategory item) => $"Delete the '{item.Name}' category? This can't be undone.";

    protected override void LoadIntoForm(RawMaterialCategory item)
    {
        EditingName = item.Name;
        EditingType = item.Type;
    }

    protected override RawMaterialCategory FormItem() => new() { Id = EditingId, Name = EditingName.Trim(), Type = EditingType };

    protected override void ClearFields() => EditingType = RawMaterialCategoryType.Ingredient;
}
