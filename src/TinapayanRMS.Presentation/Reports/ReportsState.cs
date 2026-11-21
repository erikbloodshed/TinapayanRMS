using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.RawMaterials;
using TinapayanRMS.UseCases.Reports;

namespace TinapayanRMS.Presentation.Reports;

/// <summary>
/// A row of the Product Cost Summary: a product's cost and price for one sellable unit, computed live from today's data,
/// never historized.
/// </summary>
/// <param name="sectionsWithoutPieceSize">Its sections with no weight per piece or yield (SavedProduct.SectionsWithoutPieceSize), by name.</param>
public sealed class ProductCostRow(Product product, ProductCostSummary costs, IReadOnlyList<string>? sectionsWithoutPieceSize = null)
{
    public int ProductId => product.Id;

    public string ProductName => product.Name;

    public string Stage => ProductRules.StageTitle(product.Stage);

    public string Category => product.Category?.Name ?? ProductRules.UncategorizedTitle;

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

    /// <summary>The selling price the costing works out, the Costing tab's Suggested Selling Price: a suggestion beside <see cref="CurrentPrice"/>.</summary>
    public decimal SuggestedPrice => costs.SellingPrice;

    /// <summary>
    /// <see cref="SuggestedPrice"/> rounded up to a price the shop would charge (Settings' price rounding); the
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
    /// How many percentage points the current price's markup is short of <see cref="TargetMarkupPercent"/>; null
    /// when it isn't short, or there's no current price. Sorting by it puts the furthest short first.
    /// </summary>
    public decimal? ShortOfTargetMarkup => costs.IsBelowTargetMarkup ? costs.MarginPercent - costs.CurrentMarkupPercent : null;

    /// <summary>
    /// The recipe sections with no weight per piece or yield, which cost it nothing per piece, by name ("Topping", or
    /// "Dough, Topping"); empty when every section has one. Its costs and prices leave those sections out.
    /// </summary>
    public string NoPieceSize { get; } = string.Join(", ", sectionsWithoutPieceSize ?? []);
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
/// A row of Reports' Cost Buffers: a purchased material with a price, its fixed buffered cost against its dearest
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
/// that fails says why in the screen's error notice (ScreenState.LoadSafelyAsync).
/// </summary>
public sealed class ReportsState : ScreenState, IDisposable
{
    /// <summary>The Product Cost Summary's name, which its export file takes and the screen picks its grid by.</summary>
    public const string ProductCostSummaryTitle = "Product Cost Summary";

    /// <summary>The Raw Materials Price Summary's name, which its export file takes and the screen picks its grid by.</summary>
    public const string RawMaterialPriceSummaryTitle = "Raw Materials Price Summary";

    /// <summary>The Cost Buffers tab's name, which its export file takes and the screen picks its grid by.</summary>
    public const string CostBuffersTitle = "Cost Buffers";

    public const string CostDriversTitle = "Cost Drivers";

    public const string MarginErosionTitle = "Margin Erosion";

    /// <summary>The purchased materials that make up most of what the products in Production cost, the most first.</summary>
    public TableRows<CostDriverRow> CostDrivers { get; } = [];

    private IReadOnlyList<MarginErosionRow> _allMarginErosion = [];

    /// <summary>Each product's markup as it stood on <see cref="MarginErosionSince"/> against now, the furthest dropped first.</summary>
    public TableRows<MarginErosionRow> MarginErosion { get; } = [];

    private DateTime _marginErosionSince = DateTime.Today.AddDays(-30);
    /// <summary>The day Margin Erosion compares with: each product's latest version saved by its end. 30 days ago to start with.</summary>
    public DateTime MarginErosionSince
    {
        get => _marginErosionSince;
        set
        {
            if (RaiseAndSetIfChanged(ref _marginErosionSince, value.Date))
            {
                _marginErosionWanted.OnNext(Unit.Default);
            }
        }
    }

    private bool _onlyDropped = true;
    /// <summary>Margin Erosion shows only the products whose markup dropped; on to start with.</summary>
    public bool OnlyDropped
    {
        get => _onlyDropped;
        set
        {
            RaiseAndSetIfChanged(ref _onlyDropped, value);
            ShowMarginErosion();
        }
    }

    private readonly Subject<Unit> _marginErosionWanted = new();

    public ScreenCommand ExportCostDriversCommand { get; }

    public ScreenCommand ExportMarginErosionCommand { get; }

    private IReadOnlyList<CostBufferRow> _allCostBuffers = [];

    /// <summary>Every purchased material with a price and a buffered cost, or only those whose buffer is below target.</summary>
    public TableRows<CostBufferRow> CostBuffers { get; } = [];

    /// <summary>The Cost Buffers rows picked (its SelectedItems): what Re-apply Target Buffer puts back, when any are.</summary>
    public ObservableCollection<object> SelectedCostBuffers { get; } = [];

    private bool _onlyBelowTarget = true;
    /// <summary>Cost Buffers shows only the materials whose buffer is below target; on to start with.</summary>
    public bool OnlyBelowTarget
    {
        get => _onlyBelowTarget;
        set
        {
            RaiseAndSetIfChanged(ref _onlyBelowTarget, value);
            ShowCostBuffers();
        }
    }

    /// <summary>
    /// Puts the target buffer back on top of the dearest brand for the materials picked, or for every one shown that's below
    /// target, once it's confirmed (IRawMaterialEditor.ReapplyTargetBuffersAsync, with the cascade a Save runs).
    /// </summary>
    public ScreenCommand ReapplyTargetBuffersCommand { get; }

    /// <summary>The Cost Buffers table to an Excel workbook, as its grid shows it.</summary>
    public ScreenCommand ExportCostBuffersCommand { get; }

    private readonly IReportQueries _queries;
    private readonly IRawMaterialEditor _rawMaterials;

    public TableRows<ProductCostRow> ProductCosts { get; } = [];

    public TableRows<RawMaterialPriceSummaryRow> RawMaterialPrices { get; } = [];

    /// <summary>A Product Cost Summary row's double-click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ScreenCommand<int> OpenProductCommand { get; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products the Product Cost Summary asks to open, by id. The main window, which owns the tabs, subscribes and
    /// opens each on the Products screen, as it does for the Dependents tiles on Raw Materials and Sub-Assemblies.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>A Raw Materials Price Summary row's double-click: asks for the material with this id to be opened, through <see cref="RawMaterialOpenRequests"/>.</summary>
    public ScreenCommand<int> OpenRawMaterialCommand { get; }

    private readonly Subject<int> _rawMaterialOpenRequests = new();

    /// <summary>The raw materials the Raw Materials Price Summary asks to open, by id, which the main window opens on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    /// <summary>Runs whenever the screen is opened (ScreenBase), so both summaries are from current prices.</summary>
    public ScreenCommand LoadCommand { get; }

    /// <summary>The Product Cost Summary to an Excel workbook, as its grid shows it: sorted, filtered and with its shown columns.</summary>
    public ScreenCommand ExportProductCostsCommand { get; }

    /// <summary>The Raw Materials Price Summary to an Excel workbook, as its grid shows it.</summary>
    public ScreenCommand ExportRawMaterialPricesCommand { get; }

    public ReportsState(IReportQueries queries, IRawMaterialEditor rawMaterials, ILogger logger)
        : base(logger)
    {
        _queries = queries;
        _rawMaterials = rawMaterials;
        ExportCostDriversCommand = new ScreenCommand(() => ExportToExcelAsync(CostDriversTitle));
        ExportMarginErosionCommand = new ScreenCommand(() => ExportToExcelAsync(MarginErosionTitle));

        // A new day to compare with reads the versions again; a newer one cancels a read still running.
        LoadLatest(_marginErosionWanted, (_, cancellationToken) => LoadMarginErosionAsync(cancellationToken));
        ExportCostBuffersCommand = new ScreenCommand(() => ExportToExcelAsync(CostBuffersTitle));
        ReapplyTargetBuffersCommand = new ScreenCommand(ReapplyTargetBuffersAsync);

        OpenProductCommand = ScreenCommand.Of<int>(_productOpenRequests.OnNext);
        OpenRawMaterialCommand = ScreenCommand.Of<int>(_rawMaterialOpenRequests.OnNext);
        ExportProductCostsCommand = new ScreenCommand(() => ExportToExcelAsync(ProductCostSummaryTitle));
        ExportRawMaterialPricesCommand = new ScreenCommand(() => ExportToExcelAsync(RawMaterialPriceSummaryTitle));

        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            // Read, priced and worked into the tables off the UI thread, as Products and Raw Materials read their details.
            var read = await Task.Run(async () => Tabulate(await _queries.ReadAsync(CancellationToken.None)));

            // Each table in one change (TableRows), so its grid draws it once.
            ProductCosts.ReplaceWith(read.ProductCosts);
            RawMaterialPrices.ReplaceWith(read.RawMaterialPrices);
            _allCostBuffers = read.CostBuffers;
            ShowCostBuffers();
            CostDrivers.ReplaceWith(read.CostDrivers);
            _priced = read.Priced;
            _marginErosionWanted.OnNext(Unit.Default);
        }));
    }

    /// <summary>
    /// What a visit read (IReportQueries: every product as it's saved, and every material with its price history and the
    /// BOM lines) worked into every table but Margin Erosion, each product priced as the Products screen prices it once
    /// opened (SavedProductReader, which the screen and Print read through too). The products are read once for the Product
    /// Cost Summary, Cost Drivers and Margin Erosion together: each read them for itself, three times a visit, before
    /// 2026-11-13.
    /// </summary>
    private static Read Tabulate(ReportsRead read)
    {
        var (products, details, materials) = (read.Products, read.Details, read.Materials);
        var history = read.PriceHistory.ToLookup(h => h.RawMaterialId);

        // The products in the list's order, by name; the purchased materials, ingredients and packaging alike, by name: a
        // made or linked ingredient has no price history. Cost Buffers takes those with a price and a buffered cost.
        IReadOnlyList<SavedProduct> priced = [.. products.Select(p => SavedProductReader.Build(p, details[p.Id], CostBasis.Buffered))];
        List<RawMaterial> purchased = [.. materials.Where(m => m.Source == RawMaterialSource.Purchased).OrderBy(m => m.Name)];
        return new Read(
            priced,
            [.. priced.Select(s => new ProductCostRow(s.Product, s.Costs, s.SectionsWithoutPieceSize))],
            [.. purchased.Select(m => new RawMaterialPriceSummaryRow(m, [.. history[m.Id]]))],
            [.. purchased.Where(m => RawMaterialRules.CurrentBufferPercentOf(m) is not null).Select(m => new CostBufferRow(m))],
            CostDriverAnalysis.Analyse(products, details, materials.ToDictionary(m => m.Id), [.. read.BomLines]));
    }

    /// <summary>What a visit reads, priced and worked into its tables.</summary>
    private sealed record Read(
        IReadOnlyList<SavedProduct> Priced,
        List<ProductCostRow> ProductCosts,
        List<RawMaterialPriceSummaryRow> RawMaterialPrices,
        List<CostBufferRow> CostBuffers,
        IReadOnlyList<CostDriverRow> CostDrivers);

    /// <summary>Every product as the last visit read and priced it: what Margin Erosion compares, whatever day it compares with.</summary>
    private IReadOnlyList<SavedProduct> _priced = [];

    /// <summary>
    /// Margin Erosion: every product as saved today, as the visit read it, against its latest version by the end of
    /// <see cref="MarginErosionSince"/>. A new day to compare with reads only the versions.
    /// </summary>
    private async Task LoadMarginErosionAsync(CancellationToken cancellationToken)
    {
        var priced = _priced;
        var asOf = MarginErosionSince.AddDays(1).AddTicks(-1);
        var rows = await Task.Run(
            async () =>
            {
                var versions = (await _queries.ReadVersionsAsOfAsync(asOf, cancellationToken)).ToDictionary(v => v.ProductId);
                return MarginErosionAnalysis.Analyse(priced, versions);
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _allMarginErosion = rows;
        ShowMarginErosion();
    }

    private void ShowMarginErosion() =>
        MarginErosion.ReplaceWith(OnlyDropped ? _allMarginErosion.Where(r => r.HasDropped) : _allMarginErosion);

    private void ShowCostBuffers()
    {
        // The rows picked go with the rows they were picked from: the grid unpicks them as it takes new rows, and keeps
        // them when the same rows come again (TableRows), so the picks here follow it.
        if (CostBuffers.ReplaceWith(OnlyBelowTarget ? _allCostBuffers.Where(r => r.IsBelowTarget) : _allCostBuffers))
        {
            SelectedCostBuffers.Clear();
        }
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
            var changed = await Task.Run(() => _rawMaterials.ReapplyTargetBuffersAsync([.. rows.Select(r => r.RawMaterialId)], CancellationToken.None));
            await LoadCommand.ExecuteAsync();
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
