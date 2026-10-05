using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Production Plan screen (2026-10-03): the products to make and how many, and from them the mixes to make in whole
/// batches and the raw materials to buy (ProductionPlanBuilder). One plan, kept for this Windows user on this computer
/// between runs (IViewStateStore), worked out again from the products as saved whenever the screen is shown.
/// </summary>
public sealed class ProductionPlanViewModel : ViewModelBase, IDisposable
{
    /// <summary>Where the plan is kept: one "product id:units" for each line, in the table's order.</summary>
    public const string PlanKey = "production-plan";

    public const string ExportTitle = "Production Plan";

    private readonly IProductRepository _productRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly SavedProductReader _savedProducts;
    private readonly IViewStateStore _viewState;

    private readonly Subject<RxVoid> _recalculations = new();
    private readonly HashSet<PlanLineRow> _followed = [];

    /// <summary>Everything the plan is worked out from, as last loaded; null before the first load.</summary>
    private Data? _data;

    /// <summary>The plan as it was kept, put into the table by the first load, once the products are known.</summary>
    private IReadOnlyList<string>? _kept;

    /// <summary>The products to make, as typed in the table.</summary>
    public ObservableCollection<PlanLineRow> Lines { get; } = [];

    private IReadOnlyList<Product> _products = [];
    /// <summary>Every product, by name: what the plan's picker offers.</summary>
    public IReadOnlyList<Product> Products
    {
        get => _products;
        private set => this.RaiseAndSetIfChanged(ref _products, value);
    }

    public ObservableCollection<MixRow> Mixes { get; } = [];

    public ObservableCollection<PurchaseRequisitionRow> Requisition { get; } = [];

    private decimal _totalCost;
    /// <summary>What the purchase requisition comes to, at the materials' buffered costs.</summary>
    public decimal TotalCost
    {
        get => _totalCost;
        private set => this.RaiseAndSetIfChanged(ref _totalCost, value);
    }

    private IReadOnlyList<string> _notes = [];
    /// <summary>What the plan couldn't work out, in sentences: a section with no piece size, a made ingredient with no BOM.</summary>
    public IReadOnlyList<string> Notes
    {
        get => _notes;
        private set => this.RaiseAndSetIfChanged(ref _notes, value);
    }

    private PlanLineRow? _selectedLine;
    public PlanLineRow? SelectedLine
    {
        get => _selectedLine;
        set => this.RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    /// <summary>The plan table's add row's item.</summary>
    public Func<PlanLineRow> NewLine => () => new PlanLineRow(Find);

    /// <summary>Input: the plan on paper. The View shows it in a print pane.</summary>
    public Interaction<ProductPrintSet, RxVoid> ShowPrint { get; } = new();

    /// <summary>Runs whenever the tab is shown (MainWindow): the products and prices as they are now, the plan kept.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> PrintCommand { get; }

    /// <summary>The mixing sheet and the purchase requisition as the tables show them, to an Excel workbook.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }

    public ProductionPlanViewModel(
        IProductRepository productRepository,
        IRawMaterialRepository rawMaterialRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        SavedProductReader savedProducts,
        IViewStateStore viewState,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _bomLineRepository = bomLineRepository;
        _brandPriceRepository = brandPriceRepository;
        _savedProducts = savedProducts;
        _viewState = viewState;
        _kept = viewState.ReadList(PlanKey);

        Lines.CollectionChanged += LinesChanged;

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            _data = await Task.Run(ReadAsync);
            Products = [.. _data.Products.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

            if (_kept is { } kept)
            {
                foreach (var line in Parse(kept).ToList())
                {
                    Lines.Add(line);
                }

                _kept = null;
            }
            else
            {
                // Each line pointed at today's copy of its product; one whose product is gone goes.
                foreach (var line in Lines.ToList())
                {
                    if (line.ProductId is { } id && !_data.Products.ContainsKey(id))
                    {
                        Lines.Remove(line);
                    }
                    else
                    {
                        line.ProductId = line.ProductId;
                    }
                }
            }

            Recalculate();
        }));

        RemoveLineCommand = ReactiveCommand.Create(
            () => { Lines.Remove(SelectedLine!); },
            this.WhenAnyValue(x => x.SelectedLine).Select(line => line is not null));
        ClearCommand = ReactiveCommand.Create(Lines.Clear);
        PrintCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () => await ShowPrint.Handle(PrintSet(DateTime.Now))));
        ExportCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ExportTitle));

        // A newer edit cancels a plan still being worked out, and one that fails says why and keeps the plan it had.
        LoadLatest(_recalculations, (_, cancellationToken) => RecalculateAsync(cancellationToken));
    }

    /// <summary>The plan on paper, as the tables show it now.</summary>
    public ProductPrintSet PrintSet(DateTime printedAt) => ProductPrintSet.Of(
        new PrintedPlan([.. Lines.Where(l => l.Product is not null && l.Units is > 0m)], [.. Mixes], [.. Requisition], TotalCost, Notes),
        printedAt);

    private void Recalculate() => _recalculations.OnNext(RxVoid.Default);

    private async Task RecalculateAsync(CancellationToken cancellationToken)
    {
        if (_data is not { } data)
        {
            return;
        }

        // A product listed twice is made once, its units added up.
        List<(int ProductId, decimal Units)> orders =
        [
            .. Lines.Where(l => l.ProductId is not null && l.Units is > 0m)
                .GroupBy(l => l.ProductId!.Value)
                .Select(g => (g.Key, g.Sum(l => l.Units!.Value))),
        ];

        var result = await Task.Run(
            () => ProductionPlanBuilder.Build(orders, data.Saved, data.Materials, data.BomLines, data.Prices),
            cancellationToken);

        // A newer edit may have come in meanwhile: then this plan is stale, and mustn't replace the newer one or clear its error.
        cancellationToken.ThrowIfCancellationRequested();
        Mixes.Clear();
        foreach (var mix in result.Mixes)
        {
            Mixes.Add(mix);
        }

        Requisition.Clear();
        foreach (var row in result.Requisition)
        {
            Requisition.Add(row);
        }

        TotalCost = result.TotalCost;
        Notes = result.Notes;
    }

    /// <summary>Every product, priced as saved, and every material, BOM line and brand price, read at once.</summary>
    private async Task<Data> ReadAsync()
    {
        var productsRead = _productRepository.GetAllAsync(CancellationToken.None);
        var materialsRead = _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var bomLinesRead = _bomLineRepository.GetAllAsync(CancellationToken.None);
        var pricesRead = _brandPriceRepository.GetAllAsync(CancellationToken.None);
        await Task.WhenAll(productsRead, materialsRead, bomLinesRead, pricesRead);

        var products = await productsRead;
        var saved = await _savedProducts.ReadAllAsync(products, CostBasis.Buffered, CancellationToken.None);
        return new Data(
            products.ToDictionary(p => p.Id),
            saved.ToDictionary(s => s.Product.Id),
            (await materialsRead).ToDictionary(m => m.Id),
            [.. await bomLinesRead],
            (await pricesRead).ToLookup(p => p.RawMaterialId));
    }

    private Product? Find(int id) => _data?.Products.GetValueOrDefault(id);

    /// <summary>The kept lines whose products are still there: "12:40" is 40 of product 12.</summary>
    private IEnumerable<PlanLineRow> Parse(IReadOnlyList<string> kept)
    {
        foreach (var entry in kept)
        {
            var parts = entry.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var units)
                && Find(id) is not null)
            {
                yield return new PlanLineRow(Find) { ProductId = id, Units = units };
            }
        }
    }

    /// <summary>Keeps the plan for next time: every line with a product, as "id:units".</summary>
    private void Keep() => _viewState.WriteList(
        PlanKey,
        [.. Lines.Where(l => l.ProductId is not null).Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.ProductId}:{l.Units ?? 0m}"))]);

    /// <summary>
    /// Follows the edits of the lines now in the table, and of no others: a Clear says nothing of the lines it took away,
    /// so the lines followed are kept here rather than read off the change.
    /// </summary>
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var gone in _followed.Except(Lines).ToList())
        {
            gone.PropertyChanged -= LineEdited;
            _followed.Remove(gone);
        }

        foreach (var added in Lines.Where(_followed.Add))
        {
            added.PropertyChanged += LineEdited;
        }

        Edited();
    }

    private void LineEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlanLineRow.ProductId) or nameof(PlanLineRow.Units))
        {
            Edited();
        }
    }

    /// <summary>An edit to the plan: kept, once the products are known (before then the lines are only the kept ones going back in), and worked out again.</summary>
    private void Edited()
    {
        if (_data is not null && _kept is null)
        {
            Keep();
        }

        Recalculate();
    }

    public void Dispose()
    {
        _recalculations.Dispose();
    }

    private sealed record Data(
        IReadOnlyDictionary<int, Product> Products,
        IReadOnlyDictionary<int, SavedProduct> Saved,
        IReadOnlyDictionary<int, RawMaterial> Materials,
        IReadOnlyList<IngredientBomLine> BomLines,
        ILookup<int, RawMaterialBrandPrice> Prices);
}
