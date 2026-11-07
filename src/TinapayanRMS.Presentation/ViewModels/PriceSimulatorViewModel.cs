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
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Price Simulator screen: try raw material price changes and see every product's cost, suggested price and
/// markup with them, beside what they are now. A change reaches made and linked ingredients as a real one would
/// (IngredientCostRules.Reprice) and products through the same pricing as Reports (SavedProductReader). No real price is
/// changed. The changes last while the app is open, and can be saved under a name as a scenario to open again.
/// </summary>
public sealed class PriceSimulatorViewModel : ViewModelBase, IDisposable
{
    public const string ExportTitle = "Price Simulator";

    private readonly IProductRepository _productRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly IPriceScenarioRepository _scenarioRepository;
    private readonly SavedProductReader _savedProducts;

    /// <summary>The scenario whose changes the table holds, as last opened or saved; null for none.</summary>
    private PriceScenario? _openScenario;

    /// <summary>The table's changes as last opened or saved, to tell whether it holds changes that aren't saved.</summary>
    private IReadOnlyList<ScenarioLineKey> _savedLines = [];

    private readonly Subject<RxVoid> _recalculations = new();
    private readonly Subject<int> _productOpenRequests = new();
    private readonly HashSet<SimulatedPriceChange> _followed = [];

    /// <summary>Everything the figures are worked out from, as last loaded; null before the first load.</summary>
    private Data? _data;

    /// <summary>Every product's rows from the last recalculation, before the Only affected filter.</summary>
    private IReadOnlyList<PriceSimulatorProductRow> _allRows = [];

    /// <summary>
    /// The last recalculation's products at the simulated prices, by id, and the changes it tried, each with the materials it
    /// reaches: what the report prints, so it always matches the table, even while a refused change waits to be put right.
    /// </summary>
    private IReadOnlyDictionary<int, SavedProduct> _now = new Dictionary<int, SavedProduct>();
    private Dictionary<int, SavedProduct> _simulated = [];
    private IReadOnlyList<(PrintedPriceChange Change, IReadOnlySet<int> Reaches)> _tried = [];

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the materials' costs every figure is at: buffered, as always, until the
    /// Products card's Unbuffered button (<see cref="ToggleCostBasisCommand"/>) switches to their true costs, as the Products
    /// screen's does. The table, Print Report and Export to Excel all follow it. For the session only: nothing is saved, and
    /// it isn't part of a scenario.
    /// </summary>
    public CostBasis CostBasis
    {
        get => _costBasis;
        private set
        {
            this.RaiseAndSetIfChanged(ref _costBasis, value);
            this.RaisePropertyChanged(nameof(IsUnbuffered));
            this.RaisePropertyChanged(nameof(CostBasisButtonLabel));
            this.RaisePropertyChanged(nameof(CostBasisButtonTooltip));
        }
    }

    /// <summary>The figures are at the materials' true costs: the Products card's badge says so.</summary>
    public bool IsUnbuffered => CostBasis == CostBasis.Unbuffered;

    /// <summary>The switch, named for the costs a click switches to, as on Products: "Unbuffered", then "Buffered".</summary>
    public string CostBasisButtonLabel => IsUnbuffered ? "Buffered" : "Unbuffered";

    /// <summary>What the switch does from here.</summary>
    public string CostBasisButtonTooltip => IsUnbuffered
        ? "Shows every product's figures, now and simulated, at the materials' buffered costs again, as Print Report and Export to Excel will. Nothing is saved."
        : "Shows every product's figures, now and simulated, at the materials' true costs, without their cost buffers, as Print Report and Export to Excel will. Nothing is saved.";

    /// <summary>Unbuffered or Buffered, pressed: every product is priced again at the other of the materials' costs, from what's already read.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ToggleCostBasisCommand { get; }

    /// <summary>The workbook Export to Excel suggests: "Price Simulator", with " (Unbuffered)" while the figures are.</summary>
    public string ExportTitleNow => IsUnbuffered ? $"{ExportTitle} (Unbuffered)" : ExportTitle;

    /// <summary>The price changes being tried, as typed in the table.</summary>
    public ObservableCollection<SimulatedPriceChange> Changes { get; } = [];

    /// <summary>Purchased ingredients and packaging, by name: what a price change can be tried on.</summary>
    private IReadOnlyList<RawMaterial> _materials = [];
    public IReadOnlyList<RawMaterial> Materials
    {
        get => _materials;
        private set => this.RaiseAndSetIfChanged(ref _materials, value);
    }

    /// <summary>The products, all of them or only those a change reaches.</summary>
    public ObservableCollection<PriceSimulatorProductRow> Products { get; } = [];

    /// <summary>The Products table's picked rows (its SelectedItems), Ctrl or Shift held for several: what Print Report's Selected products prints.</summary>
    public ObservableCollection<object> SelectedProducts { get; } = [];

    private Func<IReadOnlyList<PriceSimulatorProductRow>>? _tableShown;

    /// <summary>
    /// The products as the table shows them, filtered and sorted: what Print Report's All products shown prints, in that
    /// order. The screen lends its table while it shows it (<see cref="ShowProductsFrom"/>); until then, and once the screen
    /// has gone, <see cref="Products"/> as they are.
    /// </summary>
    public IReadOnlyList<PriceSimulatorProductRow> ShownProducts() => _tableShown?.Invoke() ?? Products;

    /// <summary>
    /// Has <see cref="ShownProducts"/> read the screen's table until what this returns is disposed, as the screen is left.
    /// The ViewModel lives as long as the window's page, and the screen is drawn afresh on each visit: a table it kept
    /// past its screen would hold that screen, its grid and the rows it last showed until the next visit.
    /// </summary>
    public IDisposable ShowProductsFrom(Func<IReadOnlyList<PriceSimulatorProductRow>> table)
    {
        _tableShown = table;
        return System.Reactive.Disposables.Disposable.Create(() =>
        {
            // Only its own: a screen shown since lent its own table.
            if (_tableShown == table)
            {
                _tableShown = null;
            }
        });
    }

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

    private SimulatedPriceChange? _selectedChange;
    public SimulatedPriceChange? SelectedChange
    {
        get => _selectedChange;
        set => this.RaiseAndSetIfChanged(ref _selectedChange, value);
    }

    /// <summary>The Price Changes table's add row's item.</summary>
    public Func<SimulatedPriceChange> NewChange => () => new SimulatedPriceChange(Find);

    /// <summary>The saved scenarios, newest first.</summary>
    private IReadOnlyList<PriceScenario> _scenarios = [];
    public IReadOnlyList<PriceScenario> Scenarios
    {
        get => _scenarios;
        private set => this.RaiseAndSetIfChanged(ref _scenarios, value);
    }

    private PriceScenario? _selectedScenario;
    /// <summary>
    /// The scenario the Scenarios list shows picked: the one open. Clicking another opens it (<see cref="OpenScenarioCommand"/>),
    /// which sets this once it has, or puts the open one back when the user keeps the table instead. The list letting go
    /// of its pick, as it can when its rows are replaced, closes nothing.
    /// </summary>
    public PriceScenario? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (value is not null && !ReferenceEquals(value, _selectedScenario))
            {
                OpenScenarioCommand.Execute(value).Subscribe();
            }
        }
    }

    /// <summary>Whether the table holds price changes that no scenario has as they are: Open asks before replacing them.</summary>
    public bool HasUnsavedChanges => !ScenarioLineKey.Of(ScenarioLines()).SequenceEqual(_savedLines);

    /// <summary>Opens a saved scenario: its changes replace the table's, after asking when those aren't saved.</summary>
    public ReactiveCommand<PriceScenario, RxVoid> OpenScenarioCommand { get; }

    /// <summary>Saves the table's changes under a name asked for (<see cref="PromptForName"/>), the open scenario's to start with.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SaveScenarioCommand { get; }

    /// <summary>Deletes the open scenario, after asking. The changes stay in the table.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DeleteScenarioCommand { get; }

    /// <summary>Input: what to ask for. Output: the name typed, or null when cancelled. The View shows a TextPromptWindow.</summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    /// <summary>Runs whenever the tab is shown (MainWindow): today's products and prices, the changes kept.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveChangeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    /// <summary>The products as the table shows them, and the changes, to an Excel workbook.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }

    /// <summary>Each product's recipe and costing at the simulated prices, beside today's, in a print preview (<see cref="ShowPrint"/>).</summary>
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

    public PriceSimulatorViewModel(
        IProductRepository productRepository,
        IRawMaterialRepository rawMaterialRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        IPriceScenarioRepository scenarioRepository,
        SavedProductReader savedProducts,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _bomLineRepository = bomLineRepository;
        _brandPriceRepository = brandPriceRepository;
        _scenarioRepository = scenarioRepository;
        _savedProducts = savedProducts;

        Changes.CollectionChanged += ChangesChanged;

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            _data = await Task.Run(ReadAsync);
            Materials = [.. _data.Materials.Values.Where(m => m.Source == RawMaterialSource.Purchased).OrderBy(m => m.Name)];
            ShowScenarios(_data.Scenarios);

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
        // Starting over: the table empties, and no scenario is open any more.
        ClearCommand = ReactiveCommand.Create(() =>
        {
            Changes.Clear();
            ShowOpenScenario(null);
        });
        OpenScenarioCommand = ReactiveCommand.CreateFromTask<PriceScenario>(OpenScenarioAsync);
        SaveScenarioCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(SaveScenarioAsync));
        DeleteScenarioCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the scenario {_selectedScenario!.Name}? The price changes stay in the table.", DeleteScenarioAsync),
            this.WhenAnyValue(x => x.SelectedScenario).Select(scenario => scenario is not null));
        ExportCommand = ReactiveCommand.CreateFromTask(() => ExportToExcelAsync(ExportTitleNow));
        ToggleCostBasisCommand = ReactiveCommand.Create(() =>
        {
            CostBasis = IsUnbuffered ? CostBasis.Buffered : CostBasis.Unbuffered;
            Recalculate();
        });
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
        var basis = CostBasis;
        var now = data.NowAt(basis);
        List<(SimulatedPriceChange Change, (int MaterialId, decimal Cost, decimal BufferedCost) Costs)> tried =
        [
            .. Changes.Select(change => (Change: change, Costs: change.NewCosts())).Where(t => t.Costs is not null).Select(t => (t.Change, t.Costs!.Value)),
        ];
        List<(int MaterialId, decimal Cost, decimal BufferedCost)> newCosts = [.. tried.Select(t => t.Costs)];
        EnsureNoMaterialTwice(newCosts.Select(c => c.MaterialId));

        List<PrintedPriceChange> printed = [.. tried.Select(t => Printed(t.Change))];
        var (rows, simulatedById, reaches) = await Task.Run(
            () =>
            {
                var repriced = IngredientCostRules.Reprice(data.Materials, data.BomLines, newCosts.ToDictionary(c => c.MaterialId, c => (c.Cost, c.BufferedCost)));
                var details = SavedProductReader.PricedAt(data.Details, repriced);
                Dictionary<int, SavedProduct> simulatedById = [];
                foreach (var product in data.Products)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    simulatedById[product.Id] = SavedProductReader.Build(product, details[product.Id], basis, repriced);
                }

                IReadOnlyList<PriceSimulatorProductRow> rows = [.. data.Products.Select(p => new PriceSimulatorProductRow(p, now[p.Id].Costs, simulatedById[p.Id].Costs))];
                List<IReadOnlySet<int>> reaches = [.. newCosts.Select(c => IngredientCostRules.ReachedBy(data.Materials.Values, data.BomLines, c.MaterialId))];
                return (rows, simulatedById, reaches);
            },
            cancellationToken);

        // A newer change may have come in while these were worked out: then they're stale, and must neither replace the
        // newer figures nor, by finishing, clear the error the newer one showed.
        cancellationToken.ThrowIfCancellationRequested();
        _allRows = rows;
        _now = now;
        _simulated = simulatedById;
        _tried = [.. printed.Zip(reaches)];
        ShowRows();
    }

    /// <summary>Refuses a material in the table twice, by name: which of its rows would count isn't clear.</summary>
    private void EnsureNoMaterialTwice(IEnumerable<int> materialIds)
    {
        if (materialIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            var name = _data?.Materials.GetValueOrDefault(repeated.Key)?.Name ?? "A raw material";
            throw new BusinessRuleException($"{name} is in the table twice. Remove one of its rows.");
        }
    }

    /// <summary>The table's changes as a scenario keeps them, in its order: the rows with a material and a price or change typed.</summary>
    private List<PriceScenarioLine> ScenarioLines() =>
        [.. Changes.Select(change => change.ToScenarioLine()).OfType<PriceScenarioLine>()];

    /// <summary>
    /// Opens <paramref name="scenario"/>: its changes replace the table's, in its order. A percentage goes on each material's
    /// price now, and a new price stays as it was saved. When the table holds changes that aren't saved, the user is
    /// asked first, and keeping them picks the open scenario again in the list. A material no longer bought is left out.
    /// </summary>
    private async Task OpenScenarioAsync(PriceScenario scenario)
    {
        // The list's pick calls this while it's still setting it: going on after the pick is over lets the list take
        // the scenario open back, when the user keeps the table instead.
        await Task.Yield();
        await RunSafelyAsync(async () =>
        {
            if (HasUnsavedChanges && !await ConfirmDelete.Handle(
                    $"Open the scenario {scenario.Name}? The price changes in the table aren't saved, and it replaces them."))
            {
                ShowSelectedScenario(_openScenario);
                return;
            }

            Changes.Clear();
            List<string> leftOut = [];
            foreach (var line in scenario.Lines)
            {
                var change = NewChange();
                change.Open(line);
                if (change.Material is null)
                {
                    leftOut.Add(_data?.Materials.GetValueOrDefault(line.RawMaterialId)?.Name ?? "a raw material deleted since");
                    continue;
                }

                Changes.Add(change);
            }

            ShowOpenScenario(scenario);
            if (leftOut.Count > 0)
            {
                SuccessMessage = $"Opened {scenario.Name}, leaving out {string.Join(", ", leftOut)}: no longer bought, so no price to change.";
            }
        });
    }

    /// <summary>
    /// Saves the table's changes as a scenario, under a name asked for: the open scenario's, to start with. Saving under
    /// another scenario's name replaces its changes, after asking. A change that can't be is refused as the figures refuse it.
    /// </summary>
    private async Task SaveScenarioAsync()
    {
        foreach (var change in Changes)
        {
            change.NewCosts();
        }

        var lines = ScenarioLines();
        EnsureNoMaterialTwice(lines.Select(l => l.RawMaterialId));
        if (lines.Count == 0)
        {
            throw new BusinessRuleException(PriceScenarioRules.NoChangesText);
        }

        var name = (await PromptForName.Handle(new TextPromptRequest(
            "Save Scenario",
            "Name",
            _openScenario?.Name ?? string.Empty,
            "Saves the price changes in the table under this name, to open here again. No real price changes.")))?.Trim();
        if (name is null)
        {
            return;
        }

        if (Scenarios.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } other
            && other.Id != _openScenario?.Id
            && !await ConfirmDelete.Handle($"Replace the price changes saved as {other.Name} with the ones in the table?"))
        {
            return;
        }

        var id = await _scenarioRepository.SaveAsync(name, lines, DateTime.Now, CancellationToken.None);
        ShowScenarios(await _scenarioRepository.GetAllAsync(CancellationToken.None));
        var saved = Scenarios.Single(s => s.Id == id);
        ShowOpenScenario(saved);
        SuccessMessage = $"Saved the scenario {saved.Name}.";
    }

    private async Task DeleteScenarioAsync()
    {
        var scenario = _selectedScenario!;
        await _scenarioRepository.DeleteAsync(scenario.Id, CancellationToken.None);
        ShowOpenScenario(null);
        ShowScenarios(await _scenarioRepository.GetAllAsync(CancellationToken.None));
        SuccessMessage = $"Deleted the scenario {scenario.Name}.";
    }

    /// <summary>The scenarios as read, the open one pointed at its fresh copy; one deleted since is no longer open.</summary>
    private void ShowScenarios(IReadOnlyList<PriceScenario> scenarios)
    {
        Scenarios = scenarios;
        if (_openScenario is { } open)
        {
            var fresh = scenarios.FirstOrDefault(s => s.Id == open.Id);
            _openScenario = fresh;
            if (fresh is null)
            {
                _savedLines = [];
            }
        }

        ShowSelectedScenario(_openScenario);
    }

    /// <summary>Makes <paramref name="scenario"/> the open one, with the table's changes as they are now its saved ones.</summary>
    private void ShowOpenScenario(PriceScenario? scenario)
    {
        _openScenario = scenario;
        _savedLines = scenario is null ? [] : ScenarioLineKey.Of(ScenarioLines());
        ShowSelectedScenario(scenario);
    }

    /// <summary>
    /// Shows <paramref name="scenario"/> picked in the list. Told every time, even when it hasn't changed: a pick the user took
    /// back has to be undone in the list too.
    /// </summary>
    private void ShowSelectedScenario(PriceScenario? scenario)
    {
        _selectedScenario = scenario;
        this.RaisePropertyChanged(nameof(SelectedScenario));
    }

    /// <summary>
    /// Shows the rows the Only affected filter leaves. They're new rows each time, so the picked products are picked again
    /// by id: a change typed or the filter ticked doesn't lose them.
    /// </summary>
    private void ShowRows()
    {
        HashSet<int> picked = [.. SelectedProducts.OfType<PriceSimulatorProductRow>().Select(r => r.ProductId)];
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
        HashSet<int> picked = [.. SelectedProducts.OfType<PriceSimulatorProductRow>().Select(r => r.ProductId)];
        List<PriceSimulatorProductRow> selected = [.. shown.Where(r => picked.Contains(r.ProductId))];

        List<PrintChoice> choices = [];
        if (selected.Count > 0)
        {
            choices.Add(new PrintChoice($"Selected products ({selected.Count})", Report(selected, printedAt)));
        }

        choices.Add(new PrintChoice($"All products shown ({shown.Count})", Report(shown, printedAt)));
        return choices;
    }

    private ProductPrintSet Report(IReadOnlyList<PriceSimulatorProductRow> rows, DateTime printedAt)
    {
        List<ProductPrintout> printouts = [];
        if (_data is { } data)
        {
            foreach (var row in rows)
            {
                var materialIds = PriceSimulatorReport.MaterialIdsOf(row.ProductId, data.Details);
                IReadOnlyList<PrintedPriceChange> reaching = [.. _tried.Where(t => t.Reaches.Overlaps(materialIds)).Select(t => t.Change)];
                printouts.Add(PriceSimulatorReport.Printout(_now[row.ProductId], _simulated[row.ProductId], reaching, printedAt));
            }
        }

        return PriceSimulatorReport.Set(printouts, [.. _tried.Select(t => t.Change)], printedAt);
    }

    /// <summary>A price change as the table shows it, for the report. Only a row with its new price worked out is tried.</summary>
    private static PrintedPriceChange Printed(SimulatedPriceChange change) =>
        new(change.Material!.Name, change.UnitName, change.CurrentPrice, change.ChangePercent, change.NewPrice!.Value);

    /// <summary>Every product, read and priced as saved, and every material, BOM line and brand price, read at once.</summary>
    private async Task<Data> ReadAsync()
    {
        var productsRead = _productRepository.GetAllAsync(CancellationToken.None);
        var materialsRead = _rawMaterialRepository.GetAllAsync(CancellationToken.None);
        var bomLinesRead = _bomLineRepository.GetAllAsync(CancellationToken.None);
        var pricesRead = _brandPriceRepository.GetAllAsync(CancellationToken.None);
        var scenariosRead = _scenarioRepository.GetAllAsync(CancellationToken.None);
        await Task.WhenAll(productsRead, materialsRead, bomLinesRead, pricesRead, scenariosRead);

        var products = await productsRead;
        var details = await _savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
        return new Data(
            products,
            details,
            products.ToDictionary(p => p.Id, p => SavedProductReader.Build(p, details[p.Id], CostBasis.Buffered)),
            products.ToDictionary(p => p.Id, p => SavedProductReader.Build(p, details[p.Id], CostBasis.Unbuffered)),
            (await materialsRead).ToDictionary(m => m.Id),
            [.. await bomLinesRead],
            (await pricesRead).ToLookup(p => p.RawMaterialId),
            await scenariosRead);
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
        if (e.PropertyName is nameof(SimulatedPriceChange.MaterialId) or nameof(SimulatedPriceChange.NewPrice) or nameof(SimulatedPriceChange.ChangePercent))
        {
            Recalculate();
        }
    }

    public void Dispose()
    {
        _recalculations.Dispose();
        _productOpenRequests.Dispose();
    }

    /// <summary>What everything is worked out from. Each product is priced as saved at both bases once, so a switch reads nothing again.</summary>
    private sealed record Data(
        IReadOnlyList<Product> Products,
        IReadOnlyDictionary<int, ProductDetail> Details,
        IReadOnlyDictionary<int, SavedProduct> NowBuffered,
        IReadOnlyDictionary<int, SavedProduct> NowUnbuffered,
        IReadOnlyDictionary<int, RawMaterial> Materials,
        IReadOnlyList<IngredientBomLine> BomLines,
        ILookup<int, RawMaterialBrandPrice> Prices,
        IReadOnlyList<PriceScenario> Scenarios)
    {
        /// <summary>Every product as saved, priced at <paramref name="basis"/>.</summary>
        public IReadOnlyDictionary<int, SavedProduct> NowAt(CostBasis basis) => basis == CostBasis.Unbuffered ? NowUnbuffered : NowBuffered;
    }

    /// <summary>A scenario line's material and what was typed for it, compared by value: what makes two tables the same.</summary>
    private readonly record struct ScenarioLineKey(int MaterialId, decimal? ChangePercent, decimal? NewPrice)
    {
        public static IReadOnlyList<ScenarioLineKey> Of(IEnumerable<PriceScenarioLine> lines) =>
            [.. lines.Select(l => new ScenarioLineKey(l.RawMaterialId, l.ChangePercent, l.NewPrice))];
    }
}
