using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>An in-memory (not yet saved) BOM line, shown in the BOM editor grid.</summary>
public class IngredientBomLineEdit(Ingredient component, decimal grams)
{
    public Ingredient Component { get; } = component;

    public decimal Grams { get; } = grams;
}

public class IngredientsViewModel : ViewModelBase
{
    private readonly IIngredientRepository _ingredientRepository;
    private readonly IIngredientPriceHistoryRepository _priceHistoryRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IIngredientCategoryRepository _categoryRepository;
    private readonly IValidator<Ingredient> _ingredientValidator;
    private readonly IValidator<IngredientPriceHistory> _priceHistoryValidator;
    private readonly IValidator<IngredientCategory> _categoryValidator;
    private readonly SourceCache<Ingredient, int> _ingredientCache = new(i => i.Id);
    private readonly ReadOnlyObservableCollection<Ingredient> _ingredients;
    private readonly SourceCache<IngredientPriceHistory, int> _priceHistoryCache = new(h => h.Id);
    private readonly ReadOnlyObservableCollection<IngredientPriceHistory> _priceHistory;
    private readonly SourceCache<IngredientCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<IngredientCategory> _categories;

    public ReadOnlyObservableCollection<Ingredient> Ingredients => _ingredients;

    public ReadOnlyObservableCollection<IngredientPriceHistory> PriceHistory => _priceHistory;

    public ObservableCollection<IngredientBomLineEdit> BomLineEdits { get; } = [];

    public ReadOnlyObservableCollection<IngredientCategory> Categories => _categories;

    public ObservableCollection<UnitOfMeasure> PurchaseUnits { get; } = [];

    public IReadOnlyList<IngredientSource> Sources { get; } = Enum.GetValues<IngredientSource>();

    private Ingredient? _selectedIngredient;
    public Ingredient? SelectedIngredient
    {
        get => _selectedIngredient;
        set => this.RaiseAndSetIfChanged(ref _selectedIngredient, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private IngredientCategory? _editingCategory;
    public IngredientCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private IngredientSource _editingSource = IngredientSource.Purchased;
    public IngredientSource EditingSource
    {
        get => _editingSource;
        set => this.RaiseAndSetIfChanged(ref _editingSource, value);
    }

    private UnitOfMeasure? _editingPurchaseUnit;
    public UnitOfMeasure? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    private decimal? _editingPurchaseUnitQty;
    public decimal? EditingPurchaseUnitQty
    {
        get => _editingPurchaseUnitQty;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnitQty, value);
    }

    private decimal? _editingWeightLossPercent;
    public decimal? EditingWeightLossPercent
    {
        get => _editingWeightLossPercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightLossPercent, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isEditingPurchased;
    public bool IsEditingPurchased => _isEditingPurchased.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingMade;
    public bool IsEditingMade => _isEditingMade.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPriceHistory;
    public bool ShowPriceHistory => _showPriceHistory.Value;

    private readonly ObservableAsPropertyHelper<bool> _showBomEditor;
    public bool ShowBomEditor => _showBomEditor.Value;

    private Ingredient? _editingBomComponent;
    public Ingredient? EditingBomComponent
    {
        get => _editingBomComponent;
        set => this.RaiseAndSetIfChanged(ref _editingBomComponent, value);
    }

    private decimal _editingBomGrams;
    public decimal EditingBomGrams
    {
        get => _editingBomGrams;
        set => this.RaiseAndSetIfChanged(ref _editingBomGrams, value);
    }

    private IngredientBomLineEdit? _selectedBomLineEdit;
    public IngredientBomLineEdit? SelectedBomLineEdit
    {
        get => _selectedBomLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBomLineEdit, value);
    }

    private IngredientPriceHistory? _selectedPriceHistoryEntry;
    public IngredientPriceHistory? SelectedPriceHistoryEntry
    {
        get => _selectedPriceHistoryEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceHistoryEntry, value);
    }

    // The ingredient's price when the form was loaded, so Save can tell whether EditingPrice changed.
    private decimal? _editingCurrentPrice;

    /// <summary>
    /// Purchased only: the price per purchase unit, shown in the Details form. Saving the form with
    /// a different price records it as a price history entry dated that day, which is what moves
    /// CurrentCost; there's no separate "add price" step.
    /// </summary>
    private decimal? _editingPrice;
    public decimal? EditingPrice
    {
        get => _editingPrice;
        set => this.RaiseAndSetIfChanged(ref _editingPrice, value);
    }

    // Category management (embedded panel, independent of the selected ingredient).
    private IngredientCategory? _selectedCategory;
    public IngredientCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    private int _editingCategoryId;

    private string _editingCategoryName = string.Empty;
    public string EditingCategoryName
    {
        get => _editingCategoryName;
        set => this.RaiseAndSetIfChanged(ref _editingCategoryName, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveBomCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCategoryCommand { get; }

    public IngredientsViewModel(
        IIngredientRepository ingredientRepository,
        IIngredientPriceHistoryRepository priceHistoryRepository,
        IIngredientBomLineRepository bomLineRepository,
        IIngredientCategoryRepository categoryRepository,
        IValidator<Ingredient> ingredientValidator,
        IValidator<IngredientPriceHistory> priceHistoryValidator,
        IValidator<IngredientCategory> categoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _ingredientRepository = ingredientRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _bomLineRepository = bomLineRepository;
        _categoryRepository = categoryRepository;
        _ingredientValidator = ingredientValidator;
        _priceHistoryValidator = priceHistoryValidator;
        _categoryValidator = categoryValidator;

        _ingredientCache.Connect()
            .SortAndBind(out _ingredients, SortExpressionComparer<Ingredient>.Ascending(i => i.Name))
            .Subscribe();

        _priceHistoryCache.Connect()
            .SortAndBind(out _priceHistory, SortExpressionComparer<IngredientPriceHistory>.Descending(h => h.Date).ThenByDescending(h => h.Id))
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<IngredientCategory>.Ascending(c => c.Name))
            .Subscribe();

        _isEditingPurchased = this.WhenAnyValue(x => x.EditingSource)
            .Select(source => source == IngredientSource.Purchased)
            .ToProperty(this, x => x.IsEditingPurchased);

        _isEditingMade = this.WhenAnyValue(x => x.EditingSource)
            .Select(source => source == IngredientSource.Made)
            .ToProperty(this, x => x.IsEditingMade);

        _showPriceHistory = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is { Source: IngredientSource.Purchased })
            .ToProperty(this, x => x.ShowPriceHistory);

        _showBomEditor = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is { Source: IngredientSource.Made })
            .ToProperty(this, x => x.ShowBomEditor);

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(ingredient => ingredient is not null && !IsRefreshing)
            .Subscribe(ingredient => LoadIntoForm(ingredient!));

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ =>
            {
                SelectedPriceHistoryEntry = null;
                SelectedBomLineEdit = null;
                EditingBomComponent = null;
                EditingBomGrams = 0m;
            });

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(_ => !IsRefreshing)
            .Select(ingredient => Observable.FromAsync(() => LoadPriceHistoryAsync(ingredient)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(_ => !IsRefreshing)
            .Select(ingredient => Observable.FromAsync(() => LoadBomLinesAsync(ingredient)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category =>
            {
                _editingCategoryId = category!.Id;
                EditingCategoryName = category.Name;
            });

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit added on another tab.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var ingredients = await _ingredientRepository.GetAllAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedIngredient?.Id;
            var purchaseUnitId = EditingPurchaseUnit?.Id;
            var bomComponentId = EditingBomComponent?.Id;

            ApplyRefresh(() =>
            {
                MergeCategories(categories);
                PurchaseUnits.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _ingredientCache.MergeFrom(ingredients);

                EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == purchaseUnitId);
                EditingBomComponent = Ingredients.FirstOrDefault(i => i.Id == bomComponentId);
                SelectedIngredient = Ingredients.FirstOrDefault(i => i.Id == selectedId);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedIngredient = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingSource = IngredientSource.Purchased;
            EditingPurchaseUnit = PurchaseUnits.FirstOrDefault();
            EditingPurchaseUnitQty = null;
            _editingCurrentPrice = null;
            EditingPrice = null;
            EditingWeightLossPercent = null;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var ingredient = new Ingredient
            {
                Id = _editingId,
                Name = EditingName,
                IngredientCategoryId = EditingCategory?.Id ?? 0,
                Source = EditingSource,
                PurchaseUnitId = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnit?.Id : null,
                PurchaseUnitQty = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnitQty : null,
                WeightLossPercent = EditingSource == IngredientSource.Made ? EditingWeightLossPercent : null,
            };

            var validation = _ingredientValidator.Validate(ingredient);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // A changed price becomes today's price history entry. Check it before saving anything,
            // so a bad price can't leave the ingredient saved without it.
            IngredientPriceHistory? newPrice = null;
            if (EditingSource == IngredientSource.Purchased && PriceHistoryRules.IsNewPrice(_editingCurrentPrice, EditingPrice))
            {
                newPrice = new IngredientPriceHistory
                {
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    Price = EditingPrice ?? 0m,
                };

                var priceValidation = _priceHistoryValidator.Validate(newPrice);
                if (!priceValidation.IsValid)
                {
                    ErrorMessage = string.Join(" ", priceValidation.Errors.Select(e => e.ErrorMessage));
                    return;
                }
            }

            if (_editingId == 0)
            {
                await _ingredientRepository.AddAsync(ingredient, CancellationToken.None);
            }
            else
            {
                await _ingredientRepository.UpdateAsync(ingredient, CancellationToken.None);
            }

            if (newPrice is not null)
            {
                // AddAsync has filled in a new ingredient's id by now.
                newPrice.IngredientId = ingredient.Id;
                await _priceHistoryRepository.AddAsync(newPrice, CancellationToken.None);
                _editingCurrentPrice = newPrice.Price;
            }

            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedIngredient!.Name}'? This can't be undone.", async () =>
            {
                await _ingredientRepository.DeleteAsync(SelectedIngredient!.Id, CancellationToken.None);
                SelectedIngredient = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedIngredient).Select(ingredient => ingredient is not null));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceHistoryEntry!.Id, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPriceHistoryEntry).Select(entry => entry is not null));

        AddBomLineCommand = ReactiveCommand.Create(
            () => BomLineEdits.Add(new IngredientBomLineEdit(EditingBomComponent!, EditingBomGrams)),
            this.WhenAnyValue(x => x.EditingBomComponent, x => x.EditingBomGrams, (component, grams) => component is not null && grams > 0));

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => { BomLineEdits.Remove(SelectedBomLineEdit!); },
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));

        SaveBomCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var lines = BomLineEdits.Select(l => (l.Component.Id, l.Grams)).ToList();
                await _bomLineRepository.SetLinesAsync(SelectedIngredient!.Id, lines, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            }),
            this.WhenAnyValue(x => x.SelectedIngredient).Select(ingredient => ingredient is { Source: IngredientSource.Made }));

        NewCategoryCommand = ReactiveCommand.Create(() =>
        {
            SelectedCategory = null;
            _editingCategoryId = 0;
            EditingCategoryName = string.Empty;
        });

        SaveCategoryCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new IngredientCategory { Id = _editingCategoryId, Name = EditingCategoryName };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingCategoryId == 0)
            {
                await _categoryRepository.AddAsync(category, CancellationToken.None);
            }
            else
            {
                await _categoryRepository.UpdateAsync(category, CancellationToken.None);
            }

            // The whole screen, not just the categories: a renamed category shows in the grid too.
            await LoadCommand.Execute();
            SelectedCategory = null;
        }));

        DeleteCategoryCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                await LoadCommand.Execute();
                SelectedCategory = null;
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
    }

    /// <summary>Merges categories in, keeping the Details form's and the management panel's picks.</summary>
    private void MergeCategories(IReadOnlyList<IngredientCategory> categories)
    {
        var editingCategoryId = EditingCategory?.Id;
        var panelCategoryId = SelectedCategory?.Id;

        _categoryCache.MergeFrom(categories);

        EditingCategory = Categories.FirstOrDefault(c => c.Id == editingCategoryId);
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == panelCategoryId);
    }

    private void LoadIntoForm(Ingredient ingredient)
    {
        _editingId = ingredient.Id;
        EditingName = ingredient.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == ingredient.IngredientCategoryId);
        EditingSource = ingredient.Source;
        EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == ingredient.PurchaseUnitId);
        EditingPurchaseUnitQty = ingredient.PurchaseUnitQty;
        _editingCurrentPrice = ingredient.CurrentCost;
        EditingPrice = ingredient.CurrentCost;
        EditingWeightLossPercent = ingredient.WeightLossPercent;
    }

    private async Task LoadBomLinesAsync(Ingredient? ingredient)
    {
        BomLineEdits.Clear();

        if (ingredient is not { Source: IngredientSource.Made })
        {
            return;
        }

        var lines = await _bomLineRepository.GetForIngredientAsync(ingredient.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            BomLineEdits.Add(new IngredientBomLineEdit(line.ComponentIngredient!, line.Grams));
        }
    }

    private async Task LoadPriceHistoryAsync(Ingredient? ingredient)
    {
        if (ingredient is not { Source: IngredientSource.Purchased })
        {
            _priceHistoryCache.Edit(cache => cache.Clear());
            return;
        }

        var entries = await _priceHistoryRepository.GetForIngredientAsync(ingredient.Id, CancellationToken.None);
        _priceHistoryCache.Edit(cache =>
        {
            cache.Clear();
            cache.AddOrUpdate(entries);
        });
    }

    /// <summary>
    /// Re-fetches the selected ingredient (its CurrentCost/CostPerGram just changed)
    /// and pushes it back into the grid's cache and the selection, which also
    /// re-triggers the price-history reload above.
    /// </summary>
    private async Task RefreshSelectedIngredientAsync()
    {
        if (SelectedIngredient is null)
        {
            return;
        }

        var refreshed = await _ingredientRepository.GetByIdAsync(SelectedIngredient.Id, CancellationToken.None);
        if (refreshed is not null)
        {
            _ingredientCache.AddOrUpdate(refreshed);
            SelectedIngredient = refreshed;
        }
    }
}
