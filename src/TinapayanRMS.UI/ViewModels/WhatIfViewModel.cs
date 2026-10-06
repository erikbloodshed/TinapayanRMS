using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A product on the What-If screen: its cost, suggested price and markup now, and with the price changes tried.</summary>
public sealed class WhatIfProductRow(Product product, ProductCostSummary now, ProductCostSummary whatIf)
{
    public int ProductId => product.Id;

    public string ProductName => product.Name;

    public string Category => product.Category?.Name ?? ProductFolderNode.UncategorizedTitle;

    /// <summary>For one sellable unit, as everything on the row is.</summary>
    public decimal BaseCost => now.BaseCost;

    public decimal WhatIfBaseCost => whatIf.BaseCost;

    /// <summary>How far the base cost moves, in percent.</summary>
    public decimal ChangePercent => CostingRules.PercentOf(WhatIfBaseCost - BaseCost, BaseCost);

    public decimal SuggestedPrice => now.SellingPrice;

    public decimal WhatIfSuggestedPrice => whatIf.SellingPrice;

    /// <summary>What the current price earns now, as the Products header shows it; null without a current price.</summary>
    public decimal? MarkupPercent => now.CurrentMarkupPercent;

    /// <summary>What the same current price would earn with the changes.</summary>
    public decimal? WhatIfMarkupPercent => whatIf.CurrentMarkupPercent;

    /// <summary>Whether any change reaches it. Exact: both figures come from the same arithmetic, so an untouched product's are equal.</summary>
    public bool IsAffected => WhatIfBaseCost != BaseCost;
}

/// <summary>
/// The What-If Prices screen (2026-10-03): try raw material price changes and see every product's cost, suggested price and
/// markup with them, beside what they are now. A change reaches made and linked ingredients as a real one would
/// (IngredientCostRules.Reprice) and products through the same pricing as Reports (SavedProductReader). Nothing is saved:
/// the changes last while the app is open.
/// </summary>
public sealed class WhatIfViewModel : ViewModelBase, IDisposable
{
    public const string ExportTitle = "What-If Prices";

    private readonly IProductRepository _productRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly SavedProductReader _savedProducts;

    private readonly Subject<RxVoid> _recalculations = new();
    private readonly Subject<int> _productOpenRequests = new();
    private readonly HashSet<WhatIfPriceChange> _followed = [];

    /// <summary>Everything the figures are worked out from, as last loaded; null before the first load.</summary>
    private Data? _data;

    /// <summary>Every product's rows from the last recalculation, before the Only affected filter.</summary>
    private IReadOnlyList<WhatIfProductRow> _allRows = [];

    /// <summary>
    /// The last recalculation's products at the what-if prices, by id, and the changes it tried, each with the materials it
    /// reaches: what the report prints, so it always matches the table, even while a refused change waits to be put right.
    /// </summary>
    private Dictionary<int, SavedProduct> _whatIf = [];
    private IReadOnlyList<(PrintedPriceChange Change, IReadOnlySet<int> Reaches)> _tried = [];

    /// <summary>The price changes being tried, as typed in the table.</summary>
    public ObservableCollection<WhatIfPriceChange> Changes { get; } = [];

    /// <summary>Purchased ingredients and packaging, by name: what a price change can be tried on.</summary>
    private IReadOnlyList<RawMaterial> _materials = [];
    public IReadOnlyList<RawMaterial> Materials
    {
        get => _materials;
        private set => this.RaiseAndSetIfChanged(ref _materials, value);
    }

    /// <summary>The products, all of them or only those a change reaches.</summary>
    public ObservableCollection<WhatIfProductRow> Products { get; } = [];

    /// <summary>The Products table's picked rows (its SelectedItems), Ctrl or Shift held for several: what Print Report's Selected products prints.</summary>
    public ObservableCollection<object> SelectedProducts { get; } = [];

    /// <summary>
    /// The products as the table shows them, filtered and sorted, which the view supplies from its grid: what Print Report's
    /// All products shown prints, in that order. Until it does, <see cref="Products"/> as they are.
    /// </summary>
    public Func<IReadOnlyList<WhatIfProductRow>> ShownProducts { get; set; }

    private bool _onlyAffected;
    public bool OnlyAffected
    {
        get => _onlyAffected;
        set
        {
            this.RaiseAndSetIfChanged(ref _onlyAffected, value);
            ShowRows();
        }
    }

    private WhatIfPriceChange? _selectedChange;
    public WhatIfPriceChange? SelectedChange
    {
        get => _selectedChange;
        set => this.RaiseAndSetIfChanged(ref _selectedChange, value);
    }

    /// <summary>The Price Changes table's add row's item.</summary>
    public Func<WhatIfPriceChange> NewChange => () => new WhatIfPriceChange(Find);

    /// <summary>Runs whenever the tab is shown (MainWindow): today's products and prices, the changes kept.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveChangeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    /// <summary>The products as the table shows them, and the changes, to an Excel workbook.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }

    /// <summary>Each product's recipe and costing at the what-if prices, beside today's, in a print preview (<see cref="ShowPrint"/>).</summary>
    public ReactiveCommand<RxVoid, RxVoid> PrintCommand { get; }

    /// <summary>
    /// Input: the report's sets, the one to show first first: the selected products, when any are, then all the products
    /// shown. The View shows them in a print pane.
    /// </summary>
    public Interaction<IReadOnlyList<PrintChoice>, RxVoid> ShowPrint { get; } = new();

    /// <summary>A product row's double-click: asks for the product to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; }

    /// <summary>The products asked to be opened, by id, which the main window opens on Products.</summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    public WhatIfViewModel(
        IProductRepository productRepository,
        IRawMaterialRepository rawMaterialRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        SavedProductReader savedProducts,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _bomLineRepository = bomLineRepository;
        _brandPriceRepository = brandPriceRepository;
        _savedProducts = savedProducts;
        ShownProducts = () => Products;

        Changes.CollectionChanged += ChangesChanged;

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            _data = await Task.Run(ReadAsync);
            Materials = [.. _data.Materials.Values.Where(m => m.Source == RawMaterialSource.Purchased).OrderBy(m => m.Name)];

            // Each change is pointed at today's copy of its material and price; one whose material is gone goes.
            foreach (var change in Changes.ToList())
            {
                if (change.MaterialId is { } id && _data.Materials.ContainsKey(id))
                {
                    change.MaterialId = id;
                }
                else if (change.MaterialId is not null)
                {
                    Changes.Remove(change);
                }
            }

            Recalculate();
        }));

        RemoveChangeCommand = ReactiveCommand.Create(
            () => { Changes.Remove(SelectedChange!); },
            this.WhenAnyValue(x => x.SelectedChange).Select(change => change is not null));
        ClearCommand = ReactiveCommand.Create(Changes.Clear);
        ExportCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ExportTitle));
        PrintCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () => await ShowPrint.Handle(PrintChoices(DateTime.Now))));
        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);

        // A newer change cancels a recalculation still running, and one that's refused (a price of 0) says why and keeps
        // the figures it had, until the change is put right.
        LoadLatest(_recalculations, (_, cancellationToken) => RecalculateAsync(cancellationToken));
    }

    /// <summary>Asks for the figures to be worked out again, after the last change is in.</summary>
    private void Recalculate() => _recalculations.OnNext(RxVoid.Default);

    private async Task RecalculateAsync(CancellationToken cancellationToken)
    {
        if (_data is not { } data)
        {
            return;
        }

        // Read on the UI thread, since the rows are the table's; the pricing runs off it.
        List<(WhatIfPriceChange Change, (int MaterialId, decimal Cost, decimal BufferedCost) Costs)> tried =
        [
            .. Changes.Select(change => (Change: change, Costs: change.NewCosts())).Where(t => t.Costs is not null).Select(t => (t.Change, t.Costs!.Value)),
        ];
        List<(int MaterialId, decimal Cost, decimal BufferedCost)> newCosts = [.. tried.Select(t => t.Costs)];
        if (newCosts.GroupBy(c => c.MaterialId).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new BusinessRuleException($"{data.Materials[repeated.Key].Name} is in the table twice. Remove one of its rows.");
        }

        List<PrintedPriceChange> printed = [.. tried.Select(t => Printed(t.Change))];
        var (rows, whatIfById, reaches) = await Task.Run(
            () =>
            {
                var repriced = IngredientCostRules.Reprice(data.Materials, data.BomLines, newCosts.ToDictionary(c => c.MaterialId, c => (c.Cost, c.BufferedCost)));
                var details = SavedProductReader.PricedAt(data.Details, repriced);
                Dictionary<int, SavedProduct> whatIfById = [];
                foreach (var product in data.Products)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    whatIfById[product.Id] = SavedProductReader.Build(product, details[product.Id], CostBasis.Buffered, repriced);
                }

                IReadOnlyList<WhatIfProductRow> rows = [.. data.Products.Select(p => new WhatIfProductRow(p, data.Now[p.Id].Costs, whatIfById[p.Id].Costs))];
                List<IReadOnlySet<int>> reaches = [.. newCosts.Select(c => IngredientCostRules.ReachedBy(data.Materials.Values, data.BomLines, c.MaterialId))];
                return (rows, whatIfById, reaches);
            },
            cancellationToken);

        // A newer change may have come in while these were worked out: then they're stale, and must neither replace the
        // newer figures nor, by finishing, clear the error the newer one showed.
        cancellationToken.ThrowIfCancellationRequested();
        _allRows = rows;
        _whatIf = whatIfById;
        _tried = [.. printed.Zip(reaches)];
        ShowRows();
    }

    /// <summary>
    /// Shows the rows the Only affected filter leaves. They're new rows each time, so the picked products are picked again
    /// by id: a change typed or the filter ticked doesn't lose them.
    /// </summary>
    private void ShowRows()
    {
        HashSet<int> picked = [.. SelectedProducts.OfType<WhatIfProductRow>().Select(r => r.ProductId)];
        Products.Clear();
        foreach (var row in _allRows.Where(row => !OnlyAffected || row.IsAffected))
        {
            Products.Add(row);
        }

        SelectedProducts.Clear();
        foreach (var row in Products.Where(r => picked.Contains(r.ProductId)))
        {
            SelectedProducts.Add(row);
        }
    }

    /// <summary>
    /// The report's sets as the table shows the products now: the selected ones, when any are, then all of them, each in
    /// the table's order, and each after a first page of every change tried.
    /// </summary>
    public IReadOnlyList<PrintChoice> PrintChoices(DateTime printedAt)
    {
        var shown = ShownProducts();
        HashSet<int> picked = [.. SelectedProducts.OfType<WhatIfProductRow>().Select(r => r.ProductId)];
        List<WhatIfProductRow> selected = [.. shown.Where(r => picked.Contains(r.ProductId))];

        List<PrintChoice> choices = [];
        if (selected.Count > 0)
        {
            choices.Add(new PrintChoice($"Selected products ({selected.Count})", Report(selected, printedAt)));
        }

        choices.Add(new PrintChoice($"All products shown ({shown.Count})", Report(shown, printedAt)));
        return choices;
    }

    private ProductPrintSet Report(IReadOnlyList<WhatIfProductRow> rows, DateTime printedAt)
    {
        List<ProductPrintout> printouts = [];
        if (_data is { } data)
        {
            foreach (var row in rows)
            {
                var materialIds = WhatIfReport.MaterialIdsOf(row.ProductId, data.Details);
                IReadOnlyList<PrintedPriceChange> reaching = [.. _tried.Where(t => t.Reaches.Overlaps(materialIds)).Select(t => t.Change)];
                printouts.Add(WhatIfReport.Printout(data.Now[row.ProductId], _whatIf[row.ProductId], reaching, printedAt));
            }
        }

        return WhatIfReport.Set(printouts, [.. _tried.Select(t => t.Change)], printedAt);
    }

    /// <summary>A price change as the table shows it, for the report. Only a row with its new price worked out is tried.</summary>
    private static PrintedPriceChange Printed(WhatIfPriceChange change) =>
        new(change.Material!.Name, change.UnitName, change.CurrentPrice, change.ChangePercent, change.NewPrice!.Value);

    /// <summary>Every product, read and priced as saved, and every material, BOM line and brand price, read at once.</summary>
    private async Task<Data> ReadAsync()
    {
        var productsRead = _productRepository.GetAllAsync(CancellationToken.None);
        var materialsRead = _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var bomLinesRead = _bomLineRepository.GetAllAsync(CancellationToken.None);
        var pricesRead = _brandPriceRepository.GetAllAsync(CancellationToken.None);
        await Task.WhenAll(productsRead, materialsRead, bomLinesRead, pricesRead);

        var products = await productsRead;
        var details = await _savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
        return new Data(
            products,
            details,
            products.ToDictionary(p => p.Id, p => SavedProductReader.Build(p, details[p.Id], CostBasis.Buffered)),
            (await materialsRead).ToDictionary(m => m.Id),
            [.. await bomLinesRead],
            (await pricesRead).ToLookup(p => p.RawMaterialId));
    }

    /// <summary>A purchased material by id, with what it's bought in; null for one that's gone or isn't purchased.</summary>
    private (RawMaterial Material, PurchaseUnit Unit)? Find(int id) =>
        _data?.Materials.GetValueOrDefault(id) is { Source: RawMaterialSource.Purchased } material
            ? (material, PurchaseUnit.Of(material, [.. _data.Prices[id]]))
            : null;

    /// <summary>
    /// Follows the edits of the rows now in the table, and of no others: a Clear says nothing of the rows it took away,
    /// so the rows followed are kept here rather than read off the change.
    /// </summary>
    private void ChangesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var gone in _followed.Except(Changes).ToList())
        {
            gone.PropertyChanged -= ChangeEdited;
            _followed.Remove(gone);
        }

        foreach (var added in Changes.Where(_followed.Add))
        {
            added.PropertyChanged += ChangeEdited;
        }

        Recalculate();
    }

    private void ChangeEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WhatIfPriceChange.MaterialId) or nameof(WhatIfPriceChange.NewPrice) or nameof(WhatIfPriceChange.ChangePercent))
        {
            Recalculate();
        }
    }

    public void Dispose()
    {
        _recalculations.Dispose();
        _productOpenRequests.Dispose();
    }

    private sealed record Data(
        IReadOnlyList<Product> Products,
        IReadOnlyDictionary<int, ProductDetail> Details,
        IReadOnlyDictionary<int, SavedProduct> Now,
        IReadOnlyDictionary<int, RawMaterial> Materials,
        IReadOnlyList<IngredientBomLine> BomLines,
        ILookup<int, RawMaterialBrandPrice> Prices);
}
