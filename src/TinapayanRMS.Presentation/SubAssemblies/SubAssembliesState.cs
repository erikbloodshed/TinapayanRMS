using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// The Sub-Assemblies screen: standalone sub-assemblies, which no product owns. Products link them (and follow every change
/// saved here) or copy them, from their recipe on the Products screen. One Save writes the name, the role and the lines
/// together. Its parts each hold a piece of it: the list (<see cref="List"/>), a recipe's lines (<see cref="Batch"/>), a
/// packaging set's (<see cref="PackagingSet"/>) and the Dependents tab (<see cref="Dependents"/>). The screen keeps the
/// form's own fields, which sub-assembly it shows, loading, Save, and the undo history over them all.
/// </summary>
public sealed class SubAssembliesState : RecordScreenState<SubAssemblyFormSnapshot>, IDisposable, IRecordScreen, ISubAssembliesListHost
{
    /// <summary>The details tabs along the bottom: Recipe, then Dependents.</summary>
    public const int RecipeTab = 0;

    /// <summary>Dependents: the products and made ingredients the sub-assembly goes into.</summary>
    public const int DependentsTab = 1;

    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly VersionedSaves _versionedSaves;

    /// <summary>What a Save of a recipe products link is asked about with; null in a screen built without it.</summary>
    private readonly SharedRecipeImpact? _recipeImpact;

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>
    /// Each sub-assembly picked, by the user or for them (Open, a list pick), nothing picked included: what loads one into
    /// the form. Fed by <see cref="Pick"/> alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<StandaloneSubAssemblyRow?> _picks = new(null);

    private readonly Subject<int> _newProductRequests = new();

    /// <summary>The sub-assembly the form shows: 0 for a new one.</summary>
    private int _editingId;

    /// <summary>Counts form loads, so a load overtaken by a newer one drops its lines instead of adding them too.</summary>
    private int _formLoadVersion;

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
        _recipeImpact = recipeImpact;
        _subAssemblyRepository = subAssemblyRepository;
        _versionedSaves = versionedSaves;
        _lineRepository = lineRepository;
        _roleRepository = roleRepository;
        _rawMaterialRepository = rawMaterialRepository;
        _subAssemblyValidator = subAssemblyValidator;

        // The parts first: the subscriptions below, and the blank form they start with, reach every one. Each makes its own
        // subscriptions as it's made, to what it holds alone.
        List = new SubAssembliesList(
            this, Roles, Ingredients, PackagingMaterials, subAssemblyRepository, roleRepository, rawMaterialCategoryRepository, roleValidator, viewStateStore);
        PackagingSet = new SubAssemblyPackagingSet(PackagingMaterials, _updates);
        Batch = new SubAssemblyBatch(this, Ingredients, _updates, () => !IsPackaging);
        Dependents = new SubAssemblyDependents();

        // What follows the form's fields as they change is called by their setters (SelectionChanged). What follows here
        // runs in this order: the last runs as it's made (_picks starts with nothing picked, which clears the form), so it
        // comes after what it reads.

        // Save offers itself only when the form holds something the saved sub-assembly doesn't. Re-read on every change
        // rather than only when an undo step settles: a step closes on the way out of a field, and a Save button still
        // disabled under the cursor would swallow the very click that enabled it. The lines too, any change to one: grams
        // typed into a recipe's line, or a quantity into a packaging set's, while the line's cell still has the cursor.
        // Watched on the rows themselves, not through the figures they move: those are only worked out as they're read.
        PropertyChanged += (_, _) => History.RefreshDirty();
        Batch.PropertyChanged += (_, _) => History.RefreshDirty();
        PackagingSet.PropertyChanged += (_, _) => History.RefreshDirty();
        RowChanges.OnAnyChange(Batch.Lines, History.RefreshDirty);
        RowChanges.OnAnyChange(PackagingSet.Lines, History.RefreshDirty);

        // The list's row follows the sub-assembly picked (SelectionChanged), from the start.
        List.SyncTreeSelection();

        // Picking a sub-assembly loads it into the form. Only a pick does (Pick, through _picks): a refresh points the
        // selection at its refreshed copy, which leaves the form and its unsaved edits alone.
        LoadLatest(_picks, (row, _) => LoadIntoFormAsync(row));

        // The commands. Making one runs nothing.

        // Runs each time the page opens (ScreenBase) and after this screen's own saves. It merges rather than replaces, so
        // the screen keeps its selection and unsaved lines while picking up what changed elsewhere: a repriced ingredient,
        // a renamed role, a product that links one.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(LoadAsync));

        // A new sub-assembly replaces the one showing, so its unsaved changes are asked about first. Copy doesn't ask: the
        // copy holds them.
        NewCommand = new ScreenCommand(() => AfterUnsavedChangesAsync(() => List.NewInList()));
        CopyCommand = ScreenCommand.Of(CopyForm, () => SelectedRow is not null);
        NewProductCommand = ScreenCommand.Of(AskForNewProduct, () => SelectedRow is { } row && row.SubAssembly.Role?.IsPackaging != true);
        DeleteCommand = new ScreenCommand(() => DeleteSubAssemblyAsync(SelectedRow!), () => SelectedRow is not null);
        SaveCommand = new ScreenCommand(() => RunSaveAsync(SaveAsync), () => History.IsDirty);

        // The line selected in whichever table is showing.
        RemoveLineCommand = ScreenCommand.Of(
            () => AsUndoStep(() =>
            {
                if (IsPackaging)
                {
                    PackagingSet.Lines.Remove(PackagingSet.SelectedLine!);
                }
                else
                {
                    Batch.Lines.Remove(Batch.SelectedLine!);
                }
            }),
            () => IsPackaging ? PackagingSet.SelectedLine is not null : Batch.SelectedLine is not null);
    }

    /// <summary>The list: the sub-assemblies by role, searched, the rows picked, their menus, moves and naming in place.</summary>
    public SubAssembliesList List { get; }

    /// <summary>A recipe's lines in grams, their formula base and totals, and Scale.</summary>
    public SubAssemblyBatch Batch { get; }

    /// <summary>A packaging set's lines and what the set costs, the packaging table's.</summary>
    public SubAssemblyPackagingSet PackagingSet { get; }

    /// <summary>The Dependents tab: the products and made ingredients the sub-assembly goes into.</summary>
    public SubAssemblyDependents Dependents { get; }

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials, for a packaging set's lines.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>
    /// The form is a packaging set: its role is the Packaging role, so it takes packaging materials per set instead of
    /// ingredients in grams, and the screen shows the packaging table in place of the recipe's.
    /// </summary>
    public bool IsPackaging => EditingRole?.IsPackaging == true;

    private StandaloneSubAssemblyRow? _selectedRow;

    /// <summary>
    /// The sub-assembly the form is for. Only <see cref="Pick"/> loads it into the form: a load points it at its fresh copy,
    /// Save at the row it saved, and New clears it, each leaving the form as it is.
    /// </summary>
    public StandaloneSubAssemblyRow? SelectedRow
    {
        get => _selectedRow;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedRow, value))
            {
                SelectionChanged();
            }
        }
    }

    /// <summary>The user picks <paramref name="row"/> (a list's row, Open): it's loaded into the form, unless it's the one showing.</summary>
    public void Pick(StandaloneSubAssemblyRow? row)
    {
        if (row != SelectedRow)
        {
            SelectedRow = row;
            _picks.OnNext(row);
        }
    }

    /// <summary>
    /// What follows the sub-assembly showing, a pick's or a load's fresh copy: one opened, by its Save included, ends the new
    /// one's row, since the form no longer shows the new one; and the list's row follows it.
    /// </summary>
    private void SelectionChanged()
    {
        if (SelectedRow is not null)
        {
            List.EndNewRow();
        }

        List.SyncTreeSelection();
    }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private SubAssemblyRole? _editingRole;
    public SubAssemblyRole? EditingRole
    {
        get => _editingRole;
        set => RaiseAndSetIfChanged(ref _editingRole, value);
    }

    private string _headerTitle = "New sub-assembly";
    /// <summary>The saved name of the sub-assembly the form shows, "New sub-assembly", or "Copy of …" for a copy not yet saved.</summary>
    public string HeaderTitle
    {
        get => _headerTitle;
        private set => RaiseAndSetIfChanged(ref _headerTitle, value);
    }

    private int _selectedDetailsTab = RecipeTab;
    /// <summary>
    /// The details tab showing, kept here so the screen comes back on the tab it was left on, as Products and Raw
    /// Materials do. Both tabs always show, so nothing moves it but the user.
    /// </summary>
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    public ScreenCommand LoadCommand { get; }

    public ScreenCommand NewCommand { get; }

    /// <summary>Starts a new sub-assembly from the one the form shows (<see cref="CopyForm"/>), saved by Save.</summary>
    public ScreenCommand CopyCommand { get; }

    public ScreenCommand SaveCommand { get; }

    public ScreenCommand DeleteCommand { get; }

    /// <summary>Remove Line on whichever table shows: the recipe's lines, or a packaging set's.</summary>
    public ScreenCommand RemoveLineCommand { get; }

    /// <summary>
    /// Starts a new product that links the picked sub-assembly, through <see cref="NewProductRequests"/>. Not for a packaging
    /// set, which isn't a recipe, and not while the form has changes not saved: the product links it as it's saved.
    /// </summary>
    public ScreenCommand NewProductCommand { get; }

    /// <summary>
    /// The sub-assemblies New Product asks a new product to link, by id. ScreenLinks subscribes and starts the new product on
    /// the Products screen, as it opens a Dependents tile's product.
    /// </summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

    /// <summary>The roles, materials and sub-assemblies read again and merged, the form and its selection kept.</summary>
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

        // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
        var selectedId = SelectedRow?.Id;
        var roleId = EditingRole?.Id;

        // A brand-new form has no role until the roles arrive, and the merge below gives it the first one.
        // That isn't the user's change, so what Save measures against has to follow it.
        var formHasNoRole = _editingId == 0 && roleId is null;

        ApplyRefresh(() =>
        {
            Roles.MergeFrom(roles, r => r.Id);
            Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
            PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
            List.TakeRows(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [], madeFrom.GetValueOrDefault(s.Id))));

            // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
            EditingRole = Roles.FirstOrDefault(r => r.Id == roleId) ?? (_editingId == 0 && roleId is null ? Roles.FirstOrDefault() : null);

            Batch.ResolveLines();
            PackagingSet.ResolveRows();

            SelectedRow = List.Rows.FirstOrDefault(r => r.Id == selectedId);
            if (_editingId > 0)
            {
                Dependents.Show(linkedBy.GetValueOrDefault(_editingId) ?? [], madeFrom.GetValueOrDefault(_editingId) ?? [], IsPackaging);
            }
        });

        // Only the role, and only when the form had none: a role the user picked must keep counting as a
        // change, so this can't be a blanket MarkClean.
        if (formHasNoRole && EditingRole is not null)
        {
            History.Rebase((settled, now) => settled with { RoleId = now.RoleId });
        }

        // After the merge, which only points the selection at its fresh copy: this one has to load the sub-assembly's form.
        OpenRecordAskedFor();
    }

    /// <summary>New Product: asks Products for a new product linking the picked sub-assembly, as it's saved.</summary>
    private void AskForNewProduct()
    {
        if (History.IsDirty)
        {
            ErrorMessage = $"Save {HeaderTitle} first, or undo its changes: the new product links it as it's saved.";
            return;
        }

        ErrorMessage = null;
        _newProductRequests.OnNext(SelectedRow!.Id);
    }

    /// <summary>Save: the name, the role and the lines together, with a version of each product it changes.</summary>
    private async Task SaveAsync()
    {
        var name = EditingName.Trim();
        var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingRole?.Id ?? 0 });
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return;
        }

        // The lines as they're typed, read here on the UI thread, for the save below.
        var subAssembly = new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id };
        Func<CancellationToken, Task<int>> save;
        if (IsPackaging)
        {
            var packagingLines = PackagingSet.CurrentLines();
            if (packagingLines.Count == 0)
            {
                ErrorMessage = $"Add the packaging materials that go into {name}, per set.";
                return;
            }

            save = cancellationToken => _subAssemblyRepository.SaveStandalonePackagingAsync(subAssembly, packagingLines, cancellationToken);
        }
        else
        {
            var lines = IngredientLineRow.CompleteLines(Batch.Lines, name);
            if (lines.Count == 0)
            {
                ErrorMessage = $"Add the ingredients that go into {name}, in grams.";
                return;
            }

            List<RecipeLine> recipeLines = [.. lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
            save = cancellationToken => _subAssemblyRepository.SaveStandaloneAsync(subAssembly, recipeLines, cancellationToken);

            // A saved one that products link: what it does to their costs is asked about first.
            if (_recipeImpact is not null && subAssembly.Id != 0)
            {
                List<SubAssemblyLine> newLines =
                [
                    .. lines.Select((l, i) => new SubAssemblyLine
                    {
                        SubAssemblyId = subAssembly.Id, IngredientId = l.Ingredient.Id, Ingredient = l.Ingredient, Grams = l.Grams,
                        IsFormulaBase = l.IsFormulaBase, AuthoredBakersPercent = l.AuthoredBakersPercent, SortOrder = i,
                    }),
                ];
                var impacts = await Task.Run(() => _recipeImpact.ReadAsync(subAssembly.Id, newLines, CancellationToken.None));
                if (impacts.Count > 0 && !await ConfirmDelete.Handle(SharedRecipeImpact.Question(name, impacts, Dependents.MadeIngredients.Count)))
                {
                    return;
                }
            }
        }

        // Written with a version of each product using it that this Save changes, both or neither (VersionedSaves).
        var saved = await Task.Run(() => _versionedSaves.SaveSubAssemblyAsync(subAssembly.Id, name, save, DateTime.Now, CancellationToken.None));

        // The list picks up the new totals; then the form reloads what was saved.
        await LoadCommand.ExecuteAsync();
        var row = List.Rows.FirstOrDefault(r => r.Id == saved.Id);
        SelectedRow = row;
        await LoadIntoFormAsync(row);
        SuccessMessage = ProductVersionRecorder.RecordedMessage(saved.ProductsVersioned) is { } recorded
            ? $"{name} was saved. {recorded}"
            : $"{name} was saved.";
    }

    /// <summary>
    /// New, from the header or the list. A sub-assembly still waiting for LoadCommand to open it is no longer opened: it
    /// would load over the new form, and a name typed into it since would then be saved as that sub-assembly's.
    /// </summary>
    private void StartNewSubAssembly(SubAssemblyRole? role = null)
    {
        ForgetRecordToOpen();
        ClearForm(role);
    }

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, in <paramref name="role"/> or the first role.</summary>
    private void ClearForm(SubAssemblyRole? role = null)
    {
        // The new sub-assembly's row, if one was named in the list, goes with the form it stood for.
        List.EndNewRow();
        History.Clear();
        _formLoadVersion++;
        SelectedRow = null;
        FormVersion++;
        _editingId = 0;
        HeaderTitle = "New sub-assembly";
        EditingName = string.Empty;
        EditingRole = role ?? Roles.FirstOrDefault();
        Batch.Clear();
        PackagingSet.Clear();
        Dependents.Show([], [], IsPackaging);

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// A new, unsaved sub-assembly holding what the form shows, edits not yet saved included: the same role and lines,
    /// named "… Copy" (or "… Copy 2" and on, when that's taken), for Save to add beside the original. The original is
    /// left as it was last saved. Like New, it's a fresh form with nothing to undo, measured against a blank one, so
    /// Save is offered straight away.
    /// </summary>
    private void CopyForm()
    {
        var source = CaptureSnapshot();
        var sourceName = HeaderTitle;
        var name = SubAssemblyRules.UniqueStandaloneName($"{source.Name.Trim()} Copy", [.. List.Rows.Select(r => r.Name)]);

        ClearForm();
        RestoreSnapshot(source with { EditingId = 0, Name = name }, settledThen: null);
        HeaderTitle = $"Copy of {sourceName}";
        Dependents.Show([], [], IsPackaging);
        History.RefreshDirty();
    }

    /// <summary>
    /// The picked sub-assembly's name, role and lines, fetched again so they're what was saved. A load
    /// overtaken by a newer one (another pick, New) is dropped. Nothing picked clears the form.
    /// </summary>
    private async Task LoadIntoFormAsync(StandaloneSubAssemblyRow? row)
    {
        if (row is null)
        {
            ClearForm();
            return;
        }

        // A sub-assembly has one kind of line or the other, as its role says.
        var version = ++_formLoadVersion;
        var isSet = row.SubAssembly.Role?.IsPackaging == true;
        IReadOnlyList<SubAssemblyLine> saved;
        IReadOnlyList<SubAssemblyPackagingLine> savedSet;
        try
        {
            saved = isSet ? [] : await _lineRepository.GetForSubAssembliesAsync([row.Id], CancellationToken.None);
            savedSet = isSet ? await _subAssemblyRepository.GetPackagingLinesAsync(row.Id, CancellationToken.None) : [];
        }
        catch (Exception) when (version == _formLoadVersion)
        {
            // Nothing of the picked one's came: the form mustn't go on showing another sub-assembly under its row, so it's
            // left blank, with nothing picked. Picking it again tries again.
            ClearForm();
            throw;
        }

        if (version != _formLoadVersion)
        {
            return;
        }

        History.Clear();

        // Not for the same sub-assembly loaded again, as a Save does: the boxes still hold what's its.
        if (row.Id != _editingId)
        {
            FormVersion++;
        }

        _editingId = row.Id;
        HeaderTitle = row.Name;
        EditingName = row.Name;
        EditingRole = Roles.FirstOrDefault(r => r.Id == row.SubAssembly.RoleId);
        Batch.Show(saved);
        PackagingSet.Show(savedSet);
        Dependents.Show(row.GetDependents(), row.GetMadeIngredients(), IsPackaging);

        // The form now holds what was saved, so this is what Save's dirty check measures against. Taken at the
        // end: the lines land after the name and role, and a form measured before them would read as changed
        // the moment they arrived.
        History.MarkClean();
    }

    protected override int? FindRecordByName(string text) => RecordNameMatch.Best(List.Rows, row => row.Name, text)?.Id;

    protected override bool ReplacesShowing(int id) => id != SelectedRow?.Id;

    protected override bool TrySelectRecord(int id)
    {
        if (List.Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return false;
        }

        if (!List.VisibleRows.Contains(row))
        {
            List.SearchText = string.Empty;
        }

        Pick(row);
        return true;
    }

    /// <summary>
    /// Deletes <paramref name="row"/>'s sub-assembly once it's confirmed: the header's Delete, for the one picked, or the
    /// list's, for any. The form starts afresh if it was showing it.
    /// </summary>
    private Task DeleteSubAssemblyAsync(StandaloneSubAssemblyRow row) =>
        RunWithConfirmationAsync($"Delete '{row.Name}'? This can't be undone.", async () =>
        {
            await _subAssemblyRepository.DeleteStandaloneAsync(row.Id, CancellationToken.None);
            if (_editingId == row.Id || SelectedRow?.Id == row.Id)
            {
                ClearForm();
            }

            await LoadCommand.ExecuteAsync();
        });

    protected override SubAssemblyFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        RoleId = EditingRole?.Id,
        Lines = Batch.Capture(),
        PackagingLines = PackagingSet.Capture(),
        FormulaBase = Batch.FormulaBase,
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The lines are rebuilt rather than written into —
    /// IngredientLineRow's setters drop a typed percentage — and the lines are pointed at their ingredients and the formula
    /// base applied once, at the end, from the form that was put back.
    /// </summary>
    protected override void RestoreSnapshot(SubAssemblyFormSnapshot snapshot, SubAssemblyFormSnapshot? settledThen)
    {
        ErrorMessage = null;

        // A load still on its way would otherwise land on top of what's being put back.
        _formLoadVersion++;

        ApplyRestore(() => _updates.Batch(() =>
        {
            Batch.SelectedLine = null;
            PackagingSet.SelectedLine = null;

            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingRole = snapshot.RoleId is { } roleId ? Roles.FirstOrDefault(r => r.Id == roleId) : null;

            Batch.Restore(snapshot.Lines, snapshot.FormulaBase);
            PackagingSet.Restore(snapshot.PackagingLines);
        }));

        // Once more, in this order, from the form put back: an undo that leaves a table as it was asks for none of these.
        // The figures need nothing: they're worked out from the lines as they're next read.
        Batch.ResolveLines();
        PackagingSet.ResolveRows();
        Batch.RecomputeFormulaBase();
    }

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim() : SelectedRow?.Name ?? "the new sub-assembly";

    protected override Task SaveFormAsync() => SaveCommand.ExecuteAsync();

    /// <summary>
    /// Don't Save on leaving the screen: the form goes back to the sub-assembly as it was last saved, its lines read again.
    /// A new one (or a copy) was never saved, so it goes back to a blank one.
    /// </summary>
    protected override Task RevertToSavedAsync() => LoadIntoFormAsync(SelectedRow);

    // What the list asks of the screen (ISubAssembliesListHost): the sub-assembly the form shows, and what the list's actions
    // do to it.
    int ISubAssembliesListHost.EditingId => _editingId;

    Task ISubAssembliesListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void ISubAssembliesListHost.Pick(StandaloneSubAssemblyRow row) =>
        PickFromList(row.Id, () => Pick(row), List.SyncTreeSelection);

    void ISubAssembliesListHost.StartNew(SubAssemblyRole? role) => StartNewSubAssembly(role);

    Task ISubAssembliesListHost.DeleteAsync(StandaloneSubAssemblyRow row) => DeleteSubAssemblyAsync(row);

    void ISubAssembliesListHost.NameNew(string name) => AsUndoStep(() => EditingName = name);

    void ISubAssembliesListHost.ClearNewName() => EditingName = string.Empty;

    void ISubAssembliesListHost.RenamedOnForm(string name)
    {
        HeaderTitle = name;
        EditingName = name;
        History.RewriteAll(snapshot => snapshot with { Name = name });
    }

    void ISubAssembliesListHost.MovedOnForm(int roleId)
    {
        EditingRole = Roles.FirstOrDefault(r => r.Id == roleId);
        History.RewriteAll(snapshot => snapshot with { RoleId = roleId });
    }

    void ISubAssembliesListHost.RoleDeleted(int roleId) =>
        History.RewriteAll(snapshot => snapshot.RoleId == roleId ? snapshot with { RoleId = null } : snapshot);

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        List.Dispose();

        Dependents.Dispose();
        _picks.Dispose();
        _newProductRequests.Dispose();
    }
}
