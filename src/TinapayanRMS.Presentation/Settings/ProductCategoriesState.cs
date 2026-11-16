using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Product Categories tab: what the Products list groups products under, a list of its own
/// apart from raw material categories (CategoriesState).
/// Picking a category loads it for renaming; New, or finishing a Save or Delete, clears the form, so
/// the next Save always adds a category rather than renaming the last one touched.
/// A category can set the Wastage % its products are priced with in place of the shop-wide one.
/// </summary>
public sealed class ProductCategoriesState : ScreenState, IDisposable
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IValidator<ProductCategory> _categoryValidator;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categories;

    public ReadOnlyObservableCollection<ProductCategory> Categories => _categories;

    private ProductCategory? _selectedCategory;
    public ProductCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            // Not while LoadCommand merges, which only re-points the selection at its refreshed copy.
            if (RaiseAndSetIfChanged(ref _selectedCategory, value) && value is { } category && !IsRefreshing)
            {
                _editingId = category.Id;
                EditingName = category.Name;
                EditingWastagePercent = category.WastagePercent;
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

    private decimal? _editingWastagePercent;

    /// <summary>The Wastage % the category's products are priced with; null (blank) for the shop-wide one.</summary>
    public decimal? EditingWastagePercent
    {
        get => _editingWastagePercent;
        set => RaiseAndSetIfChanged(ref _editingWastagePercent, value);
    }

    /// <summary>"New Category", or "Edit Category" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => SelectedCategory is null ? "New Category" : "Edit Category";

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand NewCommand { get; }
    public ScreenCommand SaveCommand { get; }
    public ScreenCommand DeleteCommand { get; }

    public ProductCategoriesState(IProductCategoryRepository categoryRepository, IValidator<ProductCategory> categoryValidator, ILogger logger)
        : base(logger)
    {
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
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
            var category = new ProductCategory { Id = _editingId, Name = EditingName.Trim(), WastagePercent = EditingWastagePercent };

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
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? Its products are left uncategorized. This can't be undone.", async () =>
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
        EditingWastagePercent = null;
    }

    public void Dispose() => _categoryCache.Dispose();
}
