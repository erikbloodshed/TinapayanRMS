using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// The Sub-Assemblies view: the list of standalone sub-assemblies (<see cref="List"/>) beside a strip of tabs, each a
/// sub-assembly's form of its own (<see cref="SubAssemblyForm"/>, through <see cref="RecordViewState{TForm}.Tabs"/>), so
/// several can be open at once, each with its edits and its undo. A sub-assembly picked in the list opens in the preview
/// tab, which the next pick replaces while it holds nothing unsaved; New starts one there too. The view keeps what every
/// form reads (the roles, the ingredients and the packaging materials) and the load that brings them, which keeps each
/// form, and what's typed into it, as it is. It opens on a blank new sub-assembly in the preview tab, as the screen always
/// has.
/// </summary>
public sealed class SubAssembliesState : RecordViewState<SubAssemblyForm>, ISubAssembliesListHost, IScreenPartHost, IRecordOpener, IRecordScreen
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly VersionedSaves _versionedSaves;
    private readonly ILogger _logger;

    /// <summary>What a Save of a recipe products link is asked about with; null in a view built without it.</summary>
    private readonly SharedRecipeImpact? _recipeImpact;

    // What the forms ask of other screens, passed on from whichever form asks (Wire).
    private readonly Subject<int> _productOpenRequests = new();
    private readonly Subject<int> _rawMaterialOpenRequests = new();
    private readonly Subject<int> _newProductRequests = new();

    /// <summary>What <see cref="IRecordScreen.SaveCommand"/> and its Delete are while no tab is open: nothing to do.</summary>
    private readonly ScreenCommand _nothing = ScreenCommand.Of(() => { }, () => false);

    /// <summary>A sub-assembly an address asked for before the list held it: the end of the next load opens it.</summary>
    private int? _recordToOpen;

    public SubAssembliesState(
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository lineRepository,
        ISubAssemblyRoleRepository roleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialCategoryRepository rawMaterialCategoryRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        IValidator<SubAssemblyRole> roleValidator,
        IViewStateStore viewStateStore,
        VersionedSaves versionedSaves,
        ILogger logger,
        SharedRecipeImpact? recipeImpact = null)
        : base(logger)
    {
        _subAssemblyRepository = subAssemblyRepository;
        _lineRepository = lineRepository;
        _roleRepository = roleRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _subAssemblyValidator = subAssemblyValidator;
        _versionedSaves = versionedSaves;
        _logger = logger;
        _recipeImpact = recipeImpact;

        List = new SubAssembliesList(
            this, Roles, Ingredients, PackagingMaterials, subAssemblyRepository, roleRepository, rawMaterialCategoryRepository, roleValidator, viewStateStore);

        // Runs each time the page opens (ScreenBase) and after the forms' own saves. It merges rather than replaces, so each
        // form keeps its selection and unsaved lines while picking up what changed elsewhere: a repriced ingredient, a
        // renamed role, a product that links one. It runs as the form showing does (Runner).
        LoadCommand = new ScreenCommand(() => Runner.RunLoadAsync(LoadAsync));

        // Through the list, which adds the new sub-assembly's row for its name to be typed into (NewInList), and starts it
        // through the view (StartNew).
        NewCommand = ScreenCommand.Of(() => List.NewInList());

        // The list's row follows the form showing, and a new sub-assembly's row being named in the list goes with its tab.
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordTabs<>.Active))
            {
                List.EndNewRow();
                List.SyncTreeSelection();
                RaisePropertyChanged(nameof(Form));
            }
        };

        // The view opens on a blank new sub-assembly, as the screen always has: the first one picked takes its place.
        Show(AddTab(isPreview: true));
    }

    /// <summary>The list: the sub-assemblies by role, searched, the rows picked, their menus, moves and naming in place.</summary>
    public SubAssembliesList List { get; }

    /// <summary>The roles, which the forms' Role pickers offer and the list files the sub-assemblies under.</summary>
    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials, for a packaging set's lines.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>Reads the roles, the materials and the sub-assemblies again, and merges them into the list and every open form.</summary>
    public ScreenCommand LoadCommand { get; }

    /// <summary>
    /// New, from a form's header, the list or the Vim keys' <c>:new</c>: a blank new sub-assembly in the preview tab, so a
    /// tab holding changes is left as it is, and one holding none is used for it.
    /// </summary>
    public ScreenCommand NewCommand { get; }

    /// <summary>The products a form's Dependents tile asks to open, by id. ScreenLinks opens each on Products.</summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The made ingredients a form's Dependents tile asks to open, by id. ScreenLinks opens each on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    /// <summary>The sub-assemblies a form's New Product asks a new product to link, by id. ScreenLinks starts it on Products.</summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

    /// <inheritdoc/>
    public override bool IsOpeningRecord => _recordToOpen is not null;

    protected override SubAssemblyForm NewForm() =>
        new(this, _subAssemblyRepository, _lineRepository, _subAssemblyValidator, _versionedSaves, _logger, _recipeImpact);

    protected override IDisposable Wire(SubAssemblyForm form) => new CompositeDisposable(
        form.Dependents.ProductOpenRequests.Subscribe(_productOpenRequests.OnNext),
        form.Dependents.RawMaterialOpenRequests.Subscribe(_rawMaterialOpenRequests.OnNext),
        form.NewProductRequests.Subscribe(_newProductRequests.OnNext));

    /// <summary>The roles, materials and sub-assemblies read again and merged, every form and its selection kept.</summary>
    private async Task LoadAsync()
    {
        // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
        // back to it before the next began, and waited its turn behind whatever the screen was drawing.
        var rolesRead = Task.Run(() => _roleRepository.GetAllAsync(CancellationToken.None));
        var rawMaterialsRead = Task.Run(() => _rawMaterialRepository.GetAllAsync(CancellationToken.None));
        var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
        var linkedByRead = Task.Run(() => _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None));
        var madeFromRead = Task.Run(() => _subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(CancellationToken.None));
        await Task.WhenAll(rolesRead, rawMaterialsRead, subAssembliesRead, linkedByRead, madeFromRead);

        var roles = await rolesRead;
        var rawMaterials = await rawMaterialsRead;
        var subAssemblies = await subAssembliesRead;
        var linkedBy = await linkedByRead;
        var madeFrom = await madeFromRead;

        // What each form shows, by id, before anything it reads is replaced.
        var forms = Tabs.All.Select(tab => tab.Form).ToList();
        var loads = forms.Select(form => (Form: form, Load: form.BeginLoad())).ToList();

        // Within every form's refresh at once, so nothing the merge writes, into the lists or the forms, is taken for an edit.
        RefreshingEach(forms, () =>
        {
            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                List.TakeRows(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [], madeFrom.GetValueOrDefault(s.Id))));
            });

            foreach (var (form, load) in loads)
            {
                form.TakeLoad(load, linkedBy, madeFrom);
            }
        });

        foreach (var (form, load) in loads)
        {
            await form.EndLoadAsync(load);
        }

        // An address that asked for a sub-assembly before the list held it: the list holds it now, or never will.
        if (_recordToOpen is { } id)
        {
            _recordToOpen = null;
            OpenRecord(id);
        }
    }

    /// <summary>Runs <paramref name="merge"/> within each of <paramref name="forms"/>' own refresh, one inside another.</summary>
    private static void RefreshingEach(IReadOnlyList<SubAssemblyForm> forms, Action merge, int from = 0)
    {
        if (from == forms.Count)
        {
            merge();
            return;
        }

        forms[from].RefreshWith(() => RefreshingEach(forms, merge, from + 1));
    }

    /// <summary>A sub-assembly picked in the list: its tab, or the preview tab.</summary>
    private void OpenFromList(StandaloneSubAssemblyRow row, bool keep = false) => OpenInTab(row.Id, keep, form => form.Pick(row));

    /// <summary>
    /// IRecordOpener: the sub-assembly in the view's address (Back, Forward, a link from another screen, Find), shown as a pick
    /// in the list shows it, a search hiding its row cleared. A list that doesn't hold it yet opens it at the end of the next
    /// load, which showing the view runs.
    /// </summary>
    public void OpenRecord(int id)
    {
        _recordToOpen = null;
        if (List.Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            _recordToOpen = id;
            return;
        }

        if (!List.VisibleRows.Contains(row))
        {
            List.SearchText = string.Empty;
        }

        OpenFromList(row);
    }

    /// <summary>IRecordOpener: the sub-assembly in the address the view opens on, at the end of its load, the list fresh only then.</summary>
    public void OpenRecordOnLoad(int id) => _recordToOpen = id;

    /// <summary>The Vim command line's <c>:e name</c>: opens the sub-assembly the name means best, as an address would.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(List.Rows, row => row.Name, text) is not { } row)
        {
            return false;
        }

        OpenRecord(row.Id);
        return true;
    }

    /// <summary>
    /// New: a blank new sub-assembly in <paramref name="role"/> (or the first role), in the preview tab. A sub-assembly still
    /// waiting for the load to open it is no longer opened: New is the later word.
    /// </summary>
    private void NewDraft(SubAssemblyRole? role)
    {
        _recordToOpen = null;
        var tab = PreviewSlot();
        tab.Form.StartNew(role);
        Show(tab);
    }

    /// <summary>
    /// Deletes <paramref name="row"/>'s sub-assembly once it's confirmed: a form's Delete, for its own, or the list's, for any,
    /// asked as <paramref name="asker"/>'s question (the view's own with none). The tab holding it closes, as it's gone.
    /// </summary>
    internal Task DeleteAsync(StandaloneSubAssemblyRow row, SubAssemblyForm? asker) =>
        ConfirmedAsync(asker, $"Delete '{row.Name}'? This can't be undone.", async () =>
        {
            await _subAssemblyRepository.DeleteStandaloneAsync(row.Id, CancellationToken.None);
            if (Tabs.Holding(row.Id) is { } open)
            {
                Drop(open);
            }

            await LoadCommand.ExecuteAsync();
        });

    /// <summary>Runs <paramref name="action"/> once <paramref name="question"/> is answered Yes, as <paramref name="asker"/>'s (the view's own with none).</summary>
    private Task ConfirmedAsync(SubAssemblyForm? asker, string question, Func<Task> action) =>
        asker is IFormPartHost form ? form.RunWithConfirmationAsync(question, action) : RunWithConfirmationAsync(question, action);

    /// <summary>A form shows another sub-assembly, or none: the list follows it while it's the one showing.</summary>
    internal void FormShowsAnother(SubAssemblyForm form)
    {
        if (form != Form)
        {
            return;
        }

        if (form.SelectedRow is not null)
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
            _productOpenRequests.Dispose();
            _rawMaterialOpenRequests.Dispose();
            _newProductRequests.Dispose();
        }

        base.Dispose(disposing);
    }

    // IRecordScreen, for the Vim command line while no form shows: New starts one, and nothing is there to save or delete.
    ScreenCommand IRecordScreen.SaveCommand => Form?.SaveCommand ?? _nothing;

    ScreenCommand IRecordScreen.DeleteCommand => Form?.DeleteCommand ?? _nothing;

    // What the list asks of the view (ISubAssembliesListHost): the form showing, and what the list's actions do to the forms
    // holding the sub-assemblies they act on. The list's own work runs as the form showing does, as the view's load does.
    StandaloneSubAssemblyRow? ISubAssembliesListHost.SelectedRow => Form?.SelectedRow;

    SubAssemblyRole? ISubAssembliesListHost.EditingRole => Form?.EditingRole;

    Task ISubAssembliesListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void ISubAssembliesListHost.Pick(StandaloneSubAssemblyRow row) => OpenFromList(row);

    void ISubAssembliesListHost.KeepOpen(StandaloneSubAssemblyRow row) => OpenFromList(row, keep: true);

    void ISubAssembliesListHost.StartNew(SubAssemblyRole? role) => NewDraft(role);

    Task ISubAssembliesListHost.DeleteAsync(StandaloneSubAssemblyRow row) => DeleteAsync(row, Form);

    void ISubAssembliesListHost.NameNew(string name) => Form?.NameNew(name);

    void ISubAssembliesListHost.ClearNewName() => Form?.ClearNewName();

    void ISubAssembliesListHost.Renamed(int id, string name) => Tabs.Holding(id)?.Form.TakeRename(name);

    void ISubAssembliesListHost.Moved(IReadOnlyList<SubAssemblyMove> moves)
    {
        foreach (var move in moves)
        {
            Tabs.Holding(move.SubAssemblyId)?.Form.TakeMove(move.RoleId);
        }
    }

    void ISubAssembliesListHost.RoleDeleted(int roleId)
    {
        foreach (var tab in Tabs.All)
        {
            tab.Form.ForgetRole(roleId);
        }
    }

    bool ISubAssembliesListHost.HasUnsavedChangesFor(int id) => Tabs.Holding(id)?.Form.HasUnsavedChanges == true;

    SavedVersionCheck? ISubAssembliesListHost.SavedVersionFor(IEnumerable<int> ids) =>
        ids.Select(id => Tabs.Holding(id)).OfType<RecordTab<SubAssemblyForm>>()
            .Select(tab => ((IRecordListHost)tab.Form).ShowingSavedVersion)
            .FirstOrDefault(check => check is not null);

    bool IRecordListHost.IsDirty => Form?.HasUnsavedChanges == true;

    string? IRecordListHost.ErrorMessage => Runner.ErrorMessage;

    // A new sub-assembly takes a tab of its own, or the preview tab, and replaces no form with changes: nothing to ask.
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
