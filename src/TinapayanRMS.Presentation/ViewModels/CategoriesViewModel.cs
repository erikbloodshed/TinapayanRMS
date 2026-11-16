using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Settings' Material Categories tab: the raw material categories, each for ingredients, made ingredients or packaging.
/// Picking a category loads it for renaming or retyping; New, or finishing a Save or Delete, clears the form, so
/// the next Save always adds a category rather than renaming the last one touched.
/// </summary>
public sealed class CategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    private RawMaterialCategory? _selectedCategory;
    public RawMaterialCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            // Not while LoadCommand merges, which only re-points the selection at its refreshed copy.
            if (RaiseAndSetIfChanged(ref _selectedCategory, value) && value is { } category && !IsRefreshing)
            {
                _editingId = category.Id;
                EditingName = category.Name;
                EditingType = category.Type;
            }
        }
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private RawMaterialCategoryType _editingType;
    /// <summary>Whether the category is for ingredients, made ingredients or packaging: a material only goes in a category of its own type.</summary>
    public RawMaterialCategoryType EditingType
    {
        get => _editingType;
        set => RaiseAndSetIfChanged(ref _editingType, value);
    }

    /// <summary>The Type picker's choices.</summary>
    public IReadOnlyList<RawMaterialCategoryType> Types { get; } = Enum.GetValues<RawMaterialCategoryType>();

    /// <summary>"New Category", or "Edit Category" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => SelectedCategory is null ? "New Category" : "Edit Category";

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand NewCommand { get; }
    public ScreenCommand SaveCommand { get; }
    public ScreenCommand DeleteCommand { get; }

    public CategoriesViewModel(IRawMaterialCategoryRepository categoryRepository, IValidator<RawMaterialCategory> categoryValidator, ILogger logger)
        : base(logger)
    {
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedCategory?.Id;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId);
            });
        }));

        NewCommand = ScreenCommand.Of(ClearForm);

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var category = new RawMaterialCategory { Id = _editingId, Name = EditingName.Trim(), Type = EditingType };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            if (_editingId == 0)
            {
                await _categoryRepository.AddAsync(category, CancellationToken.None);
            }
            else
            {
                await _categoryRepository.UpdateAsync(category, CancellationToken.None);
            }

            ClearForm();
            await LoadCommand.ExecuteAsync();
        }));

        DeleteCommand = new ScreenCommand(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.ExecuteAsync();
            }),
            () => SelectedCategory is not null);
    }

    private void ClearForm()
    {
        SelectedCategory = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingType = RawMaterialCategoryType.Ingredient;
    }

    public void Dispose() => _categoryCache.Dispose();
}
