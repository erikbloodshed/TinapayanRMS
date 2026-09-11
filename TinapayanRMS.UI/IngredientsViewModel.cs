using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI;

public class IngredientsViewModel : ReactiveObject
{
    private readonly IIngredientRepository _ingredientRepository;
    private readonly IIngredientPriceHistoryRepository _priceHistoryRepository;
    private readonly SourceCache<Ingredient, int> _ingredientCache = new(i => i.Id);
    private readonly ReadOnlyObservableCollection<Ingredient> _ingredients;
    private readonly SourceCache<IngredientPriceHistory, int> _priceHistoryCache = new(h => h.Id);
    private readonly ReadOnlyObservableCollection<IngredientPriceHistory> _priceHistory;

    public ReadOnlyObservableCollection<Ingredient> Ingredients => _ingredients;

    public ReadOnlyObservableCollection<IngredientPriceHistory> PriceHistory => _priceHistory;

    public ObservableCollection<IngredientCategory> Categories { get; } = [];

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

    private readonly ObservableAsPropertyHelper<bool> _isEditingPurchased;
    public bool IsEditingPurchased => _isEditingPurchased.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPriceHistory;
    public bool ShowPriceHistory => _showPriceHistory.Value;

    private IngredientPriceHistory? _selectedPriceHistoryEntry;
    public IngredientPriceHistory? SelectedPriceHistoryEntry
    {
        get => _selectedPriceHistoryEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceHistoryEntry, value);
    }

    private DateTime _editingPriceDate = DateTime.Today;
    public DateTime EditingPriceDate
    {
        get => _editingPriceDate;
        set => this.RaiseAndSetIfChanged(ref _editingPriceDate, value);
    }

    private decimal _editingPrice;
    public decimal EditingPrice
    {
        get => _editingPrice;
        set => this.RaiseAndSetIfChanged(ref _editingPrice, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }

    public IngredientsViewModel(
        IIngredientRepository ingredientRepository,
        IIngredientPriceHistoryRepository priceHistoryRepository,
        ILookupRepository<IngredientCategory> categoryRepository,
        ILookupRepository<UnitOfMeasure> unitOfMeasureRepository)
    {
        _ingredientRepository = ingredientRepository;
        _priceHistoryRepository = priceHistoryRepository;

        _ingredientCache.Connect()
            .SortAndBind(out _ingredients, SortExpressionComparer<Ingredient>.Ascending(i => i.Name))
            .Subscribe();

        _priceHistoryCache.Connect()
            .SortAndBind(out _priceHistory, SortExpressionComparer<IngredientPriceHistory>.Descending(h => h.Date).ThenByDescending(h => h.Id))
            .Subscribe();

        _isEditingPurchased = this.WhenAnyValue(x => x.EditingSource)
            .Select(source => source == IngredientSource.Purchased)
            .ToProperty(this, x => x.IsEditingPurchased);

        _showPriceHistory = this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => ingredient is { Source: IngredientSource.Purchased })
            .ToProperty(this, x => x.ShowPriceHistory);

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(ingredient => ingredient is not null)
            .Subscribe(ingredient => LoadIntoForm(ingredient!));

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Subscribe(_ =>
            {
                SelectedPriceHistoryEntry = null;
                EditingPriceDate = DateTime.Today;
                EditingPrice = 0m;
            });

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Select(ingredient => Observable.FromAsync(() => LoadPriceHistoryAsync(ingredient)))
            .Switch()
            .Subscribe();

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await categoryRepository.GetAllAsync(CancellationToken.None);
            Categories.Clear();
            Categories.AddRange(categories.OrderBy(c => c.Name));

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            PurchaseUnits.Clear();
            PurchaseUnits.AddRange(units.OrderBy(u => u.Name));

            var ingredients = await _ingredientRepository.GetAllAsync(CancellationToken.None);
            _ingredientCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(ingredients);
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
        });

        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var ingredient = new Ingredient
            {
                Id = _editingId,
                Name = EditingName,
                IngredientCategoryId = EditingCategory?.Id ?? 0,
                Source = EditingSource,
                PurchaseUnitId = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnit?.Id : null,
                PurchaseUnitQty = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnitQty : null,
            };

            if (_editingId == 0)
            {
                await _ingredientRepository.AddAsync(ingredient, CancellationToken.None);
            }
            else
            {
                await _ingredientRepository.UpdateAsync(ingredient, CancellationToken.None);
            }

            await LoadCommand.Execute();
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _ingredientRepository.DeleteAsync(SelectedIngredient!.Id, CancellationToken.None);
                SelectedIngredient = null;
                await LoadCommand.Execute();
            },
            this.WhenAnyValue(x => x.SelectedIngredient).Select(ingredient => ingredient is not null));

        AddPriceCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _priceHistoryRepository.AddAsync(new IngredientPriceHistory
                {
                    IngredientId = SelectedIngredient!.Id,
                    Date = DateOnly.FromDateTime(EditingPriceDate),
                    Price = EditingPrice,
                }, CancellationToken.None);

                await RefreshSelectedIngredientAsync();
            },
            this.WhenAnyValue(x => x.SelectedIngredient).Select(ingredient => ingredient is { Source: IngredientSource.Purchased }));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceHistoryEntry!.Id, CancellationToken.None);
                await RefreshSelectedIngredientAsync();
            },
            this.WhenAnyValue(x => x.SelectedPriceHistoryEntry).Select(entry => entry is not null));
    }

    private void LoadIntoForm(Ingredient ingredient)
    {
        _editingId = ingredient.Id;
        EditingName = ingredient.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == ingredient.IngredientCategoryId);
        EditingSource = ingredient.Source;
        EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == ingredient.PurchaseUnitId);
        EditingPurchaseUnitQty = ingredient.PurchaseUnitQty;
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
