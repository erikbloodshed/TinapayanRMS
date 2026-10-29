using System.Collections.ObjectModel;
using System.Reactive.Linq;
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

    /// <summary>The shop-wide Wastage %'s share of the ingredients cost, in pesos: the Costing tab's Wastage row.</summary>
    public decimal WastageCost => costs.Wastage;

    /// <summary>The Labor Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal LaborCost => costs.Labor;

    /// <summary>The Overhead Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal OverheadCost => costs.Overhead;

    /// <summary>The Admin Metric's percentage of the ingredients cost, in pesos.</summary>
    public decimal AdministrativeCost => costs.Administrative;

    /// <summary>The six costs before it added up: ingredients, wastage, packaging, labor, overhead and administrative.</summary>
    public decimal BaseCost => costs.BaseCost;

    /// <summary>The selling price the costing works out, the Costing tab's Total Selling Price: a suggestion beside <see cref="CurrentPrice"/>.</summary>
    public decimal SuggestedPrice => costs.SellingPrice;

    /// <summary>
    /// <see cref="SuggestedPrice"/> rounded up to a price the shop would charge (Settings' price rounding, 2026-10-29); the
    /// same figure without rounding.
    /// </summary>
    public decimal RoundedPrice => costs.RoundedSellingPrice;

    /// <summary>Null when no current price is entered.</summary>
    public decimal? CurrentPrice => costs.CurrentPrice;

    /// <summary>The markup the current price earns, as the Products header shows it; null without a current price.</summary>
    public decimal? MarkupPercent => costs.CurrentMarkupPercent;

    /// <summary>The markup the costing aims for, the Costing tab's Markup Metric.</summary>
    public decimal TargetMarkupPercent => costs.MarginPercent;

    /// <summary>
    /// How many percentage points the current price's markup is short of <see cref="TargetMarkupPercent"/> (2026-10-29); null
    /// when it isn't short, or there's no current price. Sorting by it puts the furthest short first.
    /// </summary>
    public decimal? ShortOfTargetMarkup => costs.IsBelowTargetMarkup ? costs.MarginPercent - costs.CurrentMarkupPercent : null;
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
/// A row of Reports' Cost Buffers (2026-10-29): a purchased material with a price, its fixed buffered cost against its dearest
/// brand now, and the buffer that leaves against the one last typed for it (RawMaterialRules.TargetBufferPercentOf). Rising
/// prices wear a buffer down, since the buffered cost doesn't follow them.
/// </summary>
public sealed class CostBufferRow(RawMaterial material)
{
    public int RawMaterialId => material.Id;

    public string Name => material.Name;

    public string Type => material.Type.ToString();

    public string Category => material.Category?.Name ?? string.Empty;

    /// <summary>The base unit the two costs are per: "g" for an ingredient.</summary>
    public string Per => BaseUnitText.AbbreviationOf(material);

    /// <summary>The cost per base unit of the brand it's costed at (its preferred brand, else the dearest): the true cost.</summary>
    public decimal DearestCost => material.CostPerBaseUnit;

    public decimal BufferedCost => material.BufferedCostPerBaseUnit;

    /// <summary>What the buffered cost now comes to over the dearest brand, in percent; below 0 once a price has passed it.</summary>
    public decimal BufferPercent => RawMaterialRules.CurrentBufferPercentOf(material) ?? 0m;

    public decimal TargetPercent => RawMaterialRules.TargetBufferPercentOf(material);

    /// <summary>The buffer has worn below the target: Re-apply Target Buffer would put it back.</summary>
    public bool IsBelowTarget => RawMaterialRules.IsBufferBelowTarget(material);

    /// <summary>How many percentage points below the target; null when it isn't below.</summary>
    public decimal? ShortBy => IsBelowTarget ? TargetPercent - BufferPercent : null;
}

/// <summary>
/// The Reports screen: summaries across every product and every purchased raw material, each on a tab of its own. A load
/// that fails says why in the screen's error notice (ViewModelBase.LoadSafelyAsync).
/// </summary>
public sealed class ReportsViewModel : ViewModelBase, IDisposable
{
    /// <summary>The Product Cost Summary's name, which its export file takes and the View picks its grid by.</summary>
    public const string ProductCostSummaryTitle = "Product Cost Summary";

    /// <summary>The Raw Materials Price Summary's name, which its export file takes and the View picks its grid by.</summary>
    public const string RawMaterialPriceSummaryTitle = "Raw Materials Price Summary";

    /// <summary>The Cost Buffers tab's name, which its export file takes and the View picks its grid by.</summary>
    public const string CostBuffersTitle = "Cost Buffers";

    public const string CostDriversTitle = "Cost Drivers";

    public const string MarginErosionTitle = "Margin Erosion";

    public const string ProductionResultsTitle = "Production Results";

    /// <summary>
    /// The saved plans' results, what was made against what was planned, by product category in pieces, beside the Wastage %
    /// each category is priced with (2026-10-29, ProductionResultsAnalysis).
    /// </summary>
    public ObservableCollection<ProductionResultRow> ProductionResults { get; } = [];

    public ReactiveCommand<RxVoid, RxVoid> ExportProductionResultsCommand { get; }

    public GridLayoutStore ProductionResultsLayout { get; }

    private readonly ISavedPlanRepository? _savedPlanRepository;

    /// <summary>The purchased materials that make up most of what the products in Production cost, the most first (2026-10-29).</summary>
    public ObservableCollection<CostDriverRow> CostDrivers { get; } = [];

    private IReadOnlyList<MarginErosionRow> _allMarginErosion = [];

    /// <summary>Each product's markup as it stood on <see cref="MarginErosionSince"/> against now, the furthest dropped first (2026-10-29).</summary>
    public ObservableCollection<MarginErosionRow> MarginErosion { get; } = [];

    private DateTime _marginErosionSince = DateTime.Today.AddDays(-30);
    /// <summary>The day Margin Erosion compares with: each product's latest version saved by its end. 30 days ago to start with.</summary>
    public DateTime MarginErosionSince
    {
        get => _marginErosionSince;
        set
        {
            if (value.Date == _marginErosionSince)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _marginErosionSince, value.Date);
            _marginErosionWanted.OnNext(RxVoid.Default);
        }
    }

    private bool _onlyDropped = true;
    /// <summary>Margin Erosion shows only the products whose markup dropped; on to start with.</summary>
    public bool OnlyDropped
    {
        get => _onlyDropped;
        set
        {
            this.RaiseAndSetIfChanged(ref _onlyDropped, value);
            ShowMarginErosion();
        }
    }

    private readonly Subject<RxVoid> _marginErosionWanted = new();

    public ReactiveCommand<RxVoid, RxVoid> ExportCostDriversCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ExportMarginErosionCommand { get; }

    public GridLayoutStore CostDriversLayout { get; }

    public GridLayoutStore MarginErosionLayout { get; }

    private readonly IProductVersionRepository? _versionRepository;
    private readonly IIngredientBomLineRepository? _bomLineRepository;

    private IReadOnlyList<CostBufferRow> _allCostBuffers = [];

    /// <summary>Every purchased material with a price and a buffered cost, or only those whose buffer is below target.</summary>
    public ObservableCollection<CostBufferRow> CostBuffers { get; } = [];

    /// <summary>The Cost Buffers rows picked (its SelectedItems): what Re-apply Target Buffer puts back, when any are.</summary>
    public ObservableCollection<object> SelectedCostBuffers { get; } = [];

    private bool _onlyBelowTarget = true;
    /// <summary>Cost Buffers shows only the materials whose buffer is below target; on to start with.</summary>
    public bool OnlyBelowTarget
    {
        get => _onlyBelowTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _onlyBelowTarget, value);
            ShowCostBuffers();
        }
    }

    /// <summary>
    /// Puts the target buffer back on top of the dearest brand for the materials picked, or for every one shown that's below
    /// target, once it's confirmed (IRawMaterialRepository.ReapplyTargetBuffersAsync, with the cascade a Save runs).
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ReapplyTargetBuffersCommand { get; }

    /// <summary>The Cost Buffers table to an Excel workbook, as its grid shows it.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportCostBuffersCommand { get; }

    /// <summary>The Cost Buffers table's sort and filters, kept between runs.</summary>
    public GridLayoutStore CostBuffersLayout { get; }

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

    /// <summary>The Product Cost Summary to an Excel workbook, as its grid shows it: sorted, filtered and with its shown columns.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportProductCostsCommand { get; }

    /// <summary>The Raw Materials Price Summary to an Excel workbook, as its grid shows it.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportRawMaterialPricesCommand { get; }

    /// <summary>The Product Cost Summary's filters, sort and columns, kept between runs (Controls/GridLayoutKeeping).</summary>
    public GridLayoutStore ProductCostsLayout { get; }

    /// <summary>The Raw Materials Price Summary's sort and filters, kept between runs.</summary>
    public GridLayoutStore RawMaterialPricesLayout { get; }

    public ReportsViewModel(
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialRepository rawMaterialRepository,
        IProductRepository productRepository,
        SavedProductReader savedProducts,
        IViewStateStore viewState,
        ILogger logger,
        IProductVersionRepository? versionRepository = null,
        IIngredientBomLineRepository? bomLineRepository = null,
        ISavedPlanRepository? savedPlanRepository = null)
        : base(logger)
    {
        _savedPlanRepository = savedPlanRepository;
        ProductionResultsLayout = new GridLayoutStore(viewState, "reports-production-results");
        ExportProductionResultsCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ProductionResultsTitle));
        _versionRepository = versionRepository;
        _bomLineRepository = bomLineRepository;
        CostDriversLayout = new GridLayoutStore(viewState, "reports-cost-drivers");
        MarginErosionLayout = new GridLayoutStore(viewState, "reports-margin-erosion");
        ExportCostDriversCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(CostDriversTitle));
        ExportMarginErosionCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(MarginErosionTitle));

        // A new day to compare with reads the versions again; a newer one cancels a read still running.
        LoadLatest(_marginErosionWanted, (_, cancellationToken) => LoadMarginErosionAsync(cancellationToken));
        _priceHistoryRepository = priceHistoryRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _productRepository = productRepository;
        _savedProducts = savedProducts;
        ProductCostsLayout = new GridLayoutStore(viewState, "reports-product-costs");
        RawMaterialPricesLayout = new GridLayoutStore(viewState, "reports-raw-material-prices");
        CostBuffersLayout = new GridLayoutStore(viewState, "reports-cost-buffers");
        ExportCostBuffersCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(CostBuffersTitle));
        ReapplyTargetBuffersCommand = ReactiveCommand.CreateFromTask(ReapplyTargetBuffersAsync);

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);
        OpenRawMaterialCommand = ReactiveCommand.Create<int>(_rawMaterialOpenRequests.OnNext);
        ExportProductCostsCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ProductCostSummaryTitle));
        ExportRawMaterialPricesCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(RawMaterialPriceSummaryTitle));

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // Off the UI thread, as Products and Raw Materials read their details, and both summaries at once.
            var productCostsRead = Task.Run(ReadProductCostsAsync);
            var costDriversRead = Task.Run(ReadCostDriversAsync);
            var rawMaterialPricesRead = Task.Run(ReadRawMaterialPricesAsync);
            var (productCosts, saved) = await productCostsRead;
            var productionResults = await Task.Run(() => ReadProductionResultsAsync(saved));
            var (rawMaterialPrices, costBuffers) = await rawMaterialPricesRead;

            ProductCosts.Clear();
            ProductCosts.AddRange(productCosts);
            ProductionResults.Clear();
            ProductionResults.AddRange(productionResults);
            RawMaterialPrices.Clear();
            RawMaterialPrices.AddRange(rawMaterialPrices);
            _allCostBuffers = costBuffers;
            ShowCostBuffers();
            CostDrivers.Clear();
            CostDrivers.AddRange(await costDriversRead);
            _marginErosionWanted.OnNext(RxVoid.Default);
        }));
    }

    /// <summary>
    /// Every product as it's saved, priced as the Products screen prices it once opened (SavedProductReader, which the
    /// screen and Print read through too). All of them are read together, in the same dozen or so queries however many
    /// products there are (2026-10-03: they were read a product at a time, nine or so queries each, several products at
    /// once). The rows stay in the list's order, by name.
    /// </summary>
    private async Task<(List<ProductCostRow> Rows, IReadOnlyList<SavedProduct> Saved)> ReadProductCostsAsync()
    {
        var products = await _productRepository.GetAllAsync(CancellationToken.None);
        var saved = await _savedProducts.ReadAllAsync(products, CostBasis.Buffered, CancellationToken.None);
        return ([.. saved.Select(s => new ProductCostRow(s.Product, s.Costs))], saved);
    }

    /// <summary>Production Results: the saved plans, against the products as <paramref name="saved"/> has them and the wastage they're priced with.</summary>
    private async Task<IReadOnlyList<ProductionResultRow>> ReadProductionResultsAsync(IReadOnlyList<SavedProduct> saved)
    {
        if (_savedPlanRepository is null)
        {
            return [];
        }

        var plans = await _savedPlanRepository.GetAllAsync(CancellationToken.None);
        return ProductionResultsAnalysis.Analyse(
            plans, saved.ToDictionary(s => s.Product.Id, s => s.Product), saved.ToDictionary(s => s.Product.Id, s => s.Costs.WastagePercent));
    }

    /// <summary>
    /// The purchased materials, ingredients and packaging alike, by name: a made or linked ingredient has no price history;
    /// and those with a price and a buffered cost, for Cost Buffers.
    /// </summary>
    private async Task<(List<RawMaterialPriceSummaryRow> Prices, List<CostBufferRow> Buffers)> ReadRawMaterialPricesAsync()
    {
        var materials = await _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var history = (await _priceHistoryRepository.GetAllAsync(CancellationToken.None)).ToLookup(h => h.RawMaterialId);
        List<RawMaterial> purchased = [.. materials.Where(m => m.Source == RawMaterialSource.Purchased).OrderBy(m => m.Name)];

        return (
            [.. purchased.Select(m => new RawMaterialPriceSummaryRow(m, [.. history[m.Id]]))],
            [.. purchased.Where(m => RawMaterialRules.CurrentBufferPercentOf(m) is not null).Select(m => new CostBufferRow(m))]);
    }

    /// <summary>Cost Drivers, off the UI thread: every product and material as saved, priced as Reports prices them (CostDriverAnalysis).</summary>
    private async Task<IReadOnlyList<CostDriverRow>> ReadCostDriversAsync()
    {
        if (_bomLineRepository is null)
        {
            return [];
        }

        var products = await _productRepository.GetAllAsync(CancellationToken.None);
        var details = await _savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
        var materials = (await _rawMaterialRepository.GetAllAsync(CancellationToken.None)).ToDictionary(m => m.Id);
        var bomLines = await _bomLineRepository.GetAllAsync(CancellationToken.None);
        return CostDriverAnalysis.Analyse(products, details, materials, [.. bomLines]);
    }

    /// <summary>Margin Erosion: every product as saved today against its latest version by the end of <see cref="MarginErosionSince"/>.</summary>
    private async Task LoadMarginErosionAsync(CancellationToken cancellationToken)
    {
        if (_versionRepository is null)
        {
            return;
        }

        var asOf = MarginErosionSince.AddDays(1).AddTicks(-1);
        var rows = await Task.Run(
            async () =>
            {
                var products = await _productRepository.GetAllAsync(cancellationToken);
                var saved = await _savedProducts.ReadAllAsync(products, CostBasis.Buffered, cancellationToken);
                var versions = (await _versionRepository.GetLatestAsOfAsync(asOf, cancellationToken)).ToDictionary(v => v.ProductId);
                return MarginErosionAnalysis.Analyse(saved, versions);
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _allMarginErosion = rows;
        ShowMarginErosion();
    }

    private void ShowMarginErosion()
    {
        MarginErosion.Clear();
        MarginErosion.AddRange(OnlyDropped ? _allMarginErosion.Where(r => r.HasDropped) : _allMarginErosion);
    }

    private void ShowCostBuffers()
    {
        SelectedCostBuffers.Clear();
        CostBuffers.Clear();
        CostBuffers.AddRange(OnlyBelowTarget ? _allCostBuffers.Where(r => r.IsBelowTarget) : _allCostBuffers);
    }

    private async Task ReapplyTargetBuffersAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        List<CostBufferRow> picked = [.. SelectedCostBuffers.OfType<CostBufferRow>()];
        List<CostBufferRow> rows = [.. (picked.Count > 0 ? picked : CostBuffers.ToList()).Where(r => r.IsBelowTarget)];
        if (rows.Count == 0)
        {
            ErrorMessage = "No material shown has a buffer below its target.";
            return;
        }

        var what = rows.Count == 1 ? rows[0].Name : $"{rows.Count} materials";
        if (!await ConfirmDelete.Handle(
                $"Put the target buffer back on top of the brand each is costed at for {what}? Their buffered costs go up, and so do the costs of every recipe and product using them."))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var changed = await Task.Run(() => _rawMaterialRepository.ReapplyTargetBuffersAsync([.. rows.Select(r => r.RawMaterialId)], CancellationToken.None));
            await LoadCommand.Execute();
            SuccessMessage = $"The target buffer is back on {(changed == 1 ? "1 material" : $"{changed} materials")}.";
        });
    }

    public void Dispose()
    {
        _productOpenRequests.Dispose();
        _rawMaterialOpenRequests.Dispose();
        _marginErosionWanted.Dispose();
    }
}
