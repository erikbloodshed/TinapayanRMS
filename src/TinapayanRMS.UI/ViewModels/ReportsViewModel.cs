using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row of the Product Cost Summary: a product's cost and price for one sellable unit, computed live from today's data,
/// never historized.
/// </summary>
public sealed class ProductCostRow(Product product, ProductCostSummary costs)
{
    public int ProductId => product.Id;

    public string ProductName => product.Name;

    public string Stage => ProductFolderNode.StageTitle(product.Stage);

    public string Category => product.Category?.Name ?? ProductFolderNode.UncategorizedTitle;

    /// <summary>
    /// How many pieces the sellable unit holds, which every figure after it is for; none for a composite, which is one unit
    /// of other products, however many pieces those hold.
    /// </summary>
    public int? Pieces => product.IsComposite ? null : product.QuantityRequired;

    /// <summary>The recipe cost, the Costing tab's Sub-assemblies (ingredients) row: a composite's is what its products bring.</summary>
    public decimal IngredientsCost => costs.SubAssemblies;

    /// <summary>All of the packaging, the Costing tab's Packaging row: a composite's products' too when it includes theirs.</summary>
    public decimal PackagingCost => costs.Packaging;

    /// <summary>The Labor Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal LaborCost => costs.Labor;

    /// <summary>The Overhead Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal OverheadCost => costs.Overhead;

    /// <summary>The Admin Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal AdministrativeCost => costs.Administrative;

    /// <summary>The five costs before it added up: ingredients, packaging, labor, overhead and administrative.</summary>
    public decimal BaseCost => costs.BaseCost;

    /// <summary>The selling price the costing works out, the Costing tab's Total Selling Price: a suggestion beside <see cref="CurrentPrice"/>.</summary>
    public decimal SuggestedPrice => costs.SellingPrice;

    /// <summary>Null when no current price is entered.</summary>
    public decimal? CurrentPrice => costs.CurrentPrice;

    /// <summary>The markup the current price earns, as the Products header shows it; null without a current price.</summary>
    public decimal? MarkupPercent => costs.CurrentMarkupPercent;
}

/// <summary>
/// A row of the Raw Materials Price Summary: a purchased material's latest price from its price history, and how far
/// that moved its cost from the price before it. Blank figures for a material with no price yet.
/// </summary>
public sealed class RawMaterialPriceSummaryRow
{
    /// <param name="history">The material's price entries, newest first.</param>
    public RawMaterialPriceSummaryRow(RawMaterial material, IReadOnlyList<RawMaterialPriceHistory> history)
    {
        RawMaterialId = material.Id;
        Name = material.Name;
        Type = material.Type == RawMaterialType.Ingredient ? "Ingredient" : "Packaging";
        Category = material.Category?.Name ?? string.Empty;

        if (history.Count == 0)
        {
            UnitName = string.Empty;
            return;
        }

        var latest = history[0];
        UnitName = latest.Unit?.UnitOfMeasure?.Name ?? BaseUnitText.NameOf(material);
        Price = latest.Price;
        PriceDate = latest.Date.ToDateTime(TimeOnly.MinValue);

        // Per base unit, since the two prices can be for different units (a sack, then a kilo).
        if (history.Count > 1)
        {
            var costNow = CostPerBaseUnit(latest);
            var costBefore = CostPerBaseUnit(history[1]);
            ChangePercent = CostingRules.PercentOf(costNow - costBefore, costBefore);
        }
    }

    public int RawMaterialId { get; }

    public string Name { get; }

    public string Type { get; }

    public string Category { get; }

    /// <summary>What the price is for: one of the material's units, or its base unit. Empty with no price.</summary>
    public string UnitName { get; }

    /// <summary>The latest price, per <see cref="UnitName"/>; null with no price.</summary>
    public decimal? Price { get; }

    /// <summary>The change in cost per base unit from the price before the latest; null with fewer than two prices.</summary>
    public decimal? ChangePercent { get; }

    /// <summary>The day the latest price started; null with no price.</summary>
    public DateTime? PriceDate { get; }

    private static decimal CostPerBaseUnit(RawMaterialPriceHistory entry) =>
        UnitConversionRules.CostPerBaseUnit(entry.Price, entry.Unit?.BaseUnitsPerUnit);
}

/// <summary>
/// The Reports screen: summaries across every product and every purchased raw material, each on a tab of its own. A load
/// that fails says why in the screen's error notice (ViewModelBase.LoadSafelyAsync).
/// </summary>
public sealed class ReportsViewModel : ViewModelBase, IDisposable
{
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IProductRepository _productRepository;
    private readonly SavedProductReader _savedProducts;

    public ObservableCollection<ProductCostRow> ProductCosts { get; } = [];

    public ObservableCollection<RawMaterialPriceSummaryRow> RawMaterialPrices { get; } = [];

    /// <summary>A Product Cost Summary row's double-click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products the Product Cost Summary asks to open, by id. The main window, which owns the tabs, subscribes and
    /// opens each on the Products screen, as it does for the Used By tiles on Raw Materials and Sub-Assemblies.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>A Raw Materials Price Summary row's double-click: asks for the material with this id to be opened, through <see cref="RawMaterialOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenRawMaterialCommand { get; }

    private readonly Subject<int> _rawMaterialOpenRequests = new();

    /// <summary>The raw materials the Raw Materials Price Summary asks to open, by id, which the main window opens on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    /// <summary>Runs whenever the tab is shown (MainWindow), so both summaries are from current prices.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReportsViewModel(
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialRepository rawMaterialRepository,
        IProductRepository productRepository,
        SavedProductReader savedProducts,
        ILogger logger)
        : base(logger)
    {
        _priceHistoryRepository = priceHistoryRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _productRepository = productRepository;
        _savedProducts = savedProducts;

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);
        OpenRawMaterialCommand = ReactiveCommand.Create<int>(_rawMaterialOpenRequests.OnNext);

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // Off the UI thread, as Products and Raw Materials read their details, and both summaries at once.
            var productCostsRead = Task.Run(ReadProductCostsAsync);
            var rawMaterialPricesRead = Task.Run(ReadRawMaterialPricesAsync);
            var productCosts = await productCostsRead;
            var rawMaterialPrices = await rawMaterialPricesRead;

            ProductCosts.Clear();
            ProductCosts.AddRange(productCosts);
            RawMaterialPrices.Clear();
            RawMaterialPrices.AddRange(rawMaterialPrices);
        }));
    }

    /// <summary>
    /// Every product as it's saved, priced as the Products screen prices it once opened (SavedProductReader, which the
    /// screen and Print read through too). All of them are read together, in the same dozen or so queries however many
    /// products there are (2026-10-03: they were read a product at a time, nine or so queries each, several products at
    /// once). The rows stay in the list's order, by name.
    /// </summary>
    private async Task<List<ProductCostRow>> ReadProductCostsAsync()
    {
        var products = await _productRepository.GetAllAsync(CancellationToken.None);
        var saved = await _savedProducts.ReadAllAsync(products, CostBasis.Buffered, CancellationToken.None);
        return [.. saved.Select(s => new ProductCostRow(s.Product, s.Costs))];
    }

    /// <summary>The purchased materials, ingredients and packaging alike, by name: a made or linked ingredient has no price history.</summary>
    private async Task<List<RawMaterialPriceSummaryRow>> ReadRawMaterialPricesAsync()
    {
        var materials = await _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var history = (await _priceHistoryRepository.GetAllAsync(CancellationToken.None)).ToLookup(h => h.RawMaterialId);

        return
        [
            .. materials
                .Where(m => m.Source == RawMaterialSource.Purchased)
                .OrderBy(m => m.Name)
                .Select(m => new RawMaterialPriceSummaryRow(m, [.. history[m.Id]])),
        ];
    }

    public void Dispose()
    {
        _productOpenRequests.Dispose();
        _rawMaterialOpenRequests.Dispose();
    }
}
