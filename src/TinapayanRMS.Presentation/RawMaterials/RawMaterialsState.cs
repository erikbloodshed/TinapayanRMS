using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// The Raw Materials view: ingredients and packaging in one list (<see cref="List"/>) beside a strip of tabs, each a
/// material's form of its own (<see cref="RawMaterialForm"/>, through <see cref="RecordViewState{TForm}.Tabs"/>), so several
/// can be open at once, each with its edits and its undo. A material picked in the list opens in the preview tab, which the
/// next pick replaces while it holds nothing unsaved; New and the list's Copy start one there too. The view keeps what
/// every form reads (the materials, the categories, the units of measure and Settings' cost buffer) and the load that
/// brings them, which keeps each form, and what's typed into it, as it is. It opens on a blank new material in the preview
/// tab, as the screen always has.
/// </summary>
public sealed class RawMaterialsState : RecordViewState<RawMaterialForm>, IRawMaterialsListHost, IScreenPartHost, IRecordOpener, IRecordScreen
{
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly IValidator<RawMaterialUnit> _unitValidator;
    private readonly IValidator<RawMaterialBrandPrice> _brandPriceValidator;
    private readonly IUnitOfMeasureRepository _unitOfMeasureRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly ILogger _logger;
    private readonly MaterialCostShares? _costShares;
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;

    // What the forms ask of other screens, passed on from whichever form asks (Wire).
    private readonly Subject<int> _productOpenRequests = new();
    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>What <see cref="IRecordScreen.SaveCommand"/> and its Delete are while no tab is open: nothing to do.</summary>
    private readonly ScreenCommand _nothing = ScreenCommand.Of(() => { }, () => false);

    /// <summary>A material an address asked for before the list held it: the end of the next load opens it.</summary>
    private int? _recordToOpen;

    public RawMaterialsState(
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        IIngredientBomLineRepository bomLineRepository,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialBrandPrice> brandPriceValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        IViewStateStore viewStateStore,
        ICostingDefaultsRepository costingDefaultsRepository,
        ILogger logger,
        MaterialCostShares? costShares = null)
        : base(logger)
    {
        _rawMaterialRepository = rawMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _brandPriceRepository = brandPriceRepository;
        _bomLineRepository = bomLineRepository;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;
        _rawMaterialValidator = rawMaterialValidator;
        _unitValidator = unitValidator;
        _brandPriceValidator = brandPriceValidator;
        _unitOfMeasureRepository = unitOfMeasureRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _logger = logger;
        _costShares = costShares;

        // The lists the forms and the list read, bound first. The categories before the list's own subscriptions to them, so
        // its folders read them changed.
        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);
        CategoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();
        RawMaterialCache.Connect()
            .SortAndBind(out _rawMaterials, byName)
            .Subscribe();
        RawMaterialCache.Connect()
            .Filter(m => m.Type == RawMaterialType.Ingredient)
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        List = new RawMaterialsList(
            this, RawMaterialCache, CategoryCache, RawMaterials, Categories, rawMaterialRepository, categoryRepository, categoryValidator, viewStateStore);

        // Runs each time the page opens (ScreenBase) and after the forms' own saves. It merges rather than replaces, so each
        // form keeps its selection, picker choices and unsaved edits while picking up what changed elsewhere, such as a unit
        // renamed in Settings. It runs as the form showing does (Runner).
        LoadCommand = new ScreenCommand(() => Runner.RunLoadAsync(LoadAsync));

        // Through the list, which adds the new material's row for its name to be typed into (NewInList), and starts it
        // through the view (StartNew).
        NewCommand = ScreenCommand.Of(() => List.NewInList());

        // The list's row follows the form showing, and a new material's row being named in the list goes with its tab.
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordTabs<>.Active))
            {
                List.EndNewRow();
                List.SyncTreeSelection();
                RaisePropertyChanged(nameof(Form));
            }
        };

        // The view opens on a blank new material, as the screen always has: the first one picked takes its place.
        Show(AddTab(isPreview: true));
    }

    /// <summary>The list: the materials by type and category, searched, the rows picked, their menus, moves and naming in place.</summary>
    public RawMaterialsList List { get; }

    /// <summary>Every material, by id: what the list searches and moves, and the forms' own pickers follow.</summary>
    internal SourceCache<RawMaterial, int> RawMaterialCache { get; } = new(m => m.Id);

    /// <summary>Every category, by id: the list's folders, and what the forms' Category pickers follow.</summary>
    internal SourceCache<RawMaterialCategory, int> CategoryCache { get; } = new(c => c.Id);

    /// <summary>Every raw material, by name.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The ingredients only, by name: packaging never goes in a recipe.</summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    /// <summary>Every category, by name.</summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    /// <summary>Every unit of measure, by name, for the forms' base unit pickers and Alternative Units tables.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

    /// <summary>What a new material's Cost Buffer % starts at (Settings → Costing Defaults), as of the last load.</summary>
    internal decimal DefaultCostBufferPercent { get; private set; } = CostingDefaults.DefaultCostBufferPercent;

    /// <summary>Reads the materials, categories, units of measure and Settings' cost buffer again, and merges them into the list and every open form.</summary>
    public ScreenCommand LoadCommand { get; }

    /// <summary>
    /// New, from a form's header, the list or the Vim keys' <c>:new</c>: a blank new material in the preview tab, so a tab
    /// holding changes is left as it is, and one holding none is used for it.
    /// </summary>
    public ScreenCommand NewCommand { get; }

    /// <summary>The products a form's Dependents tile asks to open, by id. ScreenLinks opens each on Products.</summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The sub-assemblies a made ingredient's Made from asks to open, by id. ScreenLinks opens each on Sub-Assemblies.</summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    /// <inheritdoc/>
    public override bool IsOpeningRecord => _recordToOpen is not null;

    protected override RawMaterialForm NewForm() => new(
        this, _rawMaterialRepository, _priceHistoryRepository, _brandPriceRepository, _bomLineRepository, _subAssemblyLineRepository,
        _categoryRepository, _categoryValidator, _rawMaterialValidator, _unitValidator, _brandPriceValidator, _logger, _costShares);

    protected override IDisposable Wire(RawMaterialForm form) => new CompositeDisposable(
        form.Dependents.ProductOpenRequests.Subscribe(_productOpenRequests.OnNext),
        form.Recipe.SubAssemblyOpenRequests.Subscribe(_subAssemblyOpenRequests.OnNext));

    /// <summary>The materials, categories, units of measure and cost buffer read again and merged, every form and its selection kept.</summary>
    private async Task LoadAsync()
    {
        // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
        // back to it before the next began, and waited its turn behind whatever the screen was drawing. The products using
        // each form's material are read with them, since Products may have started or stopped using one meanwhile.
        var categoriesRead = Task.Run(() => _categoryRepository.GetAllAsync(CancellationToken.None));
        var unitsRead = Task.Run(() => _unitOfMeasureRepository.GetAllAsync(CancellationToken.None));
        var rawMaterialsRead = Task.Run(() => _rawMaterialRepository.GetAllAsync(CancellationToken.None));
        var costingDefaultsRead = Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
        var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
        var dependentsRead = Tabs.All
            .Select(tab => tab.Form.DetailMaterialId)
            .OfType<int>()
            .Distinct()
            .ToDictionary(id => id, id => Task.Run(() => _rawMaterialRepository.GetDependentsAsync(id, CancellationToken.None)));
        await Task.WhenAll([categoriesRead, unitsRead, rawMaterialsRead, costingDefaultsRead, subAssembliesRead, .. dependentsRead.Values]);

        var categories = await categoriesRead;
        var units = await unitsRead;
        var rawMaterials = await rawMaterialsRead;
        var costingDefaults = await costingDefaultsRead;
        var subAssemblies = await subAssembliesRead;

        // What each form shows, by id, before anything it reads is replaced.
        var forms = Tabs.All.Select(tab => tab.Form).ToList();
        var loads = forms.Select(form => (Form: form, Load: form.BeginLoad())).ToList();

        // Within every form's refresh at once, so nothing the merge writes, into the lists or the forms, is taken for an edit.
        RefreshingEach(forms, () =>
        {
            ApplyRefresh(() =>
            {
                CategoryCache.MergeFrom(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                RawMaterialCache.MergeFrom(rawMaterials);
                DefaultCostBufferPercent = costingDefaults.CostBufferPercent;
            });

            foreach (var (form, load) in loads)
            {
                form.TakeLoad(load, subAssemblies, costingDefaults.CostBufferPercent);
            }
        });

        foreach (var (form, load) in loads)
        {
            await form.EndLoadAsync(load, dependentsRead);
        }

        // An address that asked for a material before the list held it: the list holds it now, or never will.
        if (_recordToOpen is { } id)
        {
            _recordToOpen = null;
            OpenRecord(id);
        }
    }

    /// <summary>Runs <paramref name="merge"/> within each of <paramref name="forms"/>' own refresh, one inside another.</summary>
    private static void RefreshingEach(IReadOnlyList<RawMaterialForm> forms, Action merge, int from = 0)
    {
        if (from == forms.Count)
        {
            merge();
            return;
        }

        forms[from].RefreshWith(() => RefreshingEach(forms, merge, from + 1));
    }

    /// <summary>A form's own Save read its material back: the list takes the fresh copy at once.</summary>
    internal void TakeSaved(RawMaterial saved) => RawMaterialCache.AddOrUpdate(saved);

    /// <summary>A material picked in the list: its tab, or the preview tab.</summary>
    private void OpenFromList(RawMaterial material, bool keep = false) => OpenInTab(material.Id, keep, form => form.Pick(material));

    /// <summary>
    /// IRecordOpener: the material in the view's address (Back, Forward, a link from another screen, Find), shown as a pick in
    /// the list shows it, a search hiding its row cleared. A list that doesn't hold it yet opens it at the end of the next
    /// load, which showing the view runs.
    /// </summary>
    public void OpenRecord(int id)
    {
        _recordToOpen = null;
        if (RawMaterials.FirstOrDefault(m => m.Id == id) is not { } material)
        {
            _recordToOpen = id;
            return;
        }

        if (!List.VisibleRawMaterials.Contains(material))
        {
            List.SearchText = string.Empty;
        }

        OpenFromList(material);
    }

    /// <summary>IRecordOpener: the material in the address the view opens on, at the end of its load, the list fresh only then.</summary>
    public void OpenRecordOnLoad(int id) => _recordToOpen = id;

    /// <summary>The Vim command line's <c>:e name</c>: opens the material the name means best, as an address would.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(RawMaterials, material => material.Name, text) is not { } material)
        {
            return false;
        }

        OpenRecord(material.Id);
        return true;
    }

    /// <summary>
    /// New, or the list's Copy: a fresh form in the preview tab, which <paramref name="start"/> fills. A material still
    /// waiting for the load to open it is no longer opened: New is the later word.
    /// </summary>
    private void NewDraft(Action<RawMaterialForm> start)
    {
        _recordToOpen = null;
        var tab = PreviewSlot();
        start(tab.Form);
        Show(tab);
    }

    /// <summary>
    /// Deletes <paramref name="material"/> once it's confirmed: a form's Delete, for its own, or the list's, for any, asked as
    /// <paramref name="asker"/>'s question (the view's own with none). The tab holding it closes, as it's gone.
    /// </summary>
    internal Task DeleteAsync(RawMaterial material, RawMaterialForm? asker) =>
        ConfirmedAsync(asker, $"Delete '{material.Name}'? This can't be undone.", async () =>
        {
            await _rawMaterialRepository.DeleteAsync(material.Id, CancellationToken.None);
            if (Tabs.Holding(material.Id) is { } open)
            {
                Drop(open);
            }

            await LoadCommand.ExecuteAsync();
        });

    /// <summary>Runs <paramref name="action"/> once <paramref name="question"/> is answered Yes, as <paramref name="asker"/>'s (the view's own with none).</summary>
    private Task ConfirmedAsync(RawMaterialForm? asker, string question, Func<Task> action) =>
        asker is IFormPartHost form ? form.RunWithConfirmationAsync(question, action) : RunWithConfirmationAsync(question, action);

    /// <summary>A form shows another material, or none: the list follows it while it's the one showing.</summary>
    internal void FormShowsAnother(RawMaterialForm form)
    {
        if (form != Form)
        {
            return;
        }

        if (form.SelectedRawMaterial is not null)
        {
            List.EndNewRow();
        }

        List.SyncTreeSelection();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A change still settling when the app closes is written now, rather than lost.
            List.Dispose();
        }

        base.Dispose(disposing);

        if (disposing)
        {
            RawMaterialCache.Dispose();
            CategoryCache.Dispose();
            _productOpenRequests.Dispose();
            _subAssemblyOpenRequests.Dispose();
        }
    }

    // IRecordScreen, for the Vim command line while no form shows: New starts one, and nothing is there to save or delete.
    ScreenCommand IRecordScreen.SaveCommand => Form?.SaveCommand ?? _nothing;

    ScreenCommand IRecordScreen.DeleteCommand => Form?.DeleteCommand ?? _nothing;

    // What the list asks of the view (IRawMaterialsListHost): the form showing, and what the list's actions do to the forms
    // holding the materials they act on. The list's own work runs as the form showing does, as the view's load does.
    RawMaterial? IRawMaterialsListHost.SelectedRawMaterial => Form?.SelectedRawMaterial;

    RawMaterialCategory? IRawMaterialsListHost.EditingCategory => Form?.EditingCategory;

    Task IRawMaterialsListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void IRawMaterialsListHost.Pick(RawMaterial material) => OpenFromList(material);

    void IRawMaterialsListHost.KeepOpen(RawMaterial material) => OpenFromList(material, keep: true);

    void IRawMaterialsListHost.StartNew(RawMaterialType type, int? categoryId, RawMaterialSource source) =>
        NewDraft(form => form.StartNew(type, categoryId, source));

    void IRawMaterialsListHost.StartCopyOf(RawMaterial material) => NewDraft(form => form.StartCopyOf(material));

    Task IRawMaterialsListHost.DeleteAsync(RawMaterial material) => DeleteAsync(material, Form);

    void IRawMaterialsListHost.NameNew(string name) => Form?.NameNew(name);

    void IRawMaterialsListHost.ClearNewName() => Form?.ClearNewName();

    void IRawMaterialsListHost.Renamed(int id, string name) => Tabs.Holding(id)?.Form.TakeRename(name);

    void IRawMaterialsListHost.Moved(IReadOnlyList<RawMaterialMove> moves)
    {
        foreach (var move in moves)
        {
            Tabs.Holding(move.RawMaterialId)?.Form.TakeMove(move.CategoryId);
        }
    }

    void IRawMaterialsListHost.CategoryDeleted(int categoryId)
    {
        foreach (var tab in Tabs.All)
        {
            tab.Form.ForgetCategory(categoryId);
        }
    }

    SavedVersionCheck? IRawMaterialsListHost.SavedVersionFor(IEnumerable<int> ids) =>
        ids.Select(id => Tabs.Holding(id)).OfType<RecordTab<RawMaterialForm>>()
            .Select(tab => ((IRecordListHost)tab.Form).ShowingSavedVersion)
            .FirstOrDefault(check => check is not null);

    bool IRecordListHost.IsDirty => Form?.HasUnsavedChanges == true;

    string? IRecordListHost.ErrorMessage => Runner.ErrorMessage;

    // A new material takes a tab of its own, or the preview tab, and replaces no form with changes: nothing to ask.
    Task IRecordListHost.AfterUnsavedChangesAsync(Action then)
    {
        then();
        return Task.CompletedTask;
    }

    SavedVersionCheck? IRecordListHost.ShowingSavedVersion => (Form as IRecordListHost)?.ShowingSavedVersion;

    Task IRecordListHost.LoadAfterSavingAsync(IEnumerable<int> ids)
    {
        var saved = ids.ToList();
        foreach (var tab in Tabs.All)
        {
            tab.Form.FollowOwnSaveOf(saved);
        }

        return LoadCommand.ExecuteAsync();
    }

    bool IFormPartHost.IsBusyApplying => Form is IFormPartHost form ? form.IsBusyApplying : IsBusyApplying;

    Task IFormPartHost.RunWithConfirmationAsync(string question, Func<Task> action) => ConfirmedAsync(Form, question, action);

    void IFormPartHost.ApplyRefresh(Action merge) => ApplyRefresh(merge);

    BackgroundWork IScreenPartHost.Work => Form is IScreenPartHost form ? form.Work : Work;

    Task IScreenPartHost.RunSafelyAsync(Func<Task> action) => Form is IScreenPartHost form ? form.RunSafelyAsync(action) : RunSafelyAsync(action);

    void IScreenPartHost.LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load) => LoadLatest(values, load);

    void IScreenPartHost.ShowError(string? message)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowError(message);
        }
        else
        {
            ErrorMessage = message;
        }
    }

    void IScreenPartHost.ShowSuccess(string? message)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowSuccess(message);
        }
        else
        {
            SuccessMessage = message;
        }
    }

    void IScreenPartHost.ShowFailure(Exception exception, string? context)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowFailure(exception, context);
        }
        else
        {
            ShowFailure(exception, context);
        }
    }

    void IUndoRecorder.BeginUndoStep() => (Form as IUndoRecorder)?.BeginUndoStep();

    void IUndoRecorder.EndUndoStep() => (Form as IUndoRecorder)?.EndUndoStep();

    bool IUndoRecorder.IsEditPending
    {
        get => Form is IUndoRecorder form && form.IsEditPending;
        set
        {
            if (Form is IUndoRecorder form)
            {
                form.IsEditPending = value;
            }
        }
    }

    int IUndoRecorder.FormVersion => (Form as IUndoRecorder)?.FormVersion ?? 0;
}
