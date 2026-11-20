using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Subjects;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// One tab of the Sub-Assemblies view (<see cref="SubAssembliesState"/>): a standalone sub-assembly's form, a new one's, or
/// a copy's. Products link standalone sub-assemblies (and follow every change saved here) or copy them, from their recipe
/// on the Products screen. One Save writes the name, the role and the lines together. Its parts each hold a piece of it: a
/// recipe's lines (<see cref="Batch"/>), a packaging set's (<see cref="PackagingSet"/>) and the Dependents tab
/// (<see cref="Dependents"/>). The form keeps its own fields, which sub-assembly it shows, Save, and the undo history over
/// them all; the view keeps the list, what the form's pickers offer, and the load that brings them (<see cref="View"/>).
/// </summary>
public sealed class SubAssemblyForm : RecordScreenState<SubAssemblyFormSnapshot>, IDisposable, IRecordScreen, ITabForm
{
    /// <summary>The details tabs along the bottom: Recipe, then Dependents.</summary>
    public const int RecipeTab = 0;

    /// <summary>Dependents: the products and made ingredients the sub-assembly goes into.</summary>
    public const int DependentsTab = 1;

    private readonly SubAssembliesState _view;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly VersionedSaves _versionedSaves;

    /// <summary>What a Save of a recipe products link is asked about with; null in a view built without it.</summary>
    private readonly SharedRecipeImpact? _recipeImpact;

    /// <summary>The form's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>
    /// Each sub-assembly picked, by the user or for them (Open, a list pick), nothing picked included: what loads one into
    /// the form. Fed by <see cref="Pick"/> alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<StandaloneSubAssemblyRow?> _picks = new(null);

    private readonly Subject<int> _newProductRequests = new();

    /// <summary>Counts form loads, so a load overtaken by a newer one drops its lines instead of adding them too.</summary>
    private int _formLoadVersion;

    internal SubAssemblyForm(
        SubAssembliesState view,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository lineRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        VersionedSaves versionedSaves,
        ILogger logger,
        SharedRecipeImpact? recipeImpact)
        : base(logger)
    {
        _view = view;
        _recipeImpact = recipeImpact;
        _subAssemblyRepository = subAssemblyRepository;
        _versionedSaves = versionedSaves;
        _lineRepository = lineRepository;
        _subAssemblyValidator = subAssemblyValidator;

        // The parts first: the subscriptions below, and the blank form they start with, reach every one. Each makes its own
        // subscriptions as it's made, to what it holds alone.
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
        // Not on the form's own word that it has changes to save, which this answers.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HasUnsavedChanges))
            {
                History.RefreshDirty();
            }
        };
        Batch.PropertyChanged += (_, _) => History.RefreshDirty();
        PackagingSet.PropertyChanged += (_, _) => History.RefreshDirty();
        RowChanges.OnAnyChange(Batch.Lines, History.RefreshDirty);
        RowChanges.OnAnyChange(PackagingSet.Lines, History.RefreshDirty);

        // The tab follows whether there's anything to save (its dot).
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(History.IsDirty))
            {
                RaisePropertyChanged(nameof(HasUnsavedChanges));
            }
        };

        // Picking a sub-assembly loads it into the form. Only a pick does (Pick, through _picks): a refresh points the
        // selection at its refreshed copy, which leaves the form and its unsaved edits alone.
        LoadLatest(_picks, (row, _) => LoadIntoFormAsync(row));

        // The commands. Making one runs nothing. Copy doesn't ask about the form's changes: the copy holds them.
        CopyCommand = ScreenCommand.Of(CopyForm, () => SelectedRow is not null);
        NewProductCommand = ScreenCommand.Of(AskForNewProduct, () => SelectedRow is { } row && row.SubAssembly.Role?.IsPackaging != true);
        DeleteCommand = new ScreenCommand(() => _view.DeleteAsync(SelectedRow!, this), () => SelectedRow is not null);
        SaveCommand = new ScreenCommand(() => RunSaveAsync(SaveAsync), () => History.IsDirty);
        PrintCommand = new ScreenCommand(() => RunSafelyAsync(async () => await ShowPrint.Handle(PrintoutNow(DateTime.Now))));

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

    /// <summary>The view this form is a tab of: its list, where the form's sub-assembly is picked, and the load.</summary>
    public SubAssembliesState View => _view;

    /// <summary>A recipe's lines in grams, their formula base and totals, and Scale.</summary>
    public SubAssemblyBatch Batch { get; }

    /// <summary>A packaging set's lines and what the set costs, the packaging table's.</summary>
    public SubAssemblyPackagingSet PackagingSet { get; }

    /// <summary>The Dependents tab: the products and made ingredients the sub-assembly goes into.</summary>
    public SubAssemblyDependents Dependents { get; }

    /// <summary>The roles the Role picker offers, the view's.</summary>
    public ObservableCollection<SubAssemblyRole> Roles => _view.Roles;

    /// <summary>The ingredients only: packaging never goes in a recipe. The view's.</summary>
    public ObservableCollection<RawMaterial> Ingredients => _view.Ingredients;

    /// <summary>The packaging materials, for a packaging set's lines. The view's.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials => _view.PackagingMaterials;

    /// <summary>
    /// The form is a packaging set: its role is the Packaging role, so it takes packaging materials per set instead of
    /// ingredients in grams, and the screen shows the packaging table in place of the recipe's.
    /// </summary>
    public bool IsPackaging => EditingRole?.IsPackaging == true;

    private int _editingId;
    /// <summary>The sub-assembly the form holds: 0 for a new one, or a copy, not saved yet.</summary>
    public int EditingId
    {
        get => _editingId;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingId, value))
            {
                RaisePropertyChanged(nameof(RecordId));
            }
        }
    }

    /// <summary>
    /// ITabForm: the sub-assembly the tab is for, as its tab and its address know it: the one picked, from the pick on, not
    /// only once its details are read (<see cref="EditingId"/>), or a link opening another in the preview tab gave the tab
    /// the one before's address meanwhile.
    /// </summary>
    public int RecordId => SelectedRow?.Id ?? EditingId;

    /// <summary>ITabForm: the name on the tab, the header's.</summary>
    public string TabTitle => HeaderTitle;

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
                RaisePropertyChanged(nameof(RecordId));
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
    /// What follows the sub-assembly showing, a pick's or a load's fresh copy: the view's list follows it while this form
    /// shows, and one opened, by its Save included, ends the new one's row.
    /// </summary>
    private void SelectionChanged() => _view.FormShowsAnother(this, SelectedRow is not null);

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
        private set
        {
            if (RaiseAndSetIfChanged(ref _headerTitle, value))
            {
                RaisePropertyChanged(nameof(TabTitle));
            }
        }
    }

    private int _selectedDetailsTab = RecipeTab;
    /// <summary>
    /// The details tab showing, kept here so the form comes back on the tab it was left on, as Products and Raw Materials
    /// do. Both tabs always show, so nothing moves it but the user.
    /// </summary>
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    /// <summary>The view's load: its lists read again, and every open form kept as it is (SubAssembliesState.LoadCommand).</summary>
    public ScreenCommand LoadCommand => _view.LoadCommand;

    /// <summary>The view's New: a new sub-assembly, in the preview tab or a tab of its own (SubAssembliesState.NewCommand).</summary>
    public ScreenCommand NewCommand => _view.NewCommand;

    /// <summary>Starts a new sub-assembly from the one the form shows (<see cref="CopyForm"/>), saved by Save.</summary>
    public ScreenCommand CopyCommand { get; }

    public ScreenCommand SaveCommand { get; }

    public ScreenCommand DeleteCommand { get; }

    /// <summary>Remove Line on whichever table shows: the recipe's lines, or a packaging set's.</summary>
    public ScreenCommand RemoveLineCommand { get; }

    /// <summary>Print: the form as it shows, unsaved edits included, in the Print dialog (<see cref="ShowPrint"/>).</summary>
    public ScreenCommand PrintCommand { get; }

    /// <summary>Input: the pages, which the screen shows in the Print dialog (PrintPreviews).</summary>
    public Interaction<TablePrintout, System.Reactive.Unit> ShowPrint { get; } = new();

    /// <summary>
    /// The sub-assembly as the form shows it, on its own: a recipe's lines with their grams, share of the formula, cost per
    /// gram and cost, and the batch's total; or a packaging set's materials, each set's quantity and cost. Its edits not
    /// saved yet are in it, which it says (a draft).
    /// </summary>
    public TablePrintout PrintoutNow(DateTime printedAt)
    {
        var title = string.IsNullOrWhiteSpace(EditingName) ? HeaderTitle : EditingName.Trim();
        var role = EditingRole?.Name ?? "No role";
        PrintedTable table;
        List<string> lines;
        if (IsPackaging)
        {
            var priced = PackagingSet.Lines.Where(l => l.PackagingMaterial is not null).Select(l => (l.PackagingMaterial!.Name, Priced: l.ToPriced())).ToList();
            lines = [$"{role} · Sub-Assemblies · a packaging set: what one set takes of each material"];
            table = new PrintedTable(
                null,
                [new("Material"), new("Qty", true), new("Unit"), new("Unit Cost", true), new("Cost per Set", true)],
                [.. priced.Select(p => (IReadOnlyList<string>)[
                    p.Name, p.Priced.QtyPerUnit?.ToString("#,0.####", CultureInfo.CurrentCulture) ?? "", p.Priced.UnitAbbreviation ?? "",
                    p.Priced.UnitCost.ToString("N4", CultureInfo.CurrentCulture), p.Priced.CostPerUnit.ToString("N2", CultureInfo.CurrentCulture)])])
            {
                Total = ["Total", "", "", "", PackagingSet.Cost.ToString("N2", CultureInfo.CurrentCulture)],
            };
        }
        else
        {
            lines = [$"{role} · Sub-Assemblies", Batch.FormulaBaseCaption];
            table = new PrintedTable(
                null,
                [new("Ingredient"), new("Grams", true), new(Batch.PercentHeader, true), new("Cost/g", true), new("Cost", true)],
                [.. Batch.Lines.Where(l => l.Ingredient is not null).Select(l => (IReadOnlyList<string>)[
                    l.Ingredient!.Name, l.Grams?.ToString("N1", CultureInfo.CurrentCulture) ?? "", l.Percent?.ToString("N1", CultureInfo.CurrentCulture) ?? "—",
                    l.CostPerGram.ToString("N4", CultureInfo.CurrentCulture), l.Cost.ToString("N2", CultureInfo.CurrentCulture)])])
            {
                Total = ["Total", Batch.BatchWeight.ToString("N1", CultureInfo.CurrentCulture), "", "", Batch.BatchCost.ToString("N2", CultureInfo.CurrentCulture)],
                Note = $"Cost per gram of the batch {Batch.CostPerGram.ToString("N4", CultureInfo.CurrentCulture)}",
            };
        }

        return new TablePrintout(title, lines, [table], printedAt)
        {
            SaveNote = EditingId == 0 ? ProductPrintout.NotSavedNote : HasUnsavedChanges ? ProductPrintout.UnsavedChangesNote : null,
        };
    }

    /// <summary>
    /// Starts a new product that links the picked sub-assembly, through <see cref="NewProductRequests"/>. Not for a packaging
    /// set, which isn't a recipe, and not while the form has changes not saved: the product links it as it's saved.
    /// </summary>
    public ScreenCommand NewProductCommand { get; }

    /// <summary>
    /// The sub-assemblies New Product asks a new product to link, by id. The view passes them on, and ScreenLinks starts the
    /// new product on the Products screen, as it opens a Dependents tile's product.
    /// </summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

    /// <summary>
    /// What the form was showing as the view's load set out, taken before anything it reads is replaced (a picker whose item
    /// gets replaced can clear its own selection mid-merge), and what the load found.
    /// </summary>
    internal sealed class Load
    {
        public int? SelectedId { get; init; }

        public int? RoleId { get; init; }

        /// <summary>A brand-new form has no role until the roles arrive, and the merge gives it the first one.</summary>
        public bool HadNoRole { get; init; }

        /// <summary>The sub-assembly showing was saved since the form took it, with nothing typed: it's read into the form again.</summary>
        public bool SavedSince { get; set; }
    }

    /// <summary>The view's load sets out: what the form shows now, by id.</summary>
    internal Load BeginLoad() => new()
    {
        SelectedId = SelectedRow?.Id,
        RoleId = EditingRole?.Id,
        HadNoRole = EditingId == 0 && EditingRole?.Id is null,
    };

    /// <summary>
    /// The view's load merges into the form, its lists merged already: the Role picker, the lines and the selection pointed
    /// at their fresh copies, the form and what's typed left as they are. Run within the form's own refresh, so nothing it
    /// writes is the user's.
    /// </summary>
    internal void TakeLoad(Load load, IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> linkedBy, IReadOnlyDictionary<int, IReadOnlyList<MadeIngredientUse>> madeFrom)
    {
        // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
        EditingRole = Roles.FirstOrDefault(r => r.Id == load.RoleId) ?? (EditingId == 0 && load.RoleId is null ? Roles.FirstOrDefault() : null);

        Batch.ResolveLines();
        PackagingSet.ResolveRows();

        SelectedRow = _view.List.Rows.FirstOrDefault(r => r.Id == load.SelectedId);

        // The sub-assembly showing, saved since the form took it (SavedVersion): read into the form again when nothing's
        // typed, so what it shows, and what its next Save starts from, is what's saved now (FollowSavedVersion). Every load,
        // so a list's own save is followed by the load it asked for and no later one.
        load.SavedSince = FollowSavedVersion(SelectedRow is { } showing && showing.Id == EditingId ? showing.SubAssembly.UpdatedAt : SavedVersion);
        if (EditingId > 0)
        {
            Dependents.Show(linkedBy.GetValueOrDefault(EditingId) ?? [], madeFrom.GetValueOrDefault(EditingId) ?? [], IsPackaging);
        }
    }

    /// <summary>The view's load is merged: what follows it on the form.</summary>
    internal async Task EndLoadAsync(Load load)
    {
        // Only the role, and only when the form had none: a role the user picked must keep counting as a change, so this
        // can't be a blanket MarkClean.
        if (load.HadNoRole && EditingRole is not null)
        {
            History.Rebase((settled, now) => settled with { RoleId = now.RoleId });
        }

        if (load.SavedSince && SelectedRow is { } savedAgain)
        {
            await LoadIntoFormAsync(savedAgain);
        }

        // After the merge, which only points the selection at its fresh copy: a record asked for before the list held it
        // has to load its form.
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

        // The lines as they're typed, read here on the UI thread, for the save below. With when it had last been saved as the
        // form took it: a save since, from another PC, refuses this one (SavedVersionRules).
        var subAssembly = new SubAssembly { Id = EditingId, Name = name, RoleId = EditingRole!.Id, UpdatedAt = SavedVersionToSend };
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
        var row = _view.List.Rows.FirstOrDefault(r => r.Id == saved.Id);
        SelectedRow = row;
        await LoadIntoFormAsync(row);
        SuccessMessage = ProductVersionRecorder.RecordedMessage(saved.ProductsVersioned) is { } recorded
            ? $"{name} was saved. {recorded}"
            : $"{name} was saved.";
    }

    /// <summary>
    /// New, from the header or the list, in this form. A sub-assembly still waiting for the load to open it is no longer
    /// opened: it would load over the new form, and a name typed into it since would then be saved as that sub-assembly's.
    /// </summary>
    internal void StartNew(SubAssemblyRole? role)
    {
        ForgetRecordToOpen();
        ClearForm(role);
    }

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, in <paramref name="role"/> or the first role.</summary>
    private void ClearForm(SubAssemblyRole? role = null)
    {
        // The new sub-assembly's row, if one was named in the list, goes with the form it stood for.
        if (_view.Form == this)
        {
            _view.List.EndNewRow();
        }

        History.Clear();
        _formLoadVersion++;
        SelectedRow = null;
        FormVersion++;
        EditingId = 0;
        SavedVersion = null;
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
        var name = SubAssemblyRules.UniqueStandaloneName($"{source.Name.Trim()} Copy", [.. _view.List.Rows.Select(r => r.Name)]);

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
        if (row.Id != EditingId)
        {
            FormVersion++;
        }

        EditingId = row.Id;
        SavedVersion = row.SubAssembly.UpdatedAt;
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

    protected override int? FindRecordByName(string text) => RecordNameMatch.Best(_view.List.Rows, row => row.Name, text)?.Id;

    protected override bool ReplacesShowing(int id) => id != SelectedRow?.Id;

    protected override bool TrySelectRecord(int id)
    {
        var list = _view.List;
        if (list.Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return false;
        }

        if (!list.VisibleRows.Contains(row))
        {
            list.SearchText = string.Empty;
        }

        Pick(row);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: the view opens it, in its tab or the preview tab.</summary>
    bool IRecordScreen.OpenByName(string text) => _view.OpenByName(text);

    /// <summary>The name typed into the new sub-assembly's row in the list goes into the form, as typed there: one undo step.</summary>
    internal void NameNew(string name) => AsUndoStep(() => EditingName = name);

    /// <summary>The new sub-assembly's row went with Esc: the name the form was given goes too, as no edit of the user's.</summary>
    internal void ClearNewName() => EditingName = string.Empty;

    /// <summary>The sub-assembly the form shows was renamed in the list, saved: the form and its undo steps take the name as given.</summary>
    internal void TakeRename(string name)
    {
        HeaderTitle = name;
        EditingName = name;
        History.RewriteAll(snapshot => snapshot with { Name = name });
    }

    /// <summary>The sub-assembly the form shows was moved to another role, saved: the form and its undo steps take the role as given.</summary>
    internal void TakeMove(int roleId)
    {
        EditingRole = Roles.FirstOrDefault(r => r.Id == roleId);
        History.RewriteAll(snapshot => snapshot with { RoleId = roleId });
    }

    /// <summary>A role was deleted: the form's undo steps forget it, so none puts back a role there's no longer any of.</summary>
    internal void ForgetRole(int roleId) =>
        History.RewriteAll(snapshot => snapshot.RoleId == roleId ? snapshot with { RoleId = null } : snapshot);

    protected override SubAssemblyFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = EditingId,
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

            EditingId = snapshot.EditingId;
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

    protected override Task ReadListsAgainAsync() => LoadCommand.ExecuteAsync();

    protected override int ShowingRecordId => EditingId;

    /// <summary>
    /// Don't Save: the form goes back to the sub-assembly as it was last saved, its lines read again. A new one (or a copy)
    /// was never saved, so it goes back to a blank one.
    /// </summary>
    protected override Task RevertToSavedAsync() => LoadIntoFormAsync(SelectedRow);

    public void Dispose()
    {
        Dependents.Dispose();
        _picks.Dispose();
        _newProductRequests.Dispose();
    }
}
