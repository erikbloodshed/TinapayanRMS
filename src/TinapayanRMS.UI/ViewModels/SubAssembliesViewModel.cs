using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A standalone sub-assembly in the Sub-Assemblies list, with the products that use it. "Used by" is kept
/// as text: CollectionMerge compares a row's scalar properties, so a changed list of products shows up
/// as a changed row.
/// </summary>
public sealed class StandaloneSubAssemblyRow(SubAssembly subAssembly, IReadOnlyList<LinkingProduct> usedBy)
{
    public SubAssembly SubAssembly { get; } = subAssembly;

    public int Id => SubAssembly.Id;

    public string Name => SubAssembly.Name ?? string.Empty;

    public string RoleName => SubAssembly.Role?.Name ?? string.Empty;

    public decimal TotalWeight => SubAssembly.TotalWeight;

    public decimal TotalCost => SubAssembly.TotalCost;

    /// <summary>The products linking it, e.g. "Cream Loaf, Cream Loaf Mini", or empty when none do.</summary>
    public string UsedByText { get; } = string.Join(", ", usedBy.Select(p => p.Name));

    /// <summary>The products linking it, by name. A method, not a property: CollectionMerge would compare the list by its scalar properties only.</summary>
    public IReadOnlyList<LinkingProduct> GetUsedBy() => usedBy;
}

/// <summary>
/// The Sub-Assemblies screen: standalone sub-assemblies, which no product owns. Products link them (and
/// follow every change saved here) or copy them, from their recipe on the Products screen. One Save writes
/// the name, the role and the lines together.
/// </summary>
public sealed class SubAssembliesViewModel : ViewModelBase, IUndoScreen, IDisposable
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly SourceCache<StandaloneSubAssemblyRow, int> _rowCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _rows;
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _visibleRows;

    /// <summary>The sub-assembly the form shows: 0 for a new one.</summary>
    private int _editingId;

    /// <summary>A sub-assembly <see cref="OpenSubAssembly"/> was asked for before the list held it, for LoadCommand to open once it does.</summary>
    private int? _subAssemblyToOpen;

    /// <summary>Counts form loads, so a load overtaken by a newer one drops its lines instead of adding them too.</summary>
    private int _formLoadVersion;

    /// <summary>Every standalone sub-assembly, whatever the list is filtered to.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> Rows => _rows;

    /// <summary>The sub-assemblies the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> VisibleRows => _visibleRows;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The form's lines, typed straight into the table, in grams.</summary>
    public ObservableCollection<IngredientLineRow> Lines { get; } = [];

    /// <summary>The line the table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewLine => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id), _formulaBase);

    /// <summary>The formula base as last worked out, for a line started in the add row.</summary>
    private decimal _formulaBase;

    /// <summary>The packaging materials, for a packaging set's lines.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>A packaging set's lines, typed straight into the table: how much of each material one set takes.</summary>
    public ObservableCollection<PackagingLineRow> PackagingLines { get; } = [];

    /// <summary>The line the packaging table's add row edits: it prices itself as its material is picked.</summary>
    public Func<PackagingLineRow> NewPackagingLine => () => new PackagingLineRow(id => PackagingMaterials.FirstOrDefault(m => m.Id == id));

    private PackagingLineRow? _selectedPackagingLine;
    public PackagingLineRow? SelectedPackagingLine
    {
        get => _selectedPackagingLine;
        set => this.RaiseAndSetIfChanged(ref _selectedPackagingLine, value);
    }

    private decimal _setCost;
    /// <summary>What one packaging set costs, at its materials' buffered costs: a linking product's packaging per piece.</summary>
    public decimal SetCost
    {
        get => _setCost;
        private set => this.RaiseAndSetIfChanged(ref _setCost, value);
    }

    /// <summary>
    /// The form is a packaging set: its role is the Packaging role, so it takes packaging materials per set instead of
    /// ingredients in grams, and the screen shows the packaging table in place of the recipe's.
    /// </summary>
    public bool IsPackaging => EditingRole?.IsPackaging == true;

    private StandaloneSubAssemblyRow? _selectedRow;
    public StandaloneSubAssemblyRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    private IngredientLineRow? _selectedLine;
    public IngredientLineRow? SelectedLine
    {
        get => _selectedLine;
        set => this.RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private SubAssemblyRole? _editingRole;
    public SubAssemblyRole? EditingRole
    {
        get => _editingRole;
        set
        {
            this.RaiseAndSetIfChanged(ref _editingRole, value);
            this.RaisePropertyChanged(nameof(IsPackaging));
        }
    }

    private string _headerTitle = "New sub-assembly";
    /// <summary>The saved name of the sub-assembly the form shows, "New sub-assembly", or "Copy of …" for a copy not yet saved.</summary>
    public string HeaderTitle
    {
        get => _headerTitle;
        private set => this.RaiseAndSetIfChanged(ref _headerTitle, value);
    }

    private string _formulaBaseCaption = NoBaseCaption;
    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string FormulaBaseCaption
    {
        get => _formulaBaseCaption;
        private set => this.RaiseAndSetIfChanged(ref _formulaBaseCaption, value);
    }

    /// <summary>With nothing ticked as the base, the percentages are absolute: each line's share of the batch.</summary>
    internal const string NoBaseCaption = "Absolute %: each line's share of the batch. Tick the base — usually the flour — for baker's %.";

    private string _percentHeader = IngredientLineRow.PercentHeader(0m);
    /// <summary>The lines table's percentage header: "Baker's %" while a base is ticked, "Absolute %" while none is.</summary>
    public string PercentHeader
    {
        get => _percentHeader;
        private set => this.RaiseAndSetIfChanged(ref _percentHeader, value);
    }

    private decimal _batchWeight;
    /// <summary>The form's batch: the sum of its lines' grams.</summary>
    public decimal BatchWeight
    {
        get => _batchWeight;
        private set => this.RaiseAndSetIfChanged(ref _batchWeight, value);
    }

    private decimal _batchCost;
    /// <summary>What the form's batch costs, at its ingredients' buffered costs.</summary>
    public decimal BatchCost
    {
        get => _batchCost;
        private set => this.RaiseAndSetIfChanged(ref _batchCost, value);
    }

    private decimal _costPerGram;
    public decimal CostPerGram
    {
        get => _costPerGram;
        private set => this.RaiseAndSetIfChanged(ref _costPerGram, value);
    }

    private IReadOnlyList<LinkingProduct> _usedBy = [];
    /// <summary>The products linking the sub-assembly the form shows, by name: one tile each under Used By, which opens it.</summary>
    public IReadOnlyList<LinkingProduct> UsedBy
    {
        get => _usedBy;
        private set => this.RaiseAndSetIfChanged(ref _usedBy, value);
    }

    private string _usedByText = NotUsedText;
    /// <summary>The caption under the Used By tiles: what linking means for editing it, or how to link it when nothing does.</summary>
    public string UsedByText
    {
        get => _usedByText;
        private set => this.RaiseAndSetIfChanged(ref _usedByText, value);
    }

    private const string NotUsedText = "No product uses it yet. Link or copy it from a product's recipe on the Products screen.";

    private const string SetNotUsedText = "No product uses it yet. Link it from a product's Recipe Structure: Add Sub-Assembly, Link an existing one.";

    private const string UsedText = "These products follow every change saved here, and it can't be deleted while they use it.";

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }

    /// <summary>Starts a new sub-assembly from the one the form shows (<see cref="CopyForm"/>), saved by Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    /// <summary>A Used By tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products Used By tiles ask to open, by id. The main window, which owns the tabs, subscribes and opens each
    /// on the Products screen. Raised as the tile is clicked, not on the main-thread scheduler a command's own results
    /// wait for.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>
    /// The form's undo and redo stacks, the same shape as Products'. One step is one finished change — a cell
    /// committed, a line added or removed, a field typed and left. Cleared whenever the form stops being the one
    /// it recorded: another sub-assembly picked, a new one started, or a Save.
    /// </summary>
    public UndoHistory<SubAssemblyFormSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }

    public SubAssembliesViewModel(
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository lineRepository,
        ISubAssemblyRoleRepository roleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        ILogger logger)
        : base(logger)
    {
        _subAssemblyRepository = subAssemblyRepository;
        _lineRepository = lineRepository;
        _subAssemblyValidator = subAssemblyValidator;

        // Before any subscription below: WhenAnyValue fires straight away with the value it finds, and the one on
        // SelectedRow reaches ClearForm, which clears the history.
        History = new UndoHistory<SubAssemblyFormSnapshot>(
            CaptureSnapshot, (before, after) => before.ShowsSameAs(after), () => !IsBusyApplying);

        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, History.WhenAnyValue(h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, History.WhenAnyValue(h => h.CanRedo));

        // Save offers itself only when the form holds something the saved sub-assembly doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(Lines);
        WatchForSave(PackagingLines);

        var byName = SortExpressionComparer<StandaloneSubAssemblyRow>.Ascending(r => r.Name);

        _rowCache.Connect()
            .SortAndBind(out _rows, byName)
            .Subscribe();

        // The list's own filter, as Raw Materials and Products filter theirs: by name, ignoring case.
        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<StandaloneSubAssemblyRow, bool>)(r => text.Length == 0 || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _rowCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRows, byName)
            .Subscribe();

        // Picking a sub-assembly loads it into the form; a refresh only re-points the selection at its
        // refreshed copy, so it leaves the form and its unsaved edits alone.
        this.WhenAnyValue(x => x.SelectedRow)
            .Where(_ => !IsBusyApplying)
            .Select(row => Observable.FromAsync(() => LoadIntoFormAsync(row)))
            .Switch()
            .Subscribe();

        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
            });

        // Cost follows both an ingredient picked and grams typed.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                RecomputeTotals();
            });

        // A packaging set's lines price themselves as their material is picked, and the set's cost follows.
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
            });
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                SetCost = PackagingLines.Sum(l => l.CostPerUnit);
            });

        // The formula's base: what's ticked, and how much of it there is.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IsFormulaBase)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                RecomputeFormulaBase();
            });

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection and unsaved lines while picking up
        // what changed elsewhere: a repriced ingredient, a renamed role, a product that links one.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await roleRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var subAssemblies = await _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None);
            var linkedBy = await _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None);

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
                _rowCache.MergeFrom(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [])));

                // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
                EditingRole = Roles.FirstOrDefault(r => r.Id == roleId) ?? (_editingId == 0 && roleId is null ? Roles.FirstOrDefault() : null);

                IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
                PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
                RecomputeTotals();
                SetCost = PackagingLines.Sum(l => l.CostPerUnit);

                SelectedRow = Rows.FirstOrDefault(r => r.Id == selectedId);
                if (_editingId > 0)
                {
                    ShowUsedBy(linkedBy.GetValueOrDefault(_editingId) ?? []);
                }
            });

            // Only the role, and only when the form had none: a role the user picked must keep counting as a
            // change, so this can't be a blanket MarkClean.
            if (formHasNoRole && EditingRole is not null)
            {
                History.Rebase((settled, now) => settled with { RoleId = now.RoleId });
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the sub-assembly's form.
            if (_subAssemblyToOpen is { } subAssemblyId)
            {
                _subAssemblyToOpen = null;
                TrySelectSubAssembly(subAssemblyId);
            }
        });

        NewCommand = ReactiveCommand.Create(ClearForm);

        CopyCommand = ReactiveCommand.Create(CopyForm, this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var name = EditingName.Trim();
            var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingRole?.Id ?? 0 });
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            int id;
            if (IsPackaging)
            {
                var setLines = PackagingLineRow.CompleteLines(PackagingLines, "set");
                if (setLines.Count == 0)
                {
                    ErrorMessage = $"Add the packaging materials that go into {name}, per set.";
                    return;
                }

                id = await _subAssemblyRepository.SaveStandalonePackagingAsync(
                    new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id },
                    [.. setLines.Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit))],
                    CancellationToken.None);
            }
            else
            {
                var lines = IngredientLineRow.CompleteLines(Lines, name);
                if (lines.Count == 0)
                {
                    ErrorMessage = $"Add the ingredients that go into {name}, in grams.";
                    return;
                }

                id = await _subAssemblyRepository.SaveStandaloneAsync(
                    new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id },
                    [.. lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    CancellationToken.None);
            }

            // The list picks up the new totals; then the form reloads what was saved.
            await LoadCommand.Execute();
            var saved = Rows.FirstOrDefault(r => r.Id == id);
            ApplyRefresh(() => SelectedRow = saved);
            await LoadIntoFormAsync(saved);
            SuccessMessage = $"{name} was saved.";
        }),
        History.WhenAnyValue(h => h.IsDirty));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedRow!.Name}'? This can't be undone.", async () =>
            {
                await _subAssemblyRepository.DeleteStandaloneAsync(SelectedRow!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        // The line selected in whichever table is showing.
        RemoveLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                if (IsPackaging)
                {
                    PackagingLines.Remove(SelectedPackagingLine!);
                }
                else
                {
                    Lines.Remove(SelectedLine!);
                }
            }),
            this.WhenAnyValue(
                x => x.SelectedLine,
                x => x.SelectedPackagingLine,
                x => x.IsPackaging,
                (line, packagingLine, isPackaging) => isPackaging ? packagingLine is not null : line is not null));
    }

    /// <summary>
    /// Follows <paramref name="rows"/> for Save's sake: a line added, removed or replaced can be something to
    /// save. Watched through the collection's own PropertyChanged rather than its CollectionChanged, because an
    /// ObservableCollection only enforces its re-entrancy guard once more than one handler is listening, so a
    /// listener added for this could turn a re-entrant change the guard tolerates today into an exception. What's
    /// typed into a line reaches Save another way: the grid closes the cell's undo step, which re-reads whether
    /// there's anything to save.
    /// </summary>
    private void WatchForSave(INotifyPropertyChanged rows) => rows.PropertyChanged += (_, _) => History.RefreshDirty();

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    bool IUndoRecorder.IsEditPending
    {
        get => History.IsEditPending;
        set => History.IsEditPending = value;
    }

    /// <summary>Records <paramref name="change"/> as one undo step, for a command that makes its change in one go.</summary>
    private void AsUndoStep(Action change)
    {
        History.Begin();
        try
        {
            change();
        }
        finally
        {
            History.End();
        }
    }

    /// <summary>Settles whatever step is open, then puts the form back one step.</summary>
    public bool TryUndo()
    {
        History.End();
        if (!History.TryUndo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    /// <summary>Steps forward again into what the last undo stepped off.</summary>
    public bool TryRedo()
    {
        History.End();
        if (!History.TryRedo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    private SubAssemblyFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        RoleId = EditingRole?.Id,
        Lines = [.. Lines.Select(l => new IngredientLineState(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
        PackagingLines = [.. PackagingLines.Select(l => new PackagingLineState(l.PackagingMaterialId, l.UnitId, l.QtyPerUnit))],
        FormulaBase = _formulaBase,
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The lines are rebuilt rather than written into —
    /// IngredientLineRow's setters drop a typed percentage — and the one recompute runs at the end, outside the
    /// guard, so the totals and the formula base are worked out once from the form that was put back.
    /// </summary>
    private void RestoreSnapshot(SubAssemblyFormSnapshot snapshot)
    {
        ErrorMessage = null;

        // A load still on its way would otherwise land on top of what's being put back.
        _formLoadVersion++;

        ApplyRestore(() =>
        {
            SelectedLine = null;
            SelectedPackagingLine = null;

            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingRole = snapshot.RoleId is { } roleId ? Roles.FirstOrDefault(r => r.Id == roleId) : null;

            _formulaBase = snapshot.FormulaBase;
            Lines.Clear();
            foreach (var line in snapshot.Lines)
            {
                Lines.Add(IngredientLineRow.Restore(
                    id => Ingredients.FirstOrDefault(m => m.Id == id),
                    snapshot.FormulaBase,
                    line.IngredientId,
                    line.Grams,
                    line.IsFormulaBase,
                    line.AuthoredBakersPercent));
            }

            PackagingLines.Clear();
            foreach (var line in snapshot.PackagingLines)
            {
                // The material goes on first: it brings the units the row can be in, so the unit sticks.
                PackagingLines.Add(new PackagingLineRow(id => PackagingMaterials.FirstOrDefault(m => m.Id == id))
                {
                    PackagingMaterialId = line.PackagingMaterialId,
                    UnitKey = line.UnitId ?? PackagingUnitChoice.BaseUnitKey,
                    QtyPerUnit = line.QtyPerUnit,
                });
            }
        });

        IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
        PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
        RecomputeFormulaBase();
        RecomputeTotals();
        SetCost = PackagingLines.Sum(l => l.CostPerUnit);
    }

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, the first role.</summary>
    private void ClearForm()
    {
        History.Clear();
        _formLoadVersion++;
        ApplyRefresh(() => SelectedRow = null);
        _editingId = 0;
        HeaderTitle = "New sub-assembly";
        EditingName = string.Empty;
        EditingRole = Roles.FirstOrDefault();
        SelectedLine = null;
        Lines.Clear();
        SelectedPackagingLine = null;
        PackagingLines.Clear();
        UsedBy = [];
        UsedByText = NotUsedText;

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
        var name = SubAssemblyRules.UniqueStandaloneName($"{source.Name.Trim()} Copy", [.. Rows.Select(r => r.Name)]);

        ClearForm();
        RestoreSnapshot(source with { EditingId = 0, Name = name });
        HeaderTitle = $"Copy of {sourceName}";
        ShowUsedBy([]);
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
        IReadOnlyList<SubAssemblyLine> saved = isSet ? [] : await _lineRepository.GetForSubAssembliesAsync([row.Id], CancellationToken.None);
        IReadOnlyList<SubAssemblyPackagingLine> savedSet = isSet ? await _subAssemblyRepository.GetPackagingLinesAsync(row.Id, CancellationToken.None) : [];
        if (version != _formLoadVersion)
        {
            return;
        }

        History.Clear();
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var packagingById = PackagingMaterials.ToDictionary(m => m.Id);
        _editingId = row.Id;
        HeaderTitle = row.Name;
        EditingName = row.Name;
        EditingRole = Roles.FirstOrDefault(r => r.Id == row.SubAssembly.RoleId);
        SelectedLine = null;
        Lines.Clear();
        Lines.AddRange(saved.Select(l => new IngredientLineRow(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
        SelectedPackagingLine = null;
        PackagingLines.Clear();
        PackagingLines.AddRange(savedSet.Select(l => new PackagingLineRow(packagingById.GetValueOrDefault(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId)));
        ShowUsedBy(row.GetUsedBy());

        // The form now holds what was saved, so this is what Save's dirty check measures against. Taken at the
        // end: the lines land after the name and role, and a form measured before them would read as changed
        // the moment they arrived.
        History.MarkClean();
    }

    private void ShowUsedBy(IReadOnlyList<LinkingProduct> products)
    {
        UsedBy = products;
        UsedByText = products.Count == 0
            ? IsPackaging ? SetNotUsedText : NotUsedText
            : UsedText;
    }

    /// <summary>The formula's base, given to every row so each reads its own percentage, and said in the caption.</summary>
    private void RecomputeFormulaBase()
    {
        var totalWeight = IngredientLineRow.BatchWeightOf(Lines);
        var baseWeight = IngredientLineRow.ApplyFormulaBase([.. Lines], totalWeight);
        _formulaBase = baseWeight;
        PercentHeader = IngredientLineRow.PercentHeader(baseWeight);
        FormulaBaseCaption = baseWeight > 0
            ? $"Base {baseWeight:#,0.##} g · whole formula {totalWeight / baseWeight * 100m:#,0.#}%"
            : NoBaseCaption;
    }

    private void RecomputeTotals()
    {
        BatchWeight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams ?? 0m);
        BatchCost = Lines.Sum(l => l.Cost);
        CostPerGram = BatchWeight > 0 ? BatchCost / BatchWeight : 0m;
    }

    /// <summary>
    /// Opens a sub-assembly as picking it in the list would, for another screen (a linked badge on Products). A search
    /// hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not loaded
    /// since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenSubAssembly(int subAssemblyId)
    {
        _subAssemblyToOpen = TrySelectSubAssembly(subAssemblyId) ? null : subAssemblyId;
    }

    private bool TrySelectSubAssembly(int subAssemblyId)
    {
        if (Rows.FirstOrDefault(r => r.Id == subAssemblyId) is not { } row)
        {
            return false;
        }

        if (!VisibleRows.Contains(row))
        {
            SearchText = string.Empty;
        }

        SelectedRow = row;
        return true;
    }

    public void Dispose()
    {
        _rowCache.Dispose();
        _productOpenRequests.Dispose();
    }
}
