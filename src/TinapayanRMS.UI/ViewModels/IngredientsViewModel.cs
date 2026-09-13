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

/// <summary>An in-memory (not yet saved) BOM line, shown in the BOM editor grid: an amount of a component in one of its units.</summary>
public class IngredientBomLineEdit(Ingredient component, IngredientUnitOption unit, decimal quantity)
{
    public Ingredient Component { get; } = component;

    public IngredientUnitOption Unit { get; } = unit;

    public decimal Quantity { get; } = quantity;

    public decimal Grams => UnitConversionRules.ToBaseQuantity(Quantity, Unit.Unit?.GramsPerUnit);
}

/// <summary>A row in the Alternative Units grid and a box in the Unit Conversion Overview.</summary>
public sealed class IngredientUnitRow(IngredientUnit unit)
{
    public IngredientUnit Unit { get; } = unit;

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? "?";

    public decimal GramsPerUnit => Unit.GramsPerUnit;

    public string BaseEquivalentText => $"{Unit.GramsPerUnit:#,0.####} g";

    /// <summary>E.g. "1 bag = 25,000 g".</summary>
    public string Example => $"1 {Abbreviation} = {BaseEquivalentText}";

    public string? Description => Unit.Description;
}

/// <summary>A row in the Price History grid: the entry, the unit it was priced in, and what that comes to per gram.</summary>
public sealed class IngredientPriceRow(IngredientPriceHistory entry)
{
    public IngredientPriceHistory Entry { get; } = entry;

    public DateOnly Date => Entry.Date;

    public string UnitName => Entry.IngredientUnit?.UnitOfMeasure?.Name ?? "gram";

    public decimal Price => Entry.Price;

    public decimal CostPerGram => UnitConversionRules.CostPerBaseUnit(Entry.Price, Entry.IngredientUnit?.GramsPerUnit);
}

/// <summary>What the Recipe Usage Example worked out.</summary>
public sealed record UsageExampleResult(string Title, decimal QuantityInGrams, decimal CostPerGram, decimal TotalCost);

public class IngredientsViewModel : ViewModelBase
{
    private readonly IIngredientRepository _ingredientRepository;
    private readonly IIngredientUnitRepository _unitRepository;
    private readonly IIngredientPriceHistoryRepository _priceHistoryRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IIngredientCategoryRepository _categoryRepository;
    private readonly IValidator<Ingredient> _ingredientValidator;
    private readonly IValidator<IngredientUnit> _unitValidator;
    private readonly IValidator<IngredientPriceHistory> _priceHistoryValidator;
    private readonly IValidator<IngredientCategory> _categoryValidator;
    private readonly SourceCache<Ingredient, int> _ingredientCache = new(i => i.Id);
    private readonly ReadOnlyObservableCollection<Ingredient> _ingredients;
    private readonly ReadOnlyObservableCollection<Ingredient> _visibleIngredients;
    private readonly SourceCache<IngredientCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<IngredientCategory> _categories;

    /// <summary>Every ingredient, for the pickers.</summary>
    public ReadOnlyObservableCollection<Ingredient> Ingredients => _ingredients;

    /// <summary>The ingredients the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<Ingredient> VisibleIngredients => _visibleIngredients;

    public ObservableCollection<IngredientPriceRow> PriceHistory { get; } = [];

    public ObservableCollection<IngredientBomLineEdit> BomLineEdits { get; } = [];

    public ReadOnlyObservableCollection<IngredientCategory> Categories => _categories;

    /// <summary>Every unit of measure, for the Alternative Units form.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

    public ObservableCollection<IngredientUnitRow> UnitRows { get; } = [];

    public ObservableCollection<IngredientUnitOption> PurchaseUnitOptions { get; } = [];

    public ObservableCollection<IngredientUnitOption> ExampleUnitOptions { get; } = [];

    public IReadOnlyList<IngredientSource> Sources { get; } = Enum.GetValues<IngredientSource>();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

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

    private decimal? _editingWeightLossPercent;
    public decimal? EditingWeightLossPercent
    {
        get => _editingWeightLossPercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightLossPercent, value);
    }

    /// <summary>Only a new ingredient's source can be picked: it's fixed once saved (IngredientRules.EnsureSourceUnchanged).</summary>
    private bool _canChangeSource = true;
    public bool CanChangeSource
    {
        get => _canChangeSource;
        private set => this.RaiseAndSetIfChanged(ref _canChangeSource, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isEditingMade;
    public bool IsEditingMade => _isEditingMade.Value;

    private readonly ObservableAsPropertyHelper<bool> _hasSelection;
    public bool HasSelection => _hasSelection.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPurchaseSetup;
    public bool ShowPurchaseSetup => _showPurchaseSetup.Value;

    private readonly ObservableAsPropertyHelper<bool> _showBomEditor;
    public bool ShowBomEditor => _showBomEditor.Value;

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the ingredient's name, or "New ingredient".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    /// <summary>The latest price's date, or "—" for an unpriced or made ingredient.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    // Alternative Units: the row picked in the grid (Edit and Remove act on it) and the add/edit form.
    private IngredientUnitRow? _selectedUnitRow;
    public IngredientUnitRow? SelectedUnitRow
    {
        get => _selectedUnitRow;
        set => this.RaiseAndSetIfChanged(ref _selectedUnitRow, value);
    }

    /// <summary>Whether the selected ingredient can take another alternative unit (UnitConversionRules.MaxAlternativeUnits).</summary>
    private bool _canAddUnit;
    public bool CanAddUnit
    {
        get => _canAddUnit;
        private set => this.RaiseAndSetIfChanged(ref _canAddUnit, value);
    }

    private int _editingUnitId;

    private bool _isUnitFormOpen;
    public bool IsUnitFormOpen
    {
        get => _isUnitFormOpen;
        private set => this.RaiseAndSetIfChanged(ref _isUnitFormOpen, value);
    }

    private string _unitFormTitle = "Add Alternative Unit";
    public string UnitFormTitle
    {
        get => _unitFormTitle;
        private set => this.RaiseAndSetIfChanged(ref _unitFormTitle, value);
    }

    private UnitOfMeasure? _editingUnitOfMeasure;
    public UnitOfMeasure? EditingUnitOfMeasure
    {
        get => _editingUnitOfMeasure;
        set => this.RaiseAndSetIfChanged(ref _editingUnitOfMeasure, value);
    }

    private decimal? _editingUnitGramsPerUnit;
    public decimal? EditingUnitGramsPerUnit
    {
        get => _editingUnitGramsPerUnit;
        set => this.RaiseAndSetIfChanged(ref _editingUnitGramsPerUnit, value);
    }

    private string _editingUnitDescription = string.Empty;
    public string EditingUnitDescription
    {
        get => _editingUnitDescription;
        set => this.RaiseAndSetIfChanged(ref _editingUnitDescription, value);
    }

    // Purchase Cost Setup.
    private IngredientUnitOption? _editingPurchaseUnit;
    public IngredientUnitOption? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    /// <summary>The price per EditingPurchaseUnit. Saving a different price or unit adds a price history entry dated today.</summary>
    private decimal? _editingPrice;
    public decimal? EditingPrice
    {
        get => _editingPrice;
        set => this.RaiseAndSetIfChanged(ref _editingPrice, value);
    }

    private readonly ObservableAsPropertyHelper<decimal?> _purchaseCostPerGram;
    /// <summary>What EditingPrice comes to per gram, recalculated as it's typed.</summary>
    public decimal? PurchaseCostPerGram => _purchaseCostPerGram.Value;

    // Recipe Usage Example.
    private Ingredient? _exampleIngredient;
    public Ingredient? ExampleIngredient
    {
        get => _exampleIngredient;
        set => this.RaiseAndSetIfChanged(ref _exampleIngredient, value);
    }

    private decimal _exampleQuantity = 500m;
    public decimal ExampleQuantity
    {
        get => _exampleQuantity;
        set => this.RaiseAndSetIfChanged(ref _exampleQuantity, value);
    }

    private IngredientUnitOption? _exampleUnit;
    public IngredientUnitOption? ExampleUnit
    {
        get => _exampleUnit;
        set => this.RaiseAndSetIfChanged(ref _exampleUnit, value);
    }

    private UsageExampleResult? _exampleResult;
    public UsageExampleResult? ExampleResult
    {
        get => _exampleResult;
        private set => this.RaiseAndSetIfChanged(ref _exampleResult, value);
    }

    private Ingredient? _editingBomComponent;
    public Ingredient? EditingBomComponent
    {
        get => _editingBomComponent;
        set => this.RaiseAndSetIfChanged(ref _editingBomComponent, value);
    }

    /// <summary>The picked component's units, for the BOM editor's unit picker.</summary>
    public ObservableCollection<IngredientUnitOption> BomUnitOptions { get; } = [];

    private IngredientUnitOption? _editingBomUnit;
    public IngredientUnitOption? EditingBomUnit
    {
        get => _editingBomUnit;
        set => this.RaiseAndSetIfChanged(ref _editingBomUnit, value);
    }

    private decimal _editingBomQuantity;
    public decimal EditingBomQuantity
    {
        get => _editingBomQuantity;
        set => this.RaiseAndSetIfChanged(ref _editingBomQuantity, value);
    }

    private IngredientBomLineEdit? _selectedBomLineEdit;
    public IngredientBomLineEdit? SelectedBomLineEdit
    {
        get => _selectedBomLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBomLineEdit, value);
    }

    private IngredientPriceRow? _selectedPriceRow;
    public IngredientPriceRow? SelectedPriceRow
    {
        get => _selectedPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceRow, value);
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
    public ReactiveCommand<RxVoid, RxVoid> AddUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SavePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveBomCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCategoryCommand { get; }

    public IngredientsViewModel(
        IIngredientRepository ingredientRepository,
        IIngredientUnitRepository unitRepository,
        IIngredientPriceHistoryRepository priceHistoryRepository,
        IIngredientBomLineRepository bomLineRepository,
        IIngredientCategoryRepository categoryRepository,
        IValidator<Ingredient> ingredientValidator,
        IValidator<IngredientUnit> unitValidator,
        IValidator<IngredientPriceHistory> priceHistoryValidator,
        IValidator<IngredientCategory> categoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _ingredientRepository = ingredientRepository;
        _unitRepository = unitRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _bomLineRepository = bomLineRepository;
        _categoryRepository = categoryRepository;
        _ingredientValidator = ingredientValidator;
        _unitValidator = unitValidator;
        _priceHistoryValidator = priceHistoryValidator;
        _categoryValidator = categoryValidator;

        var byName = SortExpressionComparer<Ingredient>.Ascending(i => i.Name);

        _ingredientCache.Connect()
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<Ingredient, bool>)(i => text.Length == 0 || i.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _ingredientCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleIngredients, byName)
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<IngredientCategory>.Ascending(c => c.Name))
            .Subscribe();

        _isEditingMade = this.WhenAnyValue(x => x.EditingSource)
            .Select(source => source == IngredientSource.Made)
            .ToProperty(this, x => x.IsEditingMade);

        _hasSelection = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is not null)
            .ToProperty(this, x => x.HasSelection);

        _showPurchaseSetup = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is { Source: IngredientSource.Purchased })
            .ToProperty(this, x => x.ShowPurchaseSetup);

        _showBomEditor = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is { Source: IngredientSource.Made })
            .ToProperty(this, x => x.ShowBomEditor);

        _headerTitle = this.WhenAnyValue(x => x.SelectedIngredient, x => x.EditingName, (ingredient, name) =>
                ingredient is null && string.IsNullOrWhiteSpace(name) ? "New ingredient" : name)
            .ToProperty(this, x => x.HeaderTitle, "New ingredient");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _purchaseCostPerGram = this.WhenAnyValue(x => x.EditingPrice, x => x.EditingPurchaseUnit, (price, unit) =>
                price is > 0 && unit is not null ? UnitConversionRules.CostPerBaseUnit(price.Value, unit.GramsPerUnit) : (decimal?)null)
            .ToProperty(this, x => x.PurchaseCostPerGram);

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(ingredient => ingredient is not null && !IsRefreshing)
            .Subscribe(ingredient => LoadIntoForm(ingredient!));

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(_ => !IsRefreshing)
            .Subscribe(ingredient =>
            {
                SelectedPriceRow = null;
                SelectedBomLineEdit = null;
                EditingBomComponent = null;
                EditingBomQuantity = 0m;
                CloseUnitForm();
                RebuildUnitViews(ingredient);
                if (ingredient is not null)
                {
                    ExampleIngredient = ingredient;
                }
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

        this.WhenAnyValue(x => x.ExampleIngredient)
            .Where(_ => !IsRefreshing)
            .Subscribe(ingredient =>
            {
                ExampleUnitOptions.Clear();
                ExampleUnitOptions.AddRange(IngredientUnitOption.For(ingredient));
                ExampleUnit = ExampleUnitOptions.FirstOrDefault();
            });

        this.WhenAnyValue(x => x.EditingBomComponent)
            .Where(_ => !IsRefreshing)
            .Subscribe(component => ReplaceUnitOptions(BomUnitOptions, component, unit => EditingBomUnit = unit, keepUnitId: null));

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.ExampleIngredient, x => x.ExampleQuantity, x => x.ExampleUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => ExampleResult = null);

        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category =>
            {
                _editingCategoryId = category!.Id;
                EditingCategoryName = category.Name;
            });

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit renamed on another tab.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var ingredients = await _ingredientRepository.GetAllAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedIngredient?.Id;
            var unitOfMeasureId = EditingUnitOfMeasure?.Id;
            var bomComponentId = EditingBomComponent?.Id;
            var bomUnitId = EditingBomUnit?.UnitId;
            var exampleIngredientId = ExampleIngredient?.Id;
            var exampleUnitId = ExampleUnit?.UnitId;
            var purchaseUnitId = EditingPurchaseUnit?.UnitId;
            var selectedUnitId = SelectedUnitRow?.Unit.Id;

            ApplyRefresh(() =>
            {
                MergeCategories(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _ingredientCache.MergeFrom(ingredients);

                EditingUnitOfMeasure = UnitsOfMeasure.FirstOrDefault(u => u.Id == unitOfMeasureId);

                var previousBomComponent = EditingBomComponent;
                EditingBomComponent = Ingredients.FirstOrDefault(i => i.Id == bomComponentId);
                if (!ReferenceEquals(previousBomComponent, EditingBomComponent))
                {
                    ReplaceUnitOptions(BomUnitOptions, EditingBomComponent, unit => EditingBomUnit = unit, keepUnitId: bomUnitId);
                }

                var previouslySelected = SelectedIngredient;
                SelectedIngredient = Ingredients.FirstOrDefault(i => i.Id == selectedId);
                if (!ReferenceEquals(previouslySelected, SelectedIngredient) && SelectedIngredient is not null)
                {
                    // Its units changed (a unit renamed on another tab): rebuild what shows them,
                    // keeping the purchase unit picked.
                    RebuildUnitViews(SelectedIngredient);
                    EditingPurchaseUnit = PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == purchaseUnitId);
                    SelectedUnitRow = UnitRows.FirstOrDefault(r => r.Unit.Id == selectedUnitId);
                }

                var previousExample = ExampleIngredient;
                ExampleIngredient = Ingredients.FirstOrDefault(i => i.Id == exampleIngredientId);
                if (!ReferenceEquals(previousExample, ExampleIngredient))
                {
                    ExampleUnitOptions.Clear();
                    ExampleUnitOptions.AddRange(IngredientUnitOption.For(ExampleIngredient));
                    ExampleUnit = ExampleUnitOptions.FirstOrDefault(o => o.UnitId == exampleUnitId) ?? ExampleUnitOptions.FirstOrDefault();
                    ExampleResult = null;
                }
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedIngredient = null;
            _editingId = 0;
            CanChangeSource = true;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingSource = IngredientSource.Purchased;
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
                WeightLossPercent = EditingSource == IngredientSource.Made ? EditingWeightLossPercent : null,
            };

            var validation = _ingredientValidator.Validate(ingredient);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _ingredientRepository.AddAsync(ingredient, CancellationToken.None);
                await LoadCommand.Execute();

                // Straight into the new ingredient, so its units and price can be added next.
                SelectedIngredient = Ingredients.FirstOrDefault(i => i.Id == ingredient.Id);
                return;
            }

            await _ingredientRepository.UpdateAsync(ingredient, CancellationToken.None);
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

        AddUnitCommand = ReactiveCommand.Create(
            () =>
            {
                _editingUnitId = 0;
                UnitFormTitle = "Add Alternative Unit";
                EditingUnitOfMeasure = null;
                EditingUnitGramsPerUnit = null;
                EditingUnitDescription = string.Empty;
                IsUnitFormOpen = true;
            },
            this.WhenAnyValue(x => x.CanAddUnit));

        var hasSelectedUnitRow = this.WhenAnyValue(x => x.SelectedUnitRow).Select(row => row is not null);

        EditUnitCommand = ReactiveCommand.Create(
            () =>
            {
                var row = SelectedUnitRow!;
                _editingUnitId = row.Unit.Id;
                UnitFormTitle = "Edit Alternative Unit";
                EditingUnitOfMeasure = UnitsOfMeasure.FirstOrDefault(u => u.Id == row.Unit.UnitOfMeasureId);
                EditingUnitGramsPerUnit = row.Unit.GramsPerUnit;
                EditingUnitDescription = row.Unit.Description ?? string.Empty;
                IsUnitFormOpen = true;
            },
            hasSelectedUnitRow);

        DeleteUnitCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Remove '{SelectedUnitRow!.UnitName}' from '{SelectedIngredient?.Name}'?", async () =>
            {
                await _unitRepository.DeleteAsync(SelectedUnitRow!.Unit.Id, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            }),
            hasSelectedUnitRow);

        SaveUnitCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var unit = new IngredientUnit
            {
                Id = _editingUnitId,
                IngredientId = SelectedIngredient!.Id,
                UnitOfMeasureId = EditingUnitOfMeasure?.Id ?? 0,
                GramsPerUnit = EditingUnitGramsPerUnit ?? 0m,
                Description = string.IsNullOrWhiteSpace(EditingUnitDescription) ? null : EditingUnitDescription.Trim(),
            };

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingUnitId == 0)
            {
                await _unitRepository.AddAsync(unit, CancellationToken.None);
            }
            else
            {
                await _unitRepository.UpdateAsync(unit, CancellationToken.None);
            }

            CloseUnitForm();
            await RefreshSelectedIngredientAsync();
        }));

        CancelUnitCommand = ReactiveCommand.Create(CloseUnitForm);

        SavePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var latest = PriceHistory.FirstOrDefault()?.Entry;
                var unitId = EditingPurchaseUnit?.UnitId;
                if (latest is not null && latest.IngredientUnitId == unitId && !PriceHistoryRules.IsNewPrice(latest.Price, EditingPrice))
                {
                    return; // Nothing changed.
                }

                var entry = new IngredientPriceHistory
                {
                    IngredientId = SelectedIngredient!.Id,
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    IngredientUnitId = unitId,
                    Price = EditingPrice ?? 0m,
                };

                var validation = _priceHistoryValidator.Validate(entry);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                await _priceHistoryRepository.AddAsync(entry, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            }),
            this.WhenAnyValue(x => x.SelectedIngredient, x => x.EditingPurchaseUnit,
                (ingredient, unit) => ingredient is { Source: IngredientSource.Purchased } && unit is not null));

        ResetPriceCommand = ReactiveCommand.Create(ResetPurchaseSetup);

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var ingredient = ExampleIngredient!;
                var unit = ExampleUnit!;
                var grams = UnitConversionRules.ToBaseQuantity(ExampleQuantity, unit.Unit?.GramsPerUnit);
                ExampleResult = new UsageExampleResult(
                    $"Cost for {ExampleQuantity:#,0.####} {unit.Abbreviation} of {ingredient.Name}",
                    grams,
                    ingredient.CostPerGram,
                    grams * ingredient.CostPerGram);
            },
            this.WhenAnyValue(x => x.ExampleIngredient, x => x.ExampleUnit, x => x.ExampleQuantity,
                (ingredient, unit, quantity) => ingredient is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        AddBomLineCommand = ReactiveCommand.Create(
            () => BomLineEdits.Add(new IngredientBomLineEdit(EditingBomComponent!, EditingBomUnit!, EditingBomQuantity)),
            this.WhenAnyValue(x => x.EditingBomComponent, x => x.EditingBomUnit, x => x.EditingBomQuantity,
                (component, unit, quantity) => component is not null && unit is not null && quantity > 0));

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => { BomLineEdits.Remove(SelectedBomLineEdit!); },
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));

        SaveBomCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var lines = BomLineEdits.Select(l => (l.Component.Id, l.Unit.UnitId, l.Quantity)).ToList();
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
        CanChangeSource = false;
        EditingName = ingredient.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == ingredient.IngredientCategoryId);
        EditingSource = ingredient.Source;
        EditingWeightLossPercent = ingredient.WeightLossPercent;
    }

    /// <summary>The Alternative Units rows and the purchase unit choices, from the ingredient's own units.</summary>
    private void RebuildUnitViews(Ingredient? ingredient)
    {
        SelectedUnitRow = null;
        UnitRows.Clear();
        UnitRows.AddRange((ingredient?.Units ?? []).Select(u => new IngredientUnitRow(u)));
        CanAddUnit = ingredient is not null && UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;

        PurchaseUnitOptions.Clear();
        if (ingredient is not null)
        {
            PurchaseUnitOptions.AddRange(IngredientUnitOption.For(ingredient));
        }
    }

    /// <summary>
    /// Fills a unit picker with <paramref name="ingredient"/>'s units and picks the one with
    /// <paramref name="keepUnitId"/>, falling back to the gram.
    /// </summary>
    private static void ReplaceUnitOptions(
        ObservableCollection<IngredientUnitOption> options, Ingredient? ingredient, Action<IngredientUnitOption?> pick, int? keepUnitId)
    {
        pick(null);
        options.Clear();
        if (ingredient is null)
        {
            return;
        }

        options.AddRange(IngredientUnitOption.For(ingredient));
        pick(options.FirstOrDefault(o => o.UnitId == keepUnitId) ?? options[0]);
    }

    private void CloseUnitForm()
    {
        IsUnitFormOpen = false;
        _editingUnitId = 0;
    }

    /// <summary>Puts the Purchase Cost Setup back to the latest price: its unit and amount, or the largest unit and no price.</summary>
    private void ResetPurchaseSetup()
    {
        var latest = PriceHistory.FirstOrDefault()?.Entry;
        EditingPurchaseUnit = latest is not null
            ? PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == latest.IngredientUnitId)
            : PurchaseUnitOptions.Skip(1).FirstOrDefault() ?? PurchaseUnitOptions.FirstOrDefault();
        EditingPrice = latest?.Price;
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
            BomLineEdits.Add(new IngredientBomLineEdit(line.ComponentIngredient!, new IngredientUnitOption(line.IngredientUnit), line.Quantity));
        }
    }

    private async Task LoadPriceHistoryAsync(Ingredient? ingredient)
    {
        PriceHistory.Clear();

        if (ingredient is { Source: IngredientSource.Purchased })
        {
            var entries = await _priceHistoryRepository.GetForIngredientAsync(ingredient.Id, CancellationToken.None);
            PriceHistory.AddRange(entries.Select(e => new IngredientPriceRow(e)));
        }

        LastUpdatedText = PriceHistory.FirstOrDefault()?.Date.ToString("MMM d, yyyy") ?? "—";
        ResetPurchaseSetup();
    }

    /// <summary>
    /// Re-fetches the selected ingredient (its units or CostPerGram just changed) and pushes it
    /// back into the grid's cache and the selection, which also reloads its details.
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
