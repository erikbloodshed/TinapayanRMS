using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Settings dialog's Product Categories tab: what the Products list groups products under, a list of its own
/// apart from raw material categories (CategoriesViewModel).
/// Picking a category loads it for renaming; New, or finishing a Save or Delete, clears the form, so
/// the next Save always adds a category rather than renaming the last one touched.
/// A category can set the Wastage % its products are priced with in place of the shop-wide one.
/// </summary>
public sealed class ProductCategoriesViewModel : ViewModelBase, IDisposable
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
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private decimal? _editingWastagePercent;

    /// <summary>The Wastage % the category's products are priced with; null (blank) for the shop-wide one.</summary>
    public decimal? EditingWastagePercent
    {
        get => _editingWastagePercent;
        set => this.RaiseAndSetIfChanged(ref _editingWastagePercent, value);
    }

    private readonly ObservableAsPropertyHelper<string> _formTitle;
    /// <summary>"New Category", or "Edit Category" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => _formTitle.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public ProductCategoriesViewModel(IProductCategoryRepository categoryRepository, IValidator<ProductCategory> categoryValidator, ILogger logger)
        : base(logger)
    {
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe();

        _formTitle = this.WhenAnyValue(x => x.SelectedCategory)
            .Select(category => category is null ? "New Category" : "Edit Category")
            .ToProperty(this, x => x.FormTitle, "New Category");

        // Skips while LoadCommand merges, which only re-points the selection at its refreshed copy.
        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category =>
            {
                _editingId = category!.Id;
                EditingName = category.Name;
                EditingWastagePercent = category.WastagePercent;
            });

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedCategory?.Id;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId);
            });
        }));

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new ProductCategory { Id = _editingId, Name = EditingName.Trim(), WastagePercent = EditingWastagePercent };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
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
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? Its products are left uncategorized. This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
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
