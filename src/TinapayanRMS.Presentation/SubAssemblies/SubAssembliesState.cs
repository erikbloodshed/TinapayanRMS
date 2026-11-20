using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;
using TinapayanRMS.UseCases.SubAssemblies;

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
public sealed class SubAssembliesState : RecordViewState<SubAssemblyForm>, ISubAssembliesListHost
{
    private readonly ISubAssemblyQueries _queries;
    private readonly ISubAssemblyEditor _editor;
    private readonly ILogger _logger;

    // What the forms ask of other screens, passed on from whichever form asks (Wire).
    private readonly Subject<int> _productOpenRequests = new();
    private readonly Subject<int> _rawMaterialOpenRequests = new();
    private readonly Subject<int> _newProductRequests = new();

    /// <param name="queries">What the view and its forms read.</param>
    /// <param name="editor">What the forms and the list save, delete, rename and move.</param>
    /// <param name="roleEditor">What the list does to the roles, its folders.</param>
    public SubAssembliesState(
        ISubAssemblyQueries queries,
        ISubAssemblyEditor editor,
        ISubAssemblyRoleEditor roleEditor,
        IViewStateStore viewStateStore,
        ILogger logger)
        : base(logger)
    {
        _queries = queries;
        _editor = editor;
        _logger = logger;

        List = new SubAssembliesList(this, Roles, queries, editor, roleEditor, viewStateStore);

        // Runs each time the page opens (ScreenBase) and after the forms' own saves. It merges rather than replaces, so each
        // form keeps its selection and unsaved lines while picking up what changed elsewhere: a repriced ingredient, a
        // renamed role, a product that links one. It runs as the form showing does (Runner).
        LoadCommand = new ScreenCommand(() => Runner.RunLoadAsync(LoadAsync));

        // Through the list, which adds the new sub-assembly's row for its name to be typed into (NewInList), and starts it
        // through the view (StartNew).
        NewCommand = ScreenCommand.Of(() => List.NewInList());

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
    public override ScreenCommand LoadCommand { get; }

    /// <summary>
    /// New, from a form's header, the list or the Vim keys' <c>:new</c>: a blank new sub-assembly in the preview tab, so a
    /// tab holding changes is left as it is, and one holding none is used for it.
    /// </summary>
    public override ScreenCommand NewCommand { get; }

    /// <summary>The products a form's Dependents tile asks to open, by id. ScreenLinks opens each on Products.</summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The made ingredients a form's Dependents tile asks to open, by id. ScreenLinks opens each on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    /// <summary>The sub-assemblies a form's New Product asks a new product to link, by id. ScreenLinks starts it on Products.</summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

    protected override RecordList ViewList => List;

    protected override SubAssemblyForm NewForm() => new(this, _editor, _queries, _logger);

    protected override IDisposable Wire(SubAssemblyForm form) => new CompositeDisposable(
        form.Dependents.ProductOpenRequests.Subscribe(_productOpenRequests.OnNext),
        form.Dependents.RawMaterialOpenRequests.Subscribe(_rawMaterialOpenRequests.OnNext),
        form.NewProductRequests.Subscribe(_newProductRequests.OnNext));

    /// <summary>The roles, materials and sub-assemblies read again and merged, every form and its selection kept.</summary>
    private async Task LoadAsync()
    {
        // Off the UI thread, the reads side by side (ReadCatalogAsync), as Products reads its lists: one after another on the
        // UI thread, each read came back to it before the next began, and waited its turn behind whatever the screen was drawing.
        var (roles, rawMaterials, subAssemblies, linkedBy, madeFrom) = await Task.Run(() => _queries.ReadCatalogAsync(CancellationToken.None));

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

        OpenRecordWaiting();
    }

    /// <summary>A sub-assembly picked in the list: its tab, or the preview tab.</summary>
    private void OpenFromList(StandaloneSubAssemblyRow row, bool keep = false) => OpenInTab(row.Id, keep, form => form.Pick(row));

    /// <summary>
    /// IRecordOpener: the sub-assembly in the view's address (Back, Forward, a link from another screen, Find), shown as a pick
    /// in the list shows it, a search hiding its row cleared. A list that doesn't hold it yet opens it at the end of the next
    /// load, which showing the view runs.
    /// </summary>
    public override void OpenRecord(int id)
    {
        RecordToOpen = null;
        if (List.Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            RecordToOpen = id;
            return;
        }

        if (!List.VisibleRows.Contains(row))
        {
            List.SearchText = string.Empty;
        }

        OpenFromList(row);
    }

    /// <summary>The Vim command line's <c>:e name</c>: opens the sub-assembly the name means best, as an address would.</summary>
    public override bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(List.Rows, row => row.Name, text) is not { } row)
        {
            return false;
        }

        OpenRecord(row.Id);
        return true;
    }

    /// <summary>
    /// Deletes <paramref name="row"/>'s sub-assembly once it's confirmed: a form's Delete, for its own, or the list's, for any,
    /// asked as <paramref name="asker"/>'s question (the view's own with none). The tab holding it closes, as it's gone.
    /// </summary>
    internal Task DeleteAsync(StandaloneSubAssemblyRow row, SubAssemblyForm? asker) =>
        DeleteRecordAsync(
            row.Id,
            asker,
            $"Delete '{row.Name}'? This can't be undone.",
            () => _editor.DeleteAsync(row.Id, CancellationToken.None));

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

    // What the list asks of the view (ISubAssembliesListHost) on top of what any record view's list asks (RecordViewState):
    // the form showing, and what the list's actions do to the forms holding the sub-assemblies they act on.
    StandaloneSubAssemblyRow? ISubAssembliesListHost.SelectedRow => Form?.SelectedRow;

    SubAssemblyRole? ISubAssembliesListHost.EditingRole => Form?.EditingRole;

    Task ISubAssembliesListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void ISubAssembliesListHost.Pick(StandaloneSubAssemblyRow row) => OpenFromList(row);

    void ISubAssembliesListHost.KeepOpen(StandaloneSubAssemblyRow row) => OpenFromList(row, keep: true);

    void ISubAssembliesListHost.StartNew(SubAssemblyRole? role) => OpenDraft(form => form.StartNew(role));

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

    bool ISubAssembliesListHost.IsMaterialName(string name) =>
        Ingredients.Concat(PackagingMaterials).Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
