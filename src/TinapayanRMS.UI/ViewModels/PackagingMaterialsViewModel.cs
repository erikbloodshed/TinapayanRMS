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

public class PackagingMaterialsViewModel : ViewModelBase
{
    private readonly IPackagingMaterialRepository _packagingMaterialRepository;
    private readonly IPackagingPriceHistoryRepository _priceHistoryRepository;
    private readonly IPackagingCategoryRepository _categoryRepository;
    private readonly IValidator<PackagingMaterial> _packagingMaterialValidator;
    private readonly IValidator<PackagingPriceHistory> _priceHistoryValidator;
    private readonly IValidator<PackagingCategory> _categoryValidator;
    private readonly SourceCache<PackagingMaterial, int> _packagingMaterialCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<PackagingMaterial> _packagingMaterials;
    private readonly SourceCache<PackagingPriceHistory, int> _priceHistoryCache = new(h => h.Id);
    private readonly ReadOnlyObservableCollection<PackagingPriceHistory> _priceHistory;
    private readonly SourceCache<PackagingCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<PackagingCategory> _categories;

    public ReadOnlyObservableCollection<PackagingMaterial> PackagingMaterials => _packagingMaterials;

    public ReadOnlyObservableCollection<PackagingPriceHistory> PriceHistory => _priceHistory;

    public ReadOnlyObservableCollection<PackagingCategory> Categories => _categories;

    public ObservableCollection<UnitOfMeasure> StandardUnits { get; } = [];

    public ObservableCollection<UnitOfMeasure> PurchaseUnits { get; } = [];

    private PackagingMaterial? _selectedPackagingMaterial;
    public PackagingMaterial? SelectedPackagingMaterial
    {
        get => _selectedPackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedPackagingMaterial, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private PackagingCategory? _editingCategory;
    public PackagingCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private UnitOfMeasure? _editingStandardUnit;
    public UnitOfMeasure? EditingStandardUnit
    {
        get => _editingStandardUnit;
        set => this.RaiseAndSetIfChanged(ref _editingStandardUnit, value);
    }

    private UnitOfMeasure? _editingPurchaseUnit;
    public UnitOfMeasure? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    private decimal _editingPurchaseUnitQty;
    public decimal EditingPurchaseUnitQty
    {
        get => _editingPurchaseUnitQty;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnitQty, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _showPriceHistory;
    public bool ShowPriceHistory => _showPriceHistory.Value;

    private PackagingPriceHistory? _selectedPriceHistoryEntry;
    public PackagingPriceHistory? SelectedPriceHistoryEntry
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

    // Category management (embedded panel, independent of the selected packaging material).
    private PackagingCategory? _selectedCategory;
    public PackagingCategory? SelectedCategory
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
    public ReactiveCommand<RxVoid, RxVoid> AddPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCategoryCommand { get; }

    public PackagingMaterialsViewModel(
        IPackagingMaterialRepository packagingMaterialRepository,
        IPackagingPriceHistoryRepository priceHistoryRepository,
        IPackagingCategoryRepository categoryRepository,
        IValidator<PackagingMaterial> packagingMaterialValidator,
        IValidator<PackagingPriceHistory> priceHistoryValidator,
        IValidator<PackagingCategory> categoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _packagingMaterialRepository = packagingMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _categoryRepository = categoryRepository;
        _packagingMaterialValidator = packagingMaterialValidator;
        _priceHistoryValidator = priceHistoryValidator;
        _categoryValidator = categoryValidator;

        _packagingMaterialCache.Connect()
            .SortAndBind(out _packagingMaterials, SortExpressionComparer<PackagingMaterial>.Ascending(p => p.Name))
            .Subscribe();

        _priceHistoryCache.Connect()
            .SortAndBind(out _priceHistory, SortExpressionComparer<PackagingPriceHistory>.Descending(h => h.Date).ThenByDescending(h => h.Id))
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<PackagingCategory>.Ascending(c => c.Name))
            .Subscribe();

        _showPriceHistory = this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(packagingMaterial => packagingMaterial is not null)
            .ToProperty(this, x => x.ShowPriceHistory);

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Where(packagingMaterial => packagingMaterial is not null)
            .Subscribe(packagingMaterial => LoadIntoForm(packagingMaterial!));

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Subscribe(_ =>
            {
                SelectedPriceHistoryEntry = null;
                EditingPriceDate = DateTime.Today;
                EditingPrice = 0m;
            });

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(packagingMaterial => Observable.FromAsync(() => LoadPriceHistoryAsync(packagingMaterial)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null)
            .Subscribe(category =>
            {
                _editingCategoryId = category!.Id;
                EditingCategoryName = category.Name;
            });

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            _categoryCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(categories);
            });

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var orderedUnits = units.OrderBy(u => u.Name).ToList();

            StandardUnits.Clear();
            StandardUnits.AddRange(orderedUnits);

            PurchaseUnits.Clear();
            PurchaseUnits.AddRange(orderedUnits);

            var packagingMaterials = await _packagingMaterialRepository.GetAllAsync(CancellationToken.None);
            _packagingMaterialCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(packagingMaterials);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedPackagingMaterial = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingStandardUnit = StandardUnits.FirstOrDefault();
            EditingPurchaseUnit = PurchaseUnits.FirstOrDefault();
            EditingPurchaseUnitQty = 0;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var packagingMaterial = new PackagingMaterial
            {
                Id = _editingId,
                Name = EditingName,
                PackagingCategoryId = EditingCategory?.Id ?? 0,
                StandardUnitId = EditingStandardUnit?.Id ?? 0,
                PurchaseUnitId = EditingPurchaseUnit?.Id ?? 0,
                PurchaseUnitQty = EditingPurchaseUnitQty,
            };

            var validation = _packagingMaterialValidator.Validate(packagingMaterial);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _packagingMaterialRepository.AddAsync(packagingMaterial, CancellationToken.None);
            }
            else
            {
                await _packagingMaterialRepository.UpdateAsync(packagingMaterial, CancellationToken.None);
            }

            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedPackagingMaterial!.Name}'? This can't be undone.", async () =>
            {
                await _packagingMaterialRepository.DeleteAsync(SelectedPackagingMaterial!.Id, CancellationToken.None);
                SelectedPackagingMaterial = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(packagingMaterial => packagingMaterial is not null));

        AddPriceCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var entry = new PackagingPriceHistory
                {
                    PackagingMaterialId = SelectedPackagingMaterial!.Id,
                    Date = DateOnly.FromDateTime(EditingPriceDate),
                    Price = EditingPrice,
                };

                var validation = _priceHistoryValidator.Validate(entry);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                await _priceHistoryRepository.AddAsync(entry, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(packagingMaterial => packagingMaterial is not null));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceHistoryEntry!.Id, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPriceHistoryEntry).Select(entry => entry is not null));

        NewCategoryCommand = ReactiveCommand.Create(() =>
        {
            SelectedCategory = null;
            _editingCategoryId = 0;
            EditingCategoryName = string.Empty;
        });

        SaveCategoryCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new PackagingCategory { Id = _editingCategoryId, Name = EditingCategoryName };

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

            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            _categoryCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(categories);
            });
            SelectedCategory = null;
        }));

        DeleteCategoryCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
                _categoryCache.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(categories);
                });
                SelectedCategory = null;
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
    }

    private void LoadIntoForm(PackagingMaterial packagingMaterial)
    {
        _editingId = packagingMaterial.Id;
        EditingName = packagingMaterial.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == packagingMaterial.PackagingCategoryId);
        EditingStandardUnit = StandardUnits.FirstOrDefault(u => u.Id == packagingMaterial.StandardUnitId);
        EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == packagingMaterial.PurchaseUnitId);
        EditingPurchaseUnitQty = packagingMaterial.PurchaseUnitQty;
    }

    private async Task LoadPriceHistoryAsync(PackagingMaterial? packagingMaterial)
    {
        if (packagingMaterial is null)
        {
            _priceHistoryCache.Edit(cache => cache.Clear());
            return;
        }

        var entries = await _priceHistoryRepository.GetForPackagingMaterialAsync(packagingMaterial.Id, CancellationToken.None);
        _priceHistoryCache.Edit(cache =>
        {
            cache.Clear();
            cache.AddOrUpdate(entries);
        });
    }

    /// <summary>
    /// Re-fetches the selected packaging material (its CurrentCost just changed) and
    /// pushes it back into the grid's cache and the selection, which also re-triggers
    /// the price-history reload above.
    /// </summary>
    private async Task RefreshSelectedPackagingMaterialAsync()
    {
        if (SelectedPackagingMaterial is null)
        {
            return;
        }

        var refreshed = await _packagingMaterialRepository.GetByIdAsync(SelectedPackagingMaterial.Id, CancellationToken.None);
        if (refreshed is not null)
        {
            _packagingMaterialCache.AddOrUpdate(refreshed);
            SelectedPackagingMaterial = refreshed;
        }
    }
}
