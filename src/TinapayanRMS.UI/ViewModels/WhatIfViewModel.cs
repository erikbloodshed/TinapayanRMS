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
        List<(int MaterialId, decimal Cost, decimal BufferedCost)> newCosts = [.. Changes.Select(change => change.NewCosts()).OfType<(int, decimal, decimal)>()];
        if (newCosts.GroupBy(c => c.MaterialId).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new BusinessRuleException($"{data.Materials[repeated.Key].Name} is in the table twice. Remove one of its rows.");
        }

        var rows = await Task.Run(
            () =>
            {
                var repriced = IngredientCostRules.Reprice(data.Materials, data.BomLines, newCosts.ToDictionary(c => c.MaterialId, c => (c.Cost, c.BufferedCost)));
                var details = SavedProductReader.PricedAt(data.Details, repriced);
                return (IReadOnlyList<WhatIfProductRow>)
                [
                    .. data.Products.Select(product =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var whatIf = SavedProductReader.Build(product, details[product.Id], CostBasis.Buffered, repriced).Costs;
                        return new WhatIfProductRow(product, data.Now[product.Id], whatIf);
                    }),
                ];
            },
            cancellationToken);

        // A newer change may have come in while these were worked out: then they're stale, and must neither replace the
        // newer figures nor, by finishing, clear the error the newer one showed.
        cancellationToken.ThrowIfCancellationRequested();
        _allRows = rows;
        ShowRows();
    }

    private void ShowRows()
    {
        Products.Clear();
        foreach (var row in _allRows.Where(row => !OnlyAffected || row.IsAffected))
        {
            Products.Add(row);
        }
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
        var details = await _savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
        return new Data(
            products,
            details,
            products.ToDictionary(p => p.Id, p => SavedProductReader.Build(p, details[p.Id], CostBasis.Buffered).Costs),
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
        IReadOnlyDictionary<int, ProductCostSummary> Now,
        IReadOnlyDictionary<int, RawMaterial> Materials,
        IReadOnlyList<IngredientBomLine> BomLines,
        ILookup<int, RawMaterialBrandPrice> Prices);
}
