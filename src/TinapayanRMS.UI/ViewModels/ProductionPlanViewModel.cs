using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
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

/// <summary>
/// The Production Plan screen (2026-10-03): the products to make and how many, and from them the mixes to make in whole
/// (or half) batches and the raw materials to buy (ProductionPlanBuilder). The plan in the table is kept for this Windows
/// user on this computer between runs (IViewStateStore), worked out again from the products as saved whenever the screen
/// is shown. A plan can also be saved under a name, in the database, to open again (2026-10-24).
/// </summary>
public sealed class ProductionPlanViewModel : ViewModelBase, IDisposable
{
    /// <summary>Where the plan is kept: one "product id:units" for each line, in the table's order.</summary>
    public const string PlanKey = "production-plan";

    /// <summary>Where Half batches is kept: "true" when it's ticked.</summary>
    public const string HalfBatchesKey = "production-plan-half-batches";

    /// <summary>Where the saved plan open in the table is kept, by id: none when no saved plan is open.</summary>
    public const string OpenPlanKey = "production-plan-open";

    public const string ExportTitle = "Production Plan";

    private readonly IProductRepository _productRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly ISavedPlanRepository _savedPlanRepository;
    private readonly SavedProductReader _savedProducts;
    private readonly IViewStateStore _viewState;

    private readonly Subject<RxVoid> _recalculations = new();
    private readonly HashSet<PlanLineRow> _followed = [];

    /// <summary>Everything the plan is worked out from, as last loaded; null before the first load.</summary>
    private Data? _data;

    /// <summary>The plan as it was kept, put into the table by the first load, once the products are known.</summary>
    private IReadOnlyList<string>? _kept;

    /// <summary>The saved plan open as it was kept, by id, found by the first load; null once it's been looked for.</summary>
    private int? _keptOpenPlanId;

    /// <summary>True while a load or an opened plan puts the lines in or right, which isn't an edit of each line.</summary>
    private bool _restoring;

    /// <summary>The saved plan open in the table, if any, and what it holds as saved, to tell whether the table has changed since.</summary>
    private SavedPlan? _openPlan;
    private PlanContents _savedContents = PlanContents.Empty;

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

    private bool _halfBatches;
    /// <summary>
    /// Each mix is rounded up to half batches rather than whole ones (2026-10-24), for a kitchen that can mix half a
    /// dough. Kept for this Windows user, as the plan is, and saved with a saved plan.
    /// </summary>
    public bool HalfBatches
    {
        get => _halfBatches;
        set
        {
            if (_halfBatches == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _halfBatches, value);
            _viewState.WriteList(HalfBatchesKey, value ? ["true"] : []);
            if (!_restoring)
            {
                Recalculate();
            }
        }
    }

    private decimal _totalCost;
    /// <summary>What the materials needed cost, at their buffered costs.</summary>
    public decimal TotalCost
    {
        get => _totalCost;
        private set => this.RaiseAndSetIfChanged(ref _totalCost, value);
    }

    private decimal _totalBuyCost;
    /// <summary>What the whole purchase units to buy cost, at their brands' prices: the money to take to the market.</summary>
    public decimal TotalBuyCost
    {
        get => _totalBuyCost;
        private set => this.RaiseAndSetIfChanged(ref _totalBuyCost, value);
    }

    private decimal _totalSales;
    /// <summary>What the plan's products sell for, with VAT (PlanLineRow.Sales).</summary>
    public decimal TotalSales
    {
        get => _totalSales;
        private set => this.RaiseAndSetIfChanged(ref _totalSales, value);
    }

    private decimal _totalProfit;
    /// <summary>What they earn over their base cost, once VAT is out (PlanLineRow.Profit).</summary>
    public decimal TotalProfit
    {
        get => _totalProfit;
        private set => this.RaiseAndSetIfChanged(ref _totalProfit, value);
    }

    private decimal _profitMarginPercent;
    /// <summary>The profit as a percentage of the sales before VAT, as the Products screen's Profit margin is; 0 with no sales.</summary>
    public decimal ProfitMarginPercent
    {
        get => _profitMarginPercent;
        private set => this.RaiseAndSetIfChanged(ref _profitMarginPercent, value);
    }

    private string? _salesNote;
    /// <summary>Which products have no Current Price, so are counted at their worked-out selling price; null when all have one.</summary>
    public string? SalesNote
    {
        get => _salesNote;
        private set => this.RaiseAndSetIfChanged(ref _salesNote, value);
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

    /// <summary>
    /// The add row's line while it's typed into (2026-10-24). It isn't in Lines until the add row is committed, but the
    /// plan follows it, so a product picked there and its units show in the tables straight away.
    /// </summary>
    private PlanLineRow? _newLine;

    /// <summary>The plan table's add row's item, which the plan follows until it's committed or dropped.</summary>
    public Func<PlanLineRow> NewLine => () =>
    {
        ReleaseNewLine();
        var line = CreateLine();
        line.PropertyChanged += NewLineEdited;
        _newLine = line;
        return line;
    };

    /// <summary>The add row's line thrown away rather than committed (controls:GridAddNewRow.ItemDropped): out of the plan.</summary>
    public Action<object> DropNewLine => line =>
    {
        if (ReferenceEquals(line, _newLine))
        {
            ReleaseNewLine();
            Recalculate();
        }
    };

    private IReadOnlyList<SavedPlan> _plans = [];
    /// <summary>The saved plans, newest first.</summary>
    public IReadOnlyList<SavedPlan> Plans
    {
        get => _plans;
        private set => this.RaiseAndSetIfChanged(ref _plans, value);
    }

    private SavedPlan? _selectedPlan;
    /// <summary>
    /// The plan the Saved Plans list shows picked: the one open. Clicking another opens it (<see cref="OpenPlanCommand"/>),
    /// which sets this once it has, or puts the open one back when the user keeps the table instead. The list letting go
    /// of its pick, as it can when its rows are replaced, closes nothing.
    /// </summary>
    public SavedPlan? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (value is not null && !ReferenceEquals(value, _selectedPlan))
            {
                OpenPlanCommand.Execute(value).Subscribe();
            }
        }
    }

    /// <summary>
    /// Whether the table holds a plan that isn't saved as it is: with a saved plan open, one changed since it was opened
    /// or saved; with none, any product in it. Open asks before replacing it.
    /// </summary>
    public bool HasUnsavedChanges => _openPlan is null ? Contents().Lines.Count > 0 : !Contents().Matches(_savedContents);

    /// <summary>Input: the plan on paper. The View shows it in a print pane.</summary>
    public Interaction<ProductPrintSet, RxVoid> ShowPrint { get; } = new();

    /// <summary>Input: what to ask for. Output: the name typed, or null when cancelled. The View shows a TextPromptWindow.</summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    /// <summary>Runs whenever the tab is shown (MainWindow): the products and prices as they are now, the plan kept.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    /// <summary>Empties the table, and closes the saved plan open, if any.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> PrintCommand { get; }

    /// <summary>The plan, the mixing sheet and the purchase requisition as the tables show them, to an Excel workbook.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }

    /// <summary>Opens a saved plan: its products replace the table's, after asking when those aren't saved.</summary>
    public ReactiveCommand<SavedPlan, RxVoid> OpenPlanCommand { get; }

    /// <summary>Saves the table's plan under a name asked for (<see cref="PromptForName"/>), the open plan's to start with.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SavePlanCommand { get; }

    /// <summary>Deletes the open saved plan, after asking. Its products stay in the table.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DeletePlanCommand { get; }

    public ProductionPlanViewModel(
        IProductRepository productRepository,
        IRawMaterialRepository rawMaterialRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        ISavedPlanRepository savedPlanRepository,
        SavedProductReader savedProducts,
        IViewStateStore viewState,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _bomLineRepository = bomLineRepository;
        _brandPriceRepository = brandPriceRepository;
        _savedPlanRepository = savedPlanRepository;
        _savedProducts = savedProducts;
        _viewState = viewState;
        _kept = viewState.ReadList(PlanKey);
        _halfBatches = viewState.ReadList(HalfBatchesKey) is ["true"];
        _keptOpenPlanId = viewState.ReadList(OpenPlanKey) is [var openId] && int.TryParse(openId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

        Lines.CollectionChanged += LinesChanged;

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            _data = await Task.Run(ReadAsync);

            // The picker keeps its list when no product changed, so it isn't rebuilt each time the tab is shown.
            List<Product> products = [.. _data.Products.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
            if (products.Count != Products.Count || !products.Zip(Products).All(pair => CollectionMerge.HasSameValues(pair.First, pair.Second)))
            {
                Products = products;
            }

            // The lines go in or are put right all at once, then the plan is kept and worked out once, not per line.
            var removed = false;
            _restoring = true;
            try
            {
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
                        if (line.ProductId is { } productId && !_data.Products.ContainsKey(productId))
                        {
                            Lines.Remove(line);
                            removed = true;
                        }
                        else
                        {
                            line.FindProduct();
                        }
                    }
                }
            }
            finally
            {
                _restoring = false;
            }

            if (removed)
            {
                Keep();
            }

            ShowPlans(_data.Plans);
            if (_keptOpenPlanId is { } keptId)
            {
                // The saved plan open when the app last ran, with what it holds as saved: the table may have changed since.
                _keptOpenPlanId = null;
                if (_data.Plans.FirstOrDefault(p => p.Id == keptId) is { } open)
                {
                    _openPlan = open;
                    _savedContents = PlanContents.Of(open);
                    ShowSelectedPlan(open);
                }
            }

            Recalculate();
        }));

        RemoveLineCommand = ReactiveCommand.Create(
            () => { Lines.Remove(SelectedLine!); },
            this.WhenAnyValue(x => x.SelectedLine).Select(line => line is not null));

        // Starting over: the table empties, and no saved plan is open any more.
        ClearCommand = ReactiveCommand.Create(() =>
        {
            Lines.Clear();
            ShowOpenPlan(null);
        });
        PrintCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () => await ShowPrint.Handle(PrintSet(DateTime.Now))));
        ExportCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ExportTitle));
        OpenPlanCommand = ReactiveCommand.CreateFromTask<SavedPlan>(OpenPlanAsync);
        SavePlanCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(SavePlanAsync));
        DeletePlanCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the saved plan {_selectedPlan!.Name}? The products stay in the table.", DeletePlanAsync),
            this.WhenAnyValue(x => x.SelectedPlan).Select(plan => plan is not null));

        // A newer edit cancels a plan still being worked out, and one that fails says why and keeps the plan it had.
        LoadLatest(_recalculations, (_, cancellationToken) => RecalculateAsync(cancellationToken));
    }

    /// <summary>The plan on paper, as the tables show it now.</summary>
    public ProductPrintSet PrintSet(DateTime printedAt) => ProductPrintSet.Of(
        new PrintedPlan([.. Lines.Where(l => l.Product is not null && l.Units is > 0m)], [.. Mixes], [.. Requisition], TotalCost, Notes)
        {
            PlanName = _openPlan?.Name,
            HalfBatches = HalfBatches,
            TotalBuyCost = TotalBuyCost,
            TotalSales = TotalSales,
            TotalProfit = TotalProfit,
            ProfitMarginPercent = ProfitMarginPercent,
            SalesNote = SalesNote,
        },
        printedAt);

    private PlanLineRow CreateLine() => new(Find, id => _data?.Source.Products.GetValueOrDefault(id)?.Costs);

    private void Recalculate() => _recalculations.OnNext(RxVoid.Default);

    private async Task RecalculateAsync(CancellationToken cancellationToken)
    {
        if (_data is not { } data)
        {
            return;
        }

        // A product listed twice is made once, its units added up. The add row's line counts too, until it's a line itself.
        List<PlanLineRow> planned = [.. _newLine is { } typed ? Lines.Append(typed) : Lines];
        List<(int ProductId, decimal Units)> orders =
        [
            .. planned.Where(l => l.ProductId is not null && l.Units is > 0m)
                .GroupBy(l => l.ProductId!.Value)
                .Select(g => (g.Key, g.Sum(l => l.Units!.Value))),
        ];
        ShowSales(planned);

        var halfBatches = HalfBatches;
        var result = await Task.Run(() => ProductionPlanBuilder.Build(orders, data.Source, halfBatches), cancellationToken);

        // A newer edit may have come in meanwhile: then this plan is stale, and mustn't replace the newer one or clear its error.
        cancellationToken.ThrowIfCancellationRequested();

        // Merged, not refilled: an edit changes a few rows, and a grid rebuilding all of them took about 30 ms of the UI
        // thread for each table (2026-10-24). A mix is matched by its name, a material by its id.
        Mixes.MergeFrom(result.Mixes, mix => mix.Name);
        Requisition.MergeFrom(result.Requisition, row => row.MaterialId);
        TotalCost = result.TotalCost;
        TotalBuyCost = result.TotalBuyCost;
        if (!Notes.SequenceEqual(result.Notes))
        {
            Notes = result.Notes;
        }
    }

    /// <summary>The plan's sales and profit, its lines' added up, and which products are counted at their worked-out price.</summary>
    private void ShowSales(IReadOnlyList<PlanLineRow> lines)
    {
        var figures = lines.Select(l => l.SalesFigures).OfType<PlanSales>().ToList();
        TotalSales = figures.Sum(f => f.Sales);
        TotalProfit = figures.Sum(f => f.Profit);
        ProfitMarginPercent = CostingRules.PercentOf(TotalProfit, figures.Sum(f => f.SalesBeforeVat));
        var unpriced = lines.Where(l => l.HasNoCurrentPrice && l.SalesFigures is not null).Select(l => l.ProductName).Distinct().ToList();
        SalesNote = unpriced.Count == 0
            ? null
            : $"No Current Price for {string.Join(", ", unpriced)}, so {(unpriced.Count == 1 ? "its sales are" : "their sales are")} at the selling price its costing works out.";
    }

    /// <summary>Every product, priced as saved, every material, BOM line and brand price, and the saved plans, read at once.</summary>
    private async Task<Data> ReadAsync()
    {
        var productsRead = _productRepository.GetAllAsync(CancellationToken.None);
        var materialsRead = _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var bomLinesRead = _bomLineRepository.GetAllAsync(CancellationToken.None);
        var pricesRead = _brandPriceRepository.GetAllAsync(CancellationToken.None);
        var plansRead = _savedPlanRepository.GetAllAsync(CancellationToken.None);
        await Task.WhenAll(productsRead, materialsRead, bomLinesRead, pricesRead, plansRead);

        var products = await productsRead;
        var saved = await _savedProducts.ReadAllAsync(products, CostBasis.Buffered, CancellationToken.None);
        return new Data(
            products.ToDictionary(p => p.Id),
            ProductionPlanSource.From(
                saved.ToDictionary(s => s.Product.Id),
                (await materialsRead).ToDictionary(m => m.Id),
                await bomLinesRead,
                (await pricesRead).ToLookup(p => p.RawMaterialId)),
            await plansRead);
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
                var line = CreateLine();
                line.ProductId = id;
                line.Units = units;
                yield return line;
            }
        }
    }

    /// <summary>
    /// Keeps the plan for next time: every line with a product, as "id:units". Units are written without trailing zeros, as
    /// typed: a saved plan's come back from the database with four places ("40.0000").
    /// </summary>
    private void Keep() => _viewState.WriteList(
        PlanKey,
        [.. Lines.Where(l => l.ProductId is not null).Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.ProductId}:{l.Units ?? 0m:0.##########}"))]);

    /// <summary>The table's plan as a saved plan holds it: the lines with a product and units to make, in order.</summary>
    private PlanContents Contents() => new(
        [.. Lines.Where(l => l.ProductId is not null && l.Units is > 0m).Select(l => (l.ProductId!.Value, l.Units!.Value))],
        HalfBatches);

    /// <summary>
    /// Opens <paramref name="plan"/>: its products replace the table's, in its order, and its half batches are ticked or
    /// not. When the table holds a plan that isn't saved, the user is asked first, and keeping it picks the open plan
    /// again in the list. A product deleted since is left out.
    /// </summary>
    private async Task OpenPlanAsync(SavedPlan plan)
    {
        // The list's pick calls this while it's still setting it: going on after the pick is over lets the list take
        // the plan open back, when the user keeps the table instead.
        await Task.Yield();
        await RunSafelyAsync(async () =>
        {
            if (HasUnsavedChanges && !await ConfirmDelete.Handle(
                    $"Open the saved plan {plan.Name}? The plan in the table isn't saved, and it replaces it."))
            {
                ShowSelectedPlan(_openPlan);
                return;
            }

            var leftOut = 0;
            _restoring = true;
            try
            {
                Lines.Clear();
                foreach (var saved in plan.Lines)
                {
                    if (Find(saved.ProductId) is null)
                    {
                        leftOut++;
                        continue;
                    }

                    var line = CreateLine();
                    line.ProductId = saved.ProductId;
                    line.Units = saved.Units;
                    Lines.Add(line);
                }

                HalfBatches = plan.HalfBatches;
            }
            finally
            {
                _restoring = false;
            }

            Keep();
            Recalculate();
            ShowOpenPlan(plan);
            if (leftOut > 0)
            {
                SuccessMessage = $"Opened {plan.Name}, leaving out {leftOut} {(leftOut == 1 ? "product" : "products")} deleted since.";
            }
        });
    }

    /// <summary>
    /// Saves the table's plan under a name asked for: the open plan's, to start with. Saving under another plan's name
    /// replaces its products, after asking.
    /// </summary>
    private async Task SavePlanAsync()
    {
        var contents = Contents();
        if (contents.Lines.Count == 0)
        {
            throw new BusinessRuleException(SavedPlanRules.NoLinesText);
        }

        var name = (await PromptForName.Handle(new TextPromptRequest(
            "Save Plan",
            "Name",
            _openPlan?.Name ?? string.Empty,
            "Saves the products in the plan, and whether it's in half batches, under this name, to open here again.")))?.Trim();
        if (name is null)
        {
            return;
        }

        if (Plans.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { } other
            && other.Id != _openPlan?.Id
            && !await ConfirmDelete.Handle($"Replace the plan saved as {other.Name} with the one in the table?"))
        {
            return;
        }

        var id = await _savedPlanRepository.SaveAsync(
            name,
            [.. contents.Lines.Select(l => new SavedPlanLine { ProductId = l.ProductId, Units = l.Units })],
            contents.HalfBatches,
            DateTime.Now,
            CancellationToken.None);
        ShowPlans(await _savedPlanRepository.GetAllAsync(CancellationToken.None));
        var savedPlan = Plans.Single(p => p.Id == id);
        ShowOpenPlan(savedPlan);
        SuccessMessage = $"Saved the plan {savedPlan.Name}.";
    }

    private async Task DeletePlanAsync()
    {
        var plan = _selectedPlan!;
        await _savedPlanRepository.DeleteAsync(plan.Id, CancellationToken.None);
        ShowOpenPlan(null);
        ShowPlans(await _savedPlanRepository.GetAllAsync(CancellationToken.None));
        SuccessMessage = $"Deleted the saved plan {plan.Name}.";
    }

    /// <summary>The saved plans as read, the open one pointed at its fresh copy; one deleted since is no longer open.</summary>
    private void ShowPlans(IReadOnlyList<SavedPlan> plans)
    {
        Plans = plans;
        if (_openPlan is { } open)
        {
            var fresh = plans.FirstOrDefault(p => p.Id == open.Id);
            _openPlan = fresh;
            if (fresh is null)
            {
                _savedContents = PlanContents.Empty;
                _viewState.WriteList(OpenPlanKey, []);
            }
        }

        ShowSelectedPlan(_openPlan);
    }

    /// <summary>Makes <paramref name="plan"/> the open one, with the table as it is now its saved contents, and keeps which it is.</summary>
    private void ShowOpenPlan(SavedPlan? plan)
    {
        _openPlan = plan;
        _savedContents = plan is null ? PlanContents.Empty : Contents();
        _viewState.WriteList(OpenPlanKey, plan is null ? [] : [plan.Id.ToString(CultureInfo.InvariantCulture)]);
        ShowSelectedPlan(plan);
    }

    /// <summary>
    /// Shows <paramref name="plan"/> picked in the list. Told every time, even when it hasn't changed: a pick the user took
    /// back has to be undone in the list too.
    /// </summary>
    private void ShowSelectedPlan(SavedPlan? plan)
    {
        _selectedPlan = plan;
        this.RaisePropertyChanged(nameof(SelectedPlan));
    }

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

        // The add row's line committed: followed as a line from now on.
        if (_newLine is { } typed && _followed.Contains(typed))
        {
            ReleaseNewLine();
        }

        Edited();
    }

    private void NewLineEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlanLineRow.ProductId) or nameof(PlanLineRow.Units))
        {
            Recalculate();
        }
    }

    private void ReleaseNewLine()
    {
        if (_newLine is { } line)
        {
            line.PropertyChanged -= NewLineEdited;
            _newLine = null;
        }
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
        // A load or an opened plan putting the lines in or right keeps and works out the plan itself, once it's done.
        if (_restoring)
        {
            return;
        }

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

    private sealed record Data(IReadOnlyDictionary<int, Product> Products, ProductionPlanSource Source, IReadOnlyList<SavedPlan> Plans);

    /// <summary>A plan as a saved plan holds it: each product and its units, in order, and its half batches.</summary>
    private sealed record PlanContents(IReadOnlyList<(int ProductId, decimal Units)> Lines, bool HalfBatches)
    {
        public static PlanContents Empty { get; } = new([], false);

        public static PlanContents Of(SavedPlan plan) => new([.. plan.Lines.Select(l => (l.ProductId, l.Units))], plan.HalfBatches);

        public bool Matches(PlanContents other) => HalfBatches == other.HalfBatches && Lines.SequenceEqual(other.Lines);
    }
}
