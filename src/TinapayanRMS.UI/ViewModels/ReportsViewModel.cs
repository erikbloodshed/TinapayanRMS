using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>Which material list the price-trend chart's material picker shows.</summary>
public enum PriceTrendMaterialKind
{
    Ingredient,
    Packaging,
}

/// <summary>One point on the price-trend chart: a purchase-unit price on a given date.</summary>
public class PricePoint(DateTime date, decimal price)
{
    public DateTime Date { get; } = date;

    public decimal Price { get; } = price;
}

/// <summary>One bar-group on the margin overview chart — a product's cost and selling price, computed live from today's data, never historized.</summary>
public class MarginRow(string productName, decimal baseCost, decimal sellingPrice)
{
    public string ProductName { get; } = productName;

    public decimal BaseCost { get; } = baseCost;

    public decimal SellingPrice { get; } = sellingPrice;
}

public class ReportsViewModel : ReactiveObject
{
    private readonly IIngredientPriceHistoryRepository _ingredientPriceHistoryRepository;
    private readonly IPackagingPriceHistoryRepository _packagingPriceHistoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;

    // True while LoadCommand merges the material lists, so re-pointing a selection doesn't also
    // start its own chart reload.
    private bool _isRefreshing;

    public ObservableCollection<Ingredient> Ingredients { get; } = [];

    public ObservableCollection<PackagingMaterial> PackagingMaterials { get; } = [];

    public ObservableCollection<PricePoint> PricePoints { get; } = [];

    public ObservableCollection<MarginRow> MarginRows { get; } = [];

    public IReadOnlyList<PriceTrendMaterialKind> MaterialKindOptions { get; } = Enum.GetValues<PriceTrendMaterialKind>();

    private PriceTrendMaterialKind _selectedMaterialKind = PriceTrendMaterialKind.Ingredient;
    public PriceTrendMaterialKind SelectedMaterialKind
    {
        get => _selectedMaterialKind;
        set => this.RaiseAndSetIfChanged(ref _selectedMaterialKind, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isIngredientKind;
    public bool IsIngredientKind => _isIngredientKind.Value;

    private readonly ObservableAsPropertyHelper<bool> _isPackagingKind;
    public bool IsPackagingKind => _isPackagingKind.Value;

    private Ingredient? _selectedIngredient;
    public Ingredient? SelectedIngredient
    {
        get => _selectedIngredient;
        set => this.RaiseAndSetIfChanged(ref _selectedIngredient, value);
    }

    private PackagingMaterial? _selectedPackagingMaterial;
    public PackagingMaterial? SelectedPackagingMaterial
    {
        get => _selectedPackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedPackagingMaterial, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReportsViewModel(
        IIngredientPriceHistoryRepository ingredientPriceHistoryRepository,
        IPackagingPriceHistoryRepository packagingPriceHistoryRepository,
        IIngredientRepository ingredientRepository,
        IPackagingMaterialRepository packagingMaterialRepository,
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository)
    {
        _ingredientPriceHistoryRepository = ingredientPriceHistoryRepository;
        _packagingPriceHistoryRepository = packagingPriceHistoryRepository;
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;

        _isIngredientKind = this.WhenAnyValue(x => x.SelectedMaterialKind)
            .Select(kind => kind == PriceTrendMaterialKind.Ingredient)
            .ToProperty(this, x => x.IsIngredientKind);

        _isPackagingKind = this.WhenAnyValue(x => x.SelectedMaterialKind)
            .Select(kind => kind == PriceTrendMaterialKind.Packaging)
            .ToProperty(this, x => x.IsPackagingKind);

        // Skips while LoadCommand merges, which reloads the chart itself once it's done.
        this.WhenAnyValue(x => x.SelectedMaterialKind, x => x.SelectedIngredient, x => x.SelectedPackagingMaterial)
            .Where(_ => !_isRefreshing)
            .Select(_ => Observable.FromAsync(LoadPricePointsAsync))
            .Switch()
            .Subscribe();

        // Runs whenever the tab is shown (MainWindow). It merges the material lists rather than
        // replacing them, so the chosen ingredient and material survive, and redraws both charts
        // from current prices.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var ingredients = await ingredientRepository.GetAllAsync(CancellationToken.None);
            var packagingMaterials = await packagingMaterialRepository.GetAllAsync(CancellationToken.None);
            var marginRows = await CalculateMarginRowsAsync();

            var ingredientId = SelectedIngredient?.Id;
            var packagingMaterialId = SelectedPackagingMaterial?.Id;

            _isRefreshing = true;
            try
            {
                Ingredients.MergeFrom([.. ingredients.Where(i => i.Source == IngredientSource.Purchased).OrderBy(i => i.Name)], i => i.Id);
                PackagingMaterials.MergeFrom([.. packagingMaterials.OrderBy(m => m.Name)], m => m.Id);

                // The first one only until something has been chosen.
                SelectedIngredient = Ingredients.FirstOrDefault(i => i.Id == ingredientId) ?? Ingredients.FirstOrDefault();
                SelectedPackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == packagingMaterialId) ?? PackagingMaterials.FirstOrDefault();

                MarginRows.Clear();
                MarginRows.AddRange(marginRows);
            }
            finally
            {
                _isRefreshing = false;
            }

            await LoadPricePointsAsync();
        });
    }

    private async Task LoadPricePointsAsync()
    {
        PricePoints.Clear();

        IReadOnlyList<(DateOnly Date, decimal Price)> entries = SelectedMaterialKind == PriceTrendMaterialKind.Ingredient
            ? SelectedIngredient is null
                ? []
                : [.. (await _ingredientPriceHistoryRepository.GetForIngredientAsync(SelectedIngredient.Id, CancellationToken.None)).Select(h => (h.Date, h.Price))]
            : SelectedPackagingMaterial is null
                ? []
                : [.. (await _packagingPriceHistoryRepository.GetForPackagingMaterialAsync(SelectedPackagingMaterial.Id, CancellationToken.None)).Select(h => (h.Date, h.Price))];

        // Repositories return newest-first (for a price-history grid); a trend line needs
        // oldest-first so it reads left to right.
        foreach (var (date, price) in entries.OrderBy(e => e.Date))
        {
            PricePoints.Add(new PricePoint(date.ToDateTime(TimeOnly.MinValue), price));
        }
    }

    private async Task<List<MarginRow>> CalculateMarginRowsAsync()
    {
        var marginRows = new List<MarginRow>();

        var products = await _productRepository.GetAllAsync(CancellationToken.None);
        foreach (var product in products)
        {
            var usages = await _usageRepository.GetEffectiveForProductAsync(product.Id, CancellationToken.None);
            var usageCosts = usages
                .Select(u => ProductRules.CalculateUsageCostPerPiece(u.SubAssembly!.TotalCost, u.WeightPerPiece, u.SubAssembly.TotalWeight))
                .ToList();

            var lines = await _packagingLineRepository.GetEffectiveForProductAsync(product.Id, CancellationToken.None);
            var packagingCosts = lines
                .Select(l => ProductRules.CalculatePackagingLineCostPerPiece(
                    l.PackagingMaterial!.CostPerBaseUnit, l.QtyPerPiece, l.PackagingMaterialUnit?.BaseUnitsPerUnit))
                .ToList();

            var materialCostPerPiece = ProductRules.CalculateMaterialCostPerPiece(usageCosts, packagingCosts);

            var costingParameters = await _costingParametersRepository.GetForProductAsync(product.Id, CancellationToken.None);
            var (baseCost, _, sellingPrice) = CostingRules.CalculateSellingPrice(
                materialCostPerPiece,
                costingParameters?.LaborCost ?? 0m,
                costingParameters?.OverheadCost ?? 0m,
                costingParameters?.MarginPercent ?? 0m,
                costingParameters?.VatPercent ?? 0m);

            marginRows.Add(new MarginRow(product.Name, baseCost, sellingPrice));
        }

        return marginRows;
    }
}
