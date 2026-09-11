using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI;

public class PackagingMaterialsViewModel : ReactiveObject
{
    private readonly IPackagingMaterialRepository _packagingMaterialRepository;
    private readonly IPackagingPriceHistoryRepository _priceHistoryRepository;
    private readonly SourceCache<PackagingMaterial, int> _packagingMaterialCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<PackagingMaterial> _packagingMaterials;
    private readonly SourceCache<PackagingPriceHistory, int> _priceHistoryCache = new(h => h.Id);
    private readonly ReadOnlyObservableCollection<PackagingPriceHistory> _priceHistory;

    public ReadOnlyObservableCollection<PackagingMaterial> PackagingMaterials => _packagingMaterials;

    public ReadOnlyObservableCollection<PackagingPriceHistory> PriceHistory => _priceHistory;

    public ObservableCollection<PackagingCategory> Categories { get; } = [];

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

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }

    public PackagingMaterialsViewModel(
        IPackagingMaterialRepository packagingMaterialRepository,
        IPackagingPriceHistoryRepository priceHistoryRepository,
        ILookupRepository<PackagingCategory> categoryRepository,
        ILookupRepository<UnitOfMeasure> unitOfMeasureRepository)
    {
        _packagingMaterialRepository = packagingMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;

        _packagingMaterialCache.Connect()
            .SortAndBind(out _packagingMaterials, SortExpressionComparer<PackagingMaterial>.Ascending(p => p.Name))
            .Subscribe();

        _priceHistoryCache.Connect()
            .SortAndBind(out _priceHistory, SortExpressionComparer<PackagingPriceHistory>.Descending(h => h.Date).ThenByDescending(h => h.Id))
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

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await categoryRepository.GetAllAsync(CancellationToken.None);
            Categories.Clear();
            Categories.AddRange(categories.OrderBy(c => c.Name));

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var orderedUnits = units.OrderBy(u => u.Name).ToList();

            StandardUnits.Clear();
            StandardUnits.AddRange(orderedUnits.Where(u => PackagingMaterialRules.IsValidStandardUnit(u.Id)));

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

        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
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

            if (_editingId == 0)
            {
                await _packagingMaterialRepository.AddAsync(packagingMaterial, CancellationToken.None);
            }
            else
            {
                await _packagingMaterialRepository.UpdateAsync(packagingMaterial, CancellationToken.None);
            }

            await LoadCommand.Execute();
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _packagingMaterialRepository.DeleteAsync(SelectedPackagingMaterial!.Id, CancellationToken.None);
                SelectedPackagingMaterial = null;
                await LoadCommand.Execute();
            },
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(packagingMaterial => packagingMaterial is not null));

        AddPriceCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _priceHistoryRepository.AddAsync(new PackagingPriceHistory
                {
                    PackagingMaterialId = SelectedPackagingMaterial!.Id,
                    Date = DateOnly.FromDateTime(EditingPriceDate),
                    Price = EditingPrice,
                }, CancellationToken.None);

                await RefreshSelectedPackagingMaterialAsync();
            },
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(packagingMaterial => packagingMaterial is not null));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceHistoryEntry!.Id, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            },
            this.WhenAnyValue(x => x.SelectedPriceHistoryEntry).Select(entry => entry is not null));
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
