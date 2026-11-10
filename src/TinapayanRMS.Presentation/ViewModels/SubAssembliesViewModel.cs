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
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Sub-Assemblies screen: standalone sub-assemblies, which no product owns. Products link them (and
/// follow every change saved here) or copy them, from their recipe on the Products screen. One Save writes
/// the name, the role and the lines together.
/// </summary>
public sealed class SubAssembliesViewModel : ViewModelBase, IUndoScreen, IDisposable, IRecordScreen, IUnsavedChangesScreen, IListTreeScreen
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IRawMaterialCategoryRepository _rawMaterialCategoryRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly IValidator<SubAssemblyRole> _roleValidator;
    private readonly VersionedSaves _versionedSaves;
    private readonly SourceCache<StandaloneSubAssemblyRow, int> _rowCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _rows;
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _visibleRows;

    /// <summary>The sub-assembly the form shows: 0 for a new one.</summary>
    private int _editingId;

    /// <summary>
    /// A sub-assembly for LoadCommand to open at its end: one <see cref="OpenSubAssembly"/> was asked for before the list
    /// held it, or the one in the screen's address (<see cref="OpenSubAssemblyOnLoad"/>). New drops it.
    /// </summary>
    private int? _subAssemblyToOpen;

    /// <summary>Counts form loads, so a load overtaken by a newer one drops its lines instead of adding them too.</summary>
    private int _formLoadVersion;

    /// <summary>Every standalone sub-assembly, whatever the list is filtered to.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> Rows => _rows;

    /// <summary>The sub-assemblies the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> VisibleRows => _visibleRows;

    /// <summary>The Sub-Assemblies list (SubAssemblyListCard): VisibleRows filed in a folder for their role.</summary>
    public SubAssemblyTree SubAssemblyTree { get; }

    /// <summary>The view state key the closed folders of the Sub-Assemblies list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "SubAssemblies.ClosedFolders";

    private readonly FolderStateWriter _closedFoldersWriter;

    private readonly TreeSelection _treeSelection = new();

    /// <summary>
    /// The rows picked in the Sub-Assemblies list, bound to the tree's SelectedItems, which it shares: one row, or several
    /// with Ctrl or Shift held. Its first is <see cref="SelectedTreeNode"/>. The tree changes it as rows are clicked, and
    /// this screen as SelectedRow changes (SyncTreeSelection), as the Raw Materials screen does its own.
    /// </summary>
    public ObservableCollection<object> SelectedTreeNodes => _treeSelection.Nodes;

    private object? _selectedTreeNode;
    /// <summary>
    /// The first row picked in the list: a sub-assembly's (SubAssemblyLeafNode), a folder's, or null. Picking a
    /// sub-assembly's row picks it (SelectedRow); picking a folder's leaves the form as it is, and so do more rows picked
    /// with Ctrl. SelectedRow changed any other way (a Save, New, a linked badge on Products) moves the row to it. Setting
    /// it picks that row alone, as a click does.
    /// </summary>
    public object? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set => _treeSelection.Pick(value);
    }

    /// <summary>The sub-assemblies picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<SubAssemblyLeafNode> SelectedTreeSubAssemblies => [.. SelectedTreeNodes.OfType<SubAssemblyLeafNode>().Where(l => !l.IsNew)];

    private readonly TreeNaming _treeNaming;

    /// <summary>
    /// The row of the list whose name is being typed in place, as in a file explorer, or null: a sub-assembly or role being
    /// renamed (Rename, F2), or a new sub-assembly or folder being named (New, New Folder). The list draws a box on it,
    /// which gives the name typed to <see cref="CommitTreeNameAsync"/>, or Esc to <see cref="CancelTreeName"/>.
    /// </summary>
    public TreeNameEdit? TreeNameEdit => _treeNaming.Edit;

    private string? _moveNotice;
    /// <summary>
    /// What the last move in the list did, "Moved Ube Halaya to Filling", on the list's notice bar with Undo beside it
    /// (UndoMoveCommand); null once it's undone or dismissed. The bar hides itself after a few seconds.
    /// </summary>
    public string? MoveNotice
    {
        get => _moveNotice;
        private set => this.RaiseAndSetIfChanged(ref _moveNotice, value);
    }

    /// <summary>The roles the sub-assemblies the last move took were in before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<SubAssemblyMove> _moveUndo = [];

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

    /// <summary>
    /// Each sub-assembly picked, by the user or for them (Open, a list pick), nothing picked included: what loads one into
    /// the form. Fed by <see cref="SelectedRow"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<StandaloneSubAssemblyRow?> _picks = new(null);

    /// <summary>
    /// The selection pointed at <paramref name="row"/> without it being a pick: a load's fresh copy of the one picked, Save's
    /// reload (which loads the form itself), New's empty selection. The list follows; the form isn't loaded.
    /// </summary>
    private void RepointSelection(StandaloneSubAssemblyRow? row) => this.RaiseAndSetIfChanged(ref _selectedRow, row, nameof(SelectedRow));
    public StandaloneSubAssemblyRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (EqualityComparer<StandaloneSubAssemblyRow?>.Default.Equals(_selectedRow, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            _picks.OnNext(value);
        }
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

    private decimal? _scaleTo;
    /// <summary>What Scale makes the batch, or its base, weigh, in grams. Not part of the sub-assembly.</summary>
    public decimal? ScaleTo
    {
        get => _scaleTo;
        set => this.RaiseAndSetIfChanged(ref _scaleTo, value);
    }

    /// <summary>What Scale can make weigh <see cref="ScaleTo"/>: the whole batch, or the lines ticked as its base.</summary>
    public IReadOnlyList<BatchScaleOption> ScaleOptions { get; } =
    [
        new(BatchScaleBasis.Batch, "the whole batch"),
        new(BatchScaleBasis.Base, "the base (ticked lines)"),
    ];

    private BatchScaleOption _scaleOption = null!;
    public BatchScaleOption ScaleOption
    {
        get => _scaleOption;
        set => this.RaiseAndSetIfChanged(ref _scaleOption, value);
    }

    /// <summary>
    /// Every line's grams by the factor that makes the batch, or its base, weigh <see cref="ScaleTo"/>, as one undo step
    /// ("make it 5 kg"). A line typed as a baker's percentage keeps it and follows the base.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ScaleCommand { get; }

    private decimal _costPerGram;
    public decimal CostPerGram
    {
        get => _costPerGram;
        private set => this.RaiseAndSetIfChanged(ref _costPerGram, value);
    }

    /// <summary>The details tabs along the bottom: Recipe, then Dependents.</summary>
    public const int RecipeTab = 0;

    /// <summary>Dependents: the products and made ingredients the sub-assembly goes into.</summary>
    public const int DependentsTab = 1;

    private int _selectedDetailsTab = RecipeTab;
    /// <summary>
    /// The details tab showing, kept here so the screen comes back on the tab it was left on, as Products and Raw
    /// Materials do. Both tabs always show, so nothing moves it but the user.
    /// </summary>
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private IReadOnlyList<LinkingProduct> _dependents = [];
    /// <summary>The products linking the sub-assembly the form shows, by name: one tile each under Dependents, which opens it.</summary>
    public IReadOnlyList<LinkingProduct> Dependents
    {
        get => _dependents;
        private set => this.RaiseAndSetIfChanged(ref _dependents, value);
    }

    private IReadOnlyList<MadeIngredientUse> _madeIngredients = [];
    /// <summary>
    /// The made ingredients made from the sub-assembly the form shows, by name: one tile each under Dependents, after the
    /// products, which opens it on Raw Materials.
    /// </summary>
    public IReadOnlyList<MadeIngredientUse> MadeIngredients
    {
        get => _madeIngredients;
        private set => this.RaiseAndSetIfChanged(ref _madeIngredients, value);
    }

    private string _dependentsText = NotUsedText;
    /// <summary>The caption under the Dependents tiles: what linking means for editing it, or how to link it when nothing does.</summary>
    public string DependentsText
    {
        get => _dependentsText;
        private set => this.RaiseAndSetIfChanged(ref _dependentsText, value);
    }

    private const string NotUsedText = "No product uses it yet. Link or copy it from a product's recipe on the Products screen.";

    private const string SetNotUsedText = "No product uses it yet. Link it from a product's Recipe Structure: Add Sub-Assembly, Link an existing one.";

    private const string UsedText = "These products follow every change saved here, and it can't be deleted while they use it.";

    private const string MadeFromText = "These follow every change saved here, and it can't be deleted while they use it: the products link it, and the made ingredients are made from it.";

    /// <summary>A made ingredient's Dependents tile: asks for it to be opened on Raw Materials, through <see cref="RawMaterialOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenRawMaterialCommand { get; }

    private readonly Subject<int> _rawMaterialOpenRequests = new();

    /// <summary>The made ingredients Dependents tiles ask to open, by id. The main window subscribes and opens each on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }

    /// <summary>Starts a new sub-assembly from the one the form shows (<see cref="CopyForm"/>), saved by Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    /// <summary>Gives sub-assemblies another role, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ReactiveCommand<IReadOnlyList<SubAssemblyMove>, RxVoid> MoveSubAssembliesCommand { get; }

    /// <summary>Puts back the roles the last move took the sub-assemblies from (the notice bar's Undo).</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; }

    /// <summary>
    /// The list's New … Here: a new sub-assembly in the folder's role. With no folder (the list's empty space), as New
    /// starts one. Like New, it adds the sub-assembly's row to the list for its name to be typed into (TreeNameEdit).
    /// </summary>
    public ReactiveCommand<SubAssemblyFolderNode?, RxVoid> NewSubAssemblyInCommand { get; }

    /// <summary>The list's Rename: the sub-assembly's name becomes a box to type into, in place, and the name typed is saved at once.</summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> RenameSubAssemblyCommand { get; }

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> DeleteSubAssemblyCommand { get; }

    /// <summary>
    /// Make In-House Ingredient on a recipe of the list: asks for the made ingredient's name and Made category, then adds
    /// that ingredient, made from the recipe, at once. The recipe stays as it is.
    /// </summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> MakeInHouseIngredientCommand { get; }

    /// <summary>
    /// Input: Make In-House Ingredient's prompt (MadeIngredientPrompt), its name box starting with the sub-assembly's name. Output:
    /// the name typed and the Made category picked, or null if it was cancelled. The View shows a TextPromptWindow with a picker.
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>
    /// The list's New Folder: a "New folder" row for its name to be typed into (TreeNameEdit), and the name typed adds a
    /// role after the others, as Settings → Roles adds one.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> NewRoleCommand { get; }

    /// <summary>The list's Rename Role, typed in place, which keeps the role's place and whether products own it.</summary>
    public ReactiveCommand<SubAssemblyFolderNode, RxVoid> RenameRoleCommand { get; }

    /// <summary>The list's Delete Role, offered only for a role no sub-assembly in the list is in, and never the Packaging role.</summary>
    public ReactiveCommand<SubAssemblyFolderNode, RxVoid> DeleteRoleCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ExpandAllCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> CollapseAllCommand { get; }

    /// <summary>A Dependents tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products Dependents tiles ask to open, by id. The main window, which owns the tabs, subscribes and opens each
    /// on the Products screen. Raised as the tile is clicked, not on the main-thread scheduler a command's own results
    /// wait for.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>
    /// Starts a new product that links the picked sub-assembly, through <see cref="NewProductRequests"/>. Not for a packaging
    /// set, which isn't a recipe, and not while the form has changes not saved: the product links it as it's saved.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> NewProductCommand { get; }

    private readonly Subject<int> _newProductRequests = new();

    /// <summary>
    /// The sub-assemblies New Product asks a new product to link, by id. The main window subscribes and starts the new
    /// product on the Products screen, as it opens a Dependents tile's product.
    /// </summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

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
        IRawMaterialCategoryRepository rawMaterialCategoryRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        IValidator<SubAssemblyRole> roleValidator,
        IViewStateStore viewStateStore,
        VersionedSaves versionedSaves,
        ILogger logger,
        SharedRecipeImpact? recipeImpact = null)
        : base(logger)
    {
        _treeNaming = new TreeNaming(
            _treeSelection, () => this.RaisePropertyChanged(nameof(TreeNameEdit)), IsNewRow, CommitTreeNameToAsync, TreeNameCancelled);
        _recipeImpact = recipeImpact;
        _subAssemblyRepository = subAssemblyRepository;
        _versionedSaves = versionedSaves;
        _lineRepository = lineRepository;
        _roleRepository = roleRepository;
        _rawMaterialCategoryRepository = rawMaterialCategoryRepository;
        _subAssemblyValidator = subAssemblyValidator;
        _roleValidator = roleValidator;

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter), as on Raw Materials; Dispose writes any change still waiting. As read, so
        // nothing is written until a folder is opened or closed: the folders aren't made until the roles are loaded.
        SubAssemblyTree = new SubAssemblyTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, SubAssemblyTree.ExpansionChanged, SubAssemblyTree.ClosedFolderKeys);

        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

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

        // The list's tree follows the rows it shows once each change has reached them (a sub-assembly added, removed,
        // renamed, moved or filtered out) and the roles (one added, renamed, reordered or deleted).
        _rowCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRows, byName)
            .Subscribe(_ => RefreshTree());

        // Through PropertyChanged, which a role added, removed or replaced raises too, for the reason WatchForSave gives.
        ((INotifyPropertyChanged)Roles).PropertyChanged += (_, _) => RefreshTree();

        // The search decides whether empty folders show, even when it leaves the same sub-assemblies showing. A name being
        // typed into a row is given up first: the search may hide the row.
        this.WhenAnyValue(x => x.SearchText)
            .Skip(1)
            .Subscribe(_ => CancelTreeName());
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshTree());

        // A sub-assembly opened, by its Save included, ends the new one's row: the form no longer shows the new one.
        this.WhenAnyValue(x => x.SelectedRow)
            .Where(row => row is not null)
            .Subscribe(_ => EndNewSubAssemblyRow());
        this.WhenAnyValue(x => x.SelectedRow)
            .Subscribe(_ => SyncTreeSelection());

        // Picking a sub-assembly loads it into the form. Only a pick does (_picks): a refresh re-points the selection at its
        // refreshed copy quietly (RepointSelection), so it leaves the form and its unsaved edits alone.
        LoadLatest(_picks, (row, _) => LoadIntoFormAsync(row));

        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => _updates.Run(ResolveLines));

        // Cost follows both an ingredient picked and grams typed.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => _updates.Run(RecomputeTotals));

        // A packaging set's lines price themselves as their material is picked, and the set's cost follows.
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .Subscribe(_ => _updates.Run(ResolvePackagingLines));
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(RecomputeSetCost));

        // The formula's base: what's ticked, and how much of it there is.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IsFormulaBase)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => _updates.Run(RecomputeFormulaBase));

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection and unsaved lines while picking up
        // what changed elsewhere: a repriced ingredient, a renamed role, a product that links one.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
            // back to it before the next began, and waited its turn behind whatever the screen was drawing.
            var rolesRead = Task.Run(() => roleRepository.GetAllAsync(CancellationToken.None));
            var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(CancellationToken.None));
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
                _rowCache.MergeFrom(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [], madeFrom.GetValueOrDefault(s.Id))));

                // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
                EditingRole = Roles.FirstOrDefault(r => r.Id == roleId) ?? (_editingId == 0 && roleId is null ? Roles.FirstOrDefault() : null);

                IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
                PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
                RecomputeTotals();
                SetCost = PackagingLines.Sum(l => l.CostPerUnit);

                RepointSelection(Rows.FirstOrDefault(r => r.Id == selectedId));
                if (_editingId > 0)
                {
                    ShowDependents(linkedBy.GetValueOrDefault(_editingId) ?? [], madeFrom.GetValueOrDefault(_editingId) ?? []);
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
        }));

        // A new sub-assembly replaces the one showing, so its unsaved changes are asked about first. Copy doesn't ask: the
        // copy holds them.
        NewCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewSubAssemblyInList()));

        CopyCommand = ReactiveCommand.Create(CopyForm, this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);

        OpenRawMaterialCommand = ReactiveCommand.Create<int>(_rawMaterialOpenRequests.OnNext);

        NewProductCommand = ReactiveCommand.Create(
            () =>
            {
                if (History.IsDirty)
                {
                    ErrorMessage = $"Save {HeaderTitle} first, or undo its changes: the new product links it as it's saved.";
                    return;
                }

                ErrorMessage = null;
                _newProductRequests.OnNext(SelectedRow!.Id);
            },
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null && row.SubAssembly.Role?.IsPackaging != true));

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var name = EditingName.Trim();
            var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingRole?.Id ?? 0 });
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // The lines as they're typed, read here on the UI thread, for the save below.
            var subAssembly = new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id };
            Func<CancellationToken, Task<int>> save;
            if (IsPackaging)
            {
                var setLines = PackagingLineRow.CompleteLines(PackagingLines, "set");
                if (setLines.Count == 0)
                {
                    ErrorMessage = $"Add the packaging materials that go into {name}, per set.";
                    return;
                }

                List<(int PackagingMaterialId, int? UnitId, decimal QtyPerUnit)> packagingLines =
                    [.. setLines.Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit))];
                save = cancellationToken => _subAssemblyRepository.SaveStandalonePackagingAsync(subAssembly, packagingLines, cancellationToken);
            }
            else
            {
                var lines = IngredientLineRow.CompleteLines(Lines, name);
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
                    if (impacts.Count > 0 && !await ConfirmDelete.Handle(SharedRecipeImpact.Question(name, impacts, MadeIngredients.Count)))
                    {
                        return;
                    }
                }
            }

            // Written with a version of each product using it that this Save changes, both or neither (VersionedSaves).
            var saved = await Task.Run(() => _versionedSaves.SaveSubAssemblyAsync(subAssembly.Id, name, save, DateTime.Now, CancellationToken.None));

            // The list picks up the new totals; then the form reloads what was saved.
            await LoadCommand.Execute();
            var row = Rows.FirstOrDefault(r => r.Id == saved.Id);
            RepointSelection(row);
            await LoadIntoFormAsync(row);
            SuccessMessage = ProductVersionRecorder.RecordedMessage(saved.ProductsVersioned) is { } recorded
                ? $"{name} was saved. {recorded}"
                : $"{name} was saved.";
        }),
        History.WhenAnyValue(h => h.IsDirty));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => DeleteSubAssemblyAsync(SelectedRow!),
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        MoveSubAssembliesCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<SubAssemblyMove>>(
            moves => RunSafelyAsync(() => MoveSubAssembliesAsync(moves, isUndo: false)));
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveSubAssembliesAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewSubAssemblyInCommand = ReactiveCommand.CreateFromTask<SubAssemblyFolderNode?>(folder => AfterUnsavedChangesAsync(() => NewSubAssemblyInList(
            folder is null ? null : Roles.FirstOrDefault(r => r.Id == folder.RoleId))));
        RenameSubAssemblyCommand = ReactiveCommand.Create<SubAssemblyLeafNode>(BeginRenameSubAssembly);
        DeleteSubAssemblyCommand = ReactiveCommand.CreateFromTask<SubAssemblyLeafNode>(leaf => DeleteSubAssemblyAsync(leaf.Row));
        MakeInHouseIngredientCommand = ReactiveCommand.CreateFromTask<SubAssemblyLeafNode>(leaf => RunSafelyAsync(() => MakeInHouseIngredientAsync(leaf.Row)));
        NewRoleCommand = ReactiveCommand.Create(BeginNewFolder);
        RenameRoleCommand = ReactiveCommand.Create<SubAssemblyFolderNode>(BeginRenameRole);
        DeleteRoleCommand = ReactiveCommand.CreateFromTask<SubAssemblyFolderNode>(DeleteRoleAsync);
        ExpandAllCommand = ReactiveCommand.Create(SubAssemblyTree.ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create(SubAssemblyTree.CollapseAll);

        ScaleOption = ScaleOptions[0];
        ScaleCommand = ReactiveCommand.Create(
            ScaleBatch,
            this.WhenAnyValue(x => x.ScaleTo, x => x.BatchWeight, x => x.IsPackaging, (to, weight, isPackaging) => to is > 0m && weight > 0m && !isPackaging));

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

    /// <summary>IUndoRecorder: goes up as another sub-assembly, or a new one, takes the form's place.</summary>
    public int FormVersion { get; private set; }

    bool IUndoRecorder.IsEditPending
    {
        get => History.IsEditPending;
        set => History.IsEditPending = value;
    }

    private void ScaleBatch()
    {
        ErrorMessage = null;
        var from = ScaleOption.Basis == BatchScaleBasis.Base ? _formulaBase : BatchWeight;
        if (from <= 0m || ScaleTo is not { } to || to <= 0m)
        {
            ErrorMessage = "Nothing is ticked as the base, so there's no base to scale. Tick the base lines, or scale the whole batch.";
            return;
        }

        AsUndoStep(() =>
        {
            // Lines typed in grams are scaled; a line typed as a percentage follows the base, which is among them.
            foreach (var line in Lines.Where(l => l.Ingredient is not null && l.AuthoredBakersPercent is null && l.Grams is not null))
            {
                line.Grams = SubAssemblyRules.ScaledGrams(line.Grams!.Value, from, to);
            }

            RecomputeFormulaBase();
            RecomputeTotals();
        });
    }

    /// <summary>What a Save of a recipe products link is asked about with; null in a screen built without it.</summary>
    private readonly SharedRecipeImpact? _recipeImpact;

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

        ApplyRestore(() => _updates.Batch(() =>
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
        }));

        // Once more, in this order, from the form put back: an undo that leaves a table as it was asks for none of these.
        ResolveLines();
        ResolvePackagingLines();
        RecomputeFormulaBase();
        RecomputeTotals();
        RecomputeSetCost();
    }

    /// <summary>
    /// New, from the header or the list. A sub-assembly still waiting for LoadCommand to open it is no longer opened: it
    /// would load over the new form, and a name typed into it since would then be saved as that sub-assembly's.
    /// </summary>
    private void StartNewSubAssembly(SubAssemblyRole? role = null)
    {
        _subAssemblyToOpen = null;
        ClearForm(role);
    }

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.</summary>
    private void ResolveLines() => IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));

    /// <summary>A packaging set's lines price themselves as their material is picked.</summary>
    private void ResolvePackagingLines() => PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));

    /// <summary>A packaging set's cost follows its lines.</summary>
    private void RecomputeSetCost() => SetCost = PackagingLines.Sum(l => l.CostPerUnit);

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, in <paramref name="role"/> or the first role.</summary>
    private void ClearForm(SubAssemblyRole? role = null)
    {
        // The new sub-assembly's row, if one was named in the list, goes with the form it stood for.
        EndNewSubAssemblyRow();
        History.Clear();
        _formLoadVersion++;
        RepointSelection(null);
        FormVersion++;
        _editingId = 0;
        HeaderTitle = "New sub-assembly";
        EditingName = string.Empty;
        EditingRole = role ?? Roles.FirstOrDefault();
        SelectedLine = null;
        Lines.Clear();
        SelectedPackagingLine = null;
        PackagingLines.Clear();
        ShowDependents([], []);

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
        ShowDependents([], []);
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
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var packagingById = PackagingMaterials.ToDictionary(m => m.Id);

        // Not for the same sub-assembly loaded again, as a Save does: the boxes still hold what's its.
        if (row.Id != _editingId)
        {
            FormVersion++;
        }

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
        ShowDependents(row.GetDependents(), row.GetMadeIngredients());

        // The form now holds what was saved, so this is what Save's dirty check measures against. Taken at the
        // end: the lines land after the name and role, and a form measured before them would read as changed
        // the moment they arrived.
        History.MarkClean();
    }

    private void ShowDependents(IReadOnlyList<LinkingProduct> products, IReadOnlyList<MadeIngredientUse> madeIngredients)
    {
        Dependents = products;
        MadeIngredients = madeIngredients;
        DependentsText = madeIngredients.Count > 0 ? MadeFromText
            : products.Count == 0 ? IsPackaging ? SetNotUsedText : NotUsedText
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

    /// <summary>
    /// Opens a sub-assembly at the end of the next LoadCommand, for the one in the screen's address as the screen opens:
    /// its list is fresh only then. New clicked meanwhile is the later word, and the sub-assembly isn't opened over it.
    /// </summary>
    public void OpenSubAssemblyOnLoad(int subAssemblyId)
    {
        _subAssemblyToOpen = subAssemblyId;
    }

    /// <summary>
    /// <see cref="OpenSubAssembly"/>, asking first about the unsaved changes of the sub-assembly showing when another is to
    /// replace it. False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenSubAssemblyAsync(int subAssemblyId)
    {
        if (subAssemblyId != SelectedRow?.Id && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenSubAssembly(subAssemblyId);
        return true;
    }

    public bool HasUnsavedChanges => History.IsDirty;

    /// <inheritdoc/>
    public Task<bool> ResolveUnsavedChangesAsync(bool revert) => AskAboutUnsavedChangesAsync(
        History.IsDirty,
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim() : SelectedRow?.Name ?? "the new sub-assembly",
        async () =>
        {
            await SaveCommand.Execute();
            return !History.IsDirty;
        },
        revert ? RevertToSavedAsync : null);

    /// <summary>Runs <paramref name="then"/>, which replaces the sub-assembly showing, once its unsaved changes are saved or thrown away.</summary>
    private async Task AfterUnsavedChangesAsync(Action then)
    {
        if (await ResolveUnsavedChangesAsync(revert: false))
        {
            then();
        }
    }

    /// <summary>
    /// Don't Save on leaving the screen: the form goes back to the sub-assembly as it was last saved, its lines read again.
    /// A new one (or a copy) was never saved, so it goes back to a blank one.
    /// </summary>
    private Task RevertToSavedAsync() => LoadIntoFormAsync(SelectedRow);

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the sub-assembly the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(Rows, row => row.Name, text) is not { } row)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenSubAssemblyAsync(row.Id));
        return true;
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

            await LoadCommand.Execute();
        });

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its sub-assembly the one picked.
        _treeSelection.Quietly(() => SubAssemblyTree.Refresh(Roles, VisibleRows, isFiltered: SearchText.Trim().Length > 0));

        // A folder picked stays picked. Otherwise the row goes back to the sub-assembly picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not SubAssemblyFolderNode)
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// Puts SelectedRow's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone, unless it's
    /// picked already, when the rows picked with it stay. Nothing picked, or a search hiding it, picks no row.
    /// </summary>
    private void SyncTreeSelection() =>
        _treeSelection.BringFirst(SelectedRow is { } row ? SubAssemblyTree.Find(row.Id) : SubAssemblyTree.PendingSubAssembly);

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a sub-assembly's row picked by the user picks the sub-assembly.
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(nameof(SelectedTreeSubAssemblies));
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the sub-assembly leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        // The new sub-assembly's row is the form's already.
        if (!_treeSelection.IsSyncing && first is SubAssemblyLeafNode { IsNew: false } leaf && leaf.Row.Id != SelectedRow?.Id)
        {
            if (History.IsDirty)
            {
                _ = RunSafelyAsync(() => PickAfterAskingAsync(leaf.Row.Id));
            }
            else
            {
                SelectedRow = leaf.Row;
            }
        }
    }

    /// <summary>
    /// Another sub-assembly's row picked while the form holds unsaved changes: the list goes back to the row of the one
    /// showing, and the one picked opens once the changes are saved or thrown away.
    /// </summary>
    private async Task PickAfterAskingAsync(int subAssemblyId)
    {
        // The tree calls TreeSelectionChanged in the middle of changing its selection: going on once that's over lets the
        // list take the row of the sub-assembly showing back.
        await Task.Yield();
        SyncTreeSelection();
        await OpenSubAssemblyAsync(subAssemblyId);
    }

    /// <summary>
    /// Saves <paramref name="moves"/> and files the sub-assemblies in their new folders. On the one the form shows, the
    /// Role picker follows, and every undo step takes the new role as given, so no undo puts the old one back for a Save to
    /// write; whatever else is typed there stays unsaved.
    /// </summary>
    private async Task MoveSubAssembliesAsync(IReadOnlyList<SubAssemblyMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _rowCache.Lookup(m.SubAssemblyId))
            .Where(found => found.HasValue)
            .Select(found => new SubAssemblyMove(found.Value.Id, found.Value.SubAssembly.RoleId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _subAssemblyRepository.MoveAsync(moves, CancellationToken.None);

        // The load only re-points the selection at the fresh copy: the form isn't loaded again, so what's typed stays.
        await LoadCommand.Execute();
        if (moves.FirstOrDefault(m => m.SubAssemblyId == _editingId) is { } onForm)
        {
            ApplyRefresh(() => EditingRole = Roles.FirstOrDefault(r => r.Id == onForm.RoleId));
            History.RewriteAll(snapshot => snapshot with { RoleId = onForm.RoleId });
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        _treeSelection.Restore(picked, r => r is not SubAssemblyLeafNode leaf || SubAssemblyTree.Find(leaf.Row.Id) == leaf);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Ube Halaya to Filling", "Moved 3 sub-assemblies to Filling", or, for
    /// sub-assemblies given different roles, "Moved 3 sub-assemblies".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<SubAssemblyMove> moves)
    {
        var what = moves.Count == 1 && _rowCache.Lookup(moves[0].SubAssemblyId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} sub-assemblies";
        var roleId = moves[0].RoleId;
        return moves.All(m => m.RoleId == roleId) && Roles.FirstOrDefault(r => r.Id == roleId) is { } role
            ? $"Moved {what} to {role.Name}"
            : $"Moved {what}";
    }

    /// <summary>
    /// The menu a right-click opens on the list (SubAssemblyList): on <paramref name="node"/>'s row, which the list
    /// picks first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null.
    /// A sub-assembly's menu covers every sub-assembly picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(SubAssemblyTreeNode? node) => node switch
    {
        SubAssemblyLeafNode { IsNew: true } or SubAssemblyFolderNode { IsNewFolder: true } => [],
        SubAssemblyLeafNode leaf => SubAssembliesMenu(SelectedTreeSubAssemblies is var picked && picked.Contains(leaf) ? picked : [leaf]),
        SubAssemblyFolderNode folder => FolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [SubAssemblyTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or SubAssemblyTreeNode ? TreeMenuFor(node as SubAssemblyTreeNode) : [];

    /// <summary>Only sub-assemblies can be dragged, one or several: the folders are the roles, in their own order.</summary>
    bool IListTreeScreen.CanDrag(IEnumerable<object> nodes) => nodes.All(node => node is SubAssemblyLeafNode { IsNew: false });

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    IObservable<object?> IListTreeScreen.Drop(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        return moves.Count == 0
            ? Observable.Return<object?>(null)
            : MoveSubAssembliesCommand.Execute(moves).Select(_ => (object?)SubAssemblyTree.Find(moves[0].SubAssemblyId));
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (SubAssemblyTree.MovesTo): none unless every one is a record.</summary>
    private static IReadOnlyList<SubAssemblyMove> DropMoves(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { Count: > 0 } dragging && dragging.All(node => node is SubAssemblyLeafNode)
            ? SubAssemblyTree.MovesTo(target as SubAssemblyTreeNode, dragging.Cast<SubAssemblyLeafNode>().Select(leaf => leaf.SubAssembly))
            : [];

    private List<TreeMenuItem> SubAssembliesMenu(IReadOnlyList<SubAssemblyLeafNode> leaves)
    {
        var subAssemblies = leaves.Select(l => l.SubAssembly).ToList();
        var isOne = subAssemblies.Count == 1;
        List<TreeMenuItem> items = [];

        // Only recipes move, among the recipe roles: every packaging set is in the one Packaging role, and a set picked
        // with recipes has no role it could share with them.
        if (!subAssemblies.Any(SubAssemblyTree.IsPackagingSet))
        {
            List<TreeMenuItem> roles =
            [
                .. Roles.Where(r => !r.IsPackaging).Select(r =>
                {
                    var moves = SubAssemblyTree.MovesTo(r, subAssemblies);
                    return new TreeMenuItem(r.Name, MoveSubAssembliesCommand, moves) { IsEnabled = moves.Count > 0 };
                }),
            ];
            items.Add(new($"Move {(isOne ? string.Empty : $"{subAssemblies.Count} Sub-Assemblies ")}to")
            {
                Icon = AppIcon.Move,
                Items = roles,
                IsEnabled = roles.Any(r => r.IsEnabled),
            });
        }

        if (isOne)
        {
            var leaf = leaves[0];
            if (items.Count > 0)
            {
                items.Add(TreeMenuItem.Separator);
            }

            items.Add(new("Rename", RenameSubAssemblyCommand, leaf) { Gesture = "F2", Icon = AppIcon.Rename });

            // A made ingredient can be made from a recipe; a packaging set's lines are packaging.
            if (!SubAssemblyTree.IsPackagingSet(leaf.SubAssembly))
            {
                items.Add(new("Make In-House Ingredient…", MakeInHouseIngredientCommand, leaf) { Icon = AppIcon.InHouse });
            }

            items.AddRange([TreeMenuItem.Separator, new("Delete", DeleteSubAssemblyCommand, leaf) { Gesture = "Del", Icon = AppIcon.Delete }]);
        }

        return items;
    }

    private List<TreeMenuItem> FolderMenu(SubAssemblyFolderNode folder)
    {
        List<TreeMenuItem> items =
        [
            new(folder.IsPackaging ? "New Packaging Set Here" : "New Sub-Assembly Here", NewSubAssemblyInCommand, folder) { Icon = AppIcon.Add },
            TreeMenuItem.Separator,
            new("New Folder", NewRoleCommand) { Icon = AppIcon.NewFolder },
            new("Rename Role", RenameRoleCommand, folder) { Gesture = "F2", Icon = AppIcon.Rename },
        ];

        // The Packaging role is never deleted, so it isn't offered. Every sub-assembly in it counts, not just a search's
        // matches: the database won't delete a role anything is in.
        if (!folder.IsPackaging)
        {
            var holds = Rows.Count(r => r.SubAssembly.RoleId == folder.RoleId);
            items.Add(new(holds == 0 ? "Delete Role" : $"Delete Role (holds {holds})", DeleteRoleCommand, folder)
            {
                Gesture = "Del",
                Icon = AppIcon.Delete,
                IsEnabled = holds == 0,
            });
        }

        items.AddRange([TreeMenuItem.Separator, new("Expand All", ExpandAllCommand), new("Collapse All", CollapseAllCommand)]);
        return items;
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Sub-Assembly", NewSubAssemblyInCommand) { Icon = AppIcon.Add },
        new("New Folder", NewRoleCommand) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Make In-House Ingredient, saved at once once the prompt is answered: a new made ingredient made from the
    /// recipe, in the Made category picked, with no add-ins and no weight loss. The
    /// recipe stays here as it is, and the products linking it are
    /// left alone; it shows under Dependents. The form's unsaved changes to it have to be saved or undone first: the made
    /// ingredient is made from it as it's saved.
    /// </summary>
    private async Task MakeInHouseIngredientAsync(StandaloneSubAssemblyRow row)
    {
        ErrorMessage = null;
        if (_editingId == row.Id && History.IsDirty)
        {
            ErrorMessage = $"Save {row.Name} first, or undo its changes: the made ingredient is made from it as it's saved.";
            return;
        }

        var categories = await MadeIngredientPrompt.MadeCategoriesAsync(_rawMaterialCategoryRepository);
        if (categories.Count == 0)
        {
            ErrorMessage = MadeIngredientPrompt.NoMadeCategoryMessage;
            return;
        }

        var answer = await PromptForMadeIngredient.Handle(MadeIngredientPrompt.RequestFor(
            "Make In-House Ingredient",
            row.Name,
            categories,
            $"A made ingredient made from {row.Name}, with no weight loss, so it costs per gram what the recipe does. {row.Name} stays here as it is, and its lines are still changed here."));
        if (answer is null)
        {
            return;
        }

        var name = answer.Text.Trim();
        if (Ingredients.Concat(PackagingMaterials).Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = MadeIngredientPrompt.NameTakenMessage(name);
            return;
        }

        var category = categories[answer.Choice];
        await _subAssemblyRepository.AddMadeIngredientAsync(row.Id, new MadeIngredientDraft(name, category.Id), CancellationToken.None);

        await LoadCommand.Execute();
        SuccessMessage = $"{name} was added to {category.Name}, made from {row.Name}.";
    }

    /// <summary>
    /// The list's Rename: the sub-assembly's name becomes a box to type into, in place (TreeNameEdit), and the name typed
    /// is saved at once (<see cref="RenameSubAssemblyToAsync"/>).
    /// </summary>
    private void BeginRenameSubAssembly(SubAssemblyLeafNode leaf)
    {
        if (leaf.IsNew)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(leaf, leaf.Row.Name);
    }

    /// <summary>
    /// The list's Rename Role: the folder's name becomes a box to type into, in place, and the name typed renames the role
    /// (<see cref="RenameRoleToAsync"/>), keeping its place and whether products own it.
    /// </summary>
    private void BeginRenameRole(SubAssemblyFolderNode folder)
    {
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(folder, role.Name, RoleHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row after the roles, picked, with its name ready to be typed, as in a file
    /// explorer; the name typed adds a role (<see cref="CommitTreeNameAsync"/>). The search is cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder() => BeginNewFolderWith(SubAssemblyFolderNode.NewFolderTitle);

    private void BeginNewFolderWith(string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        _treeNaming.Start(SubAssemblyTree.BeginNewFolder(), text, RoleHint);
    }

    /// <summary>What the name box on a role's folder says when pointed at: the prompts used to say it.</summary>
    private const string RoleHint =
        "A role: a folder here, and a section in the recipes that use it. Its place and whether products own it are set in Settings, on the Roles tab.";

    /// <summary>
    /// New and New … Here: <see cref="StartNewSubAssembly"/>, and the new sub-assembly's row in its role's folder, picked,
    /// with its name ready to be typed, as a new file in a file explorer. The name typed goes into the form
    /// (<see cref="CommitTreeNameAsync"/>), and the row stays until Save files the sub-assembly or the form leaves it. The
    /// search is cleared first, so the row shows. With no role to put it in (none yet), the form starts as before.
    /// </summary>
    private void NewSubAssemblyInList(SubAssemblyRole? role = null)
    {
        CancelTreeName();
        SearchText = string.Empty;
        StartNewSubAssembly(role);
        if (EditingRole is not { } inRole)
        {
            return;
        }

        var title = inRole.IsPackaging ? "New packaging set" : "New sub-assembly";
        if (SubAssemblyTree.BeginNewSubAssembly(new SubAssembly { Name = title, RoleId = inRole.Id, Role = inRole }) is { } row)
        {
            _treeNaming.Start(row, title);
        }
    }

    /// <summary>
    /// Takes the new sub-assembly's row out of the list (<see cref="NewSubAssemblyInList"/>), and the rows picked, and stops
    /// the naming in it: the form no longer shows the new sub-assembly, or it's saved and has a row of its own. The form is
    /// left as it is.
    /// </summary>
    private void EndNewSubAssemblyRow() => _treeNaming.EndNewRow(SubAssemblyTree.PendingSubAssembly, SubAssemblyTree.EndNewSubAssembly);

    /// <summary>
    /// The name typed into the list's box (<see cref="TreeNameEdit"/>), on Enter or on leaving it. A sub-assembly's or a
    /// role's is renamed at once; a new folder's adds a role after the others, or picks the one of that name already; a
    /// new sub-assembly's goes into the form, as typed there, and its row takes it: it needs a line before Save can file it.
    /// An empty name, or the name it had, is an Esc (<see cref="CancelTreeName"/>). A name refused shows why, and the box
    /// comes back with it. A second call for the same box, as Enter's box is left, does nothing.
    /// </summary>
    public Task CommitTreeNameAsync(string text) => _treeNaming.CommitAsync(text);

    /// <summary>Whether a row of the list is a new sub-assembly's or a new folder's, still to be named.</summary>
    private static bool IsNewRow(object node) => node is SubAssemblyLeafNode { IsNew: true } or SubAssemblyFolderNode { IsNewFolder: true };

    /// <summary>What a name typed into the list's box does (<see cref="CommitTreeNameAsync"/>), once it's taken for one.</summary>
    private async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case SubAssemblyLeafNode { IsNew: true } row:
                NameNewSubAssembly(row, name);
                break;

            case SubAssemblyLeafNode leaf:
                await RunSafelyAsync(() => RenameSubAssemblyToAsync(leaf.Row, name));
                if (ErrorMessage is not null && SubAssemblyTree.Find(leaf.Row.Id) is { } stillListed)
                {
                    _treeNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case SubAssemblyFolderNode { IsNewFolder: true } folder:
                _treeNaming.EndNewRow(folder, SubAssemblyTree.EndNewFolder);
                await RunSafelyAsync(() => AddRoleAsync(name));
                if (ErrorMessage is not null)
                {
                    BeginNewFolderWith(text);
                }

                break;

            case SubAssemblyFolderNode folder:
                await RunSafelyAsync(() => RenameRoleToAsync(folder, name));
                if (ErrorMessage is not null && Roles.Any(r => r.Id == folder.RoleId))
                {
                    _treeNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// Esc in the list's name box: the name stays as it was, and a new sub-assembly's or folder's row goes, with nothing
    /// saved. The form stays on the new sub-assembly, blank, as New leaves it.
    /// </summary>
    public void CancelTreeName() => _treeNaming.Cancel();

    /// <summary>What Esc does to the row named (<see cref="CancelTreeName"/>), once its box is closed.</summary>
    private void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case SubAssemblyLeafNode { IsNew: true }:
                EndNewSubAssemblyRow();
                ApplyRefresh(() => EditingName = string.Empty);
                break;
            case SubAssemblyFolderNode { IsNewFolder: true } folder:
                _treeNaming.EndNewRow(folder, SubAssemblyTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new sub-assembly's row: it goes into the form, as typed there (an undo step, which Save is
    /// offered for), and the row takes it, until Save files the sub-assembly or the form leaves it.
    /// </summary>
    private void NameNewSubAssembly(SubAssemblyLeafNode row, string name)
    {
        var recorder = (IUndoRecorder)this;
        recorder.BeginUndoStep();
        EditingName = name;
        recorder.EndUndoStep();
        var subAssembly = row.SubAssembly;
        row.Row = new StandaloneSubAssemblyRow(new SubAssembly { Name = name, RoleId = subAssembly.RoleId, Role = subAssembly.Role }, []);
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the sub-assembly's at once. On the one the form shows, the title and the name box
    /// take it, and the form's undo steps take it as given, as a move gives them its role: whatever else is typed there
    /// stays to be saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameSubAssemblyToAsync(StandaloneSubAssemblyRow row, string name)
    {
        await _subAssemblyRepository.RenameAsync(row.Id, name, CancellationToken.None);
        await LoadCommand.Execute();
        if (_editingId == row.Id)
        {
            HeaderTitle = name;
            ApplyRefresh(() => EditingName = name);
            History.RewriteAll(snapshot => snapshot with { Name = name });
        }
    }

    /// <summary>
    /// Adds a role named <paramref name="name"/> after the others, then picks its folder. A name that's a role already picks
    /// that one's folder instead. A name refused shows why.
    /// </summary>
    private async Task AddRoleAsync(string name)
    {
        var role = Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            var added = new SubAssemblyRole { Name = name };
            var validation = _roleValidator.Validate(added);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // AddAsync fills in the saved Id, which the copy read back is found by.
            await _roleRepository.AddAsync(added, CancellationToken.None);
            await LoadCommand.Execute();
            role = Roles.FirstOrDefault(r => r.Id == added.Id);
        }

        if (role is not null && SubAssemblyTree.Folder(role.Id) is { } folder && SubAssemblyTree.Roots.Contains(folder))
        {
            SelectedTreeNode = folder;
        }
    }

    /// <summary>Renames the folder's role to <paramref name="name"/>, keeping its place and whether products own it. A name refused shows why.</summary>
    private async Task RenameRoleToAsync(SubAssemblyFolderNode folder, string name)
    {
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
        {
            return;
        }

        var renamed = new SubAssemblyRole
        {
            Id = role.Id,
            Name = name,
            SortOrder = role.SortOrder,
            IsPackaging = role.IsPackaging,
            IsProductOwned = role.IsProductOwned,
        };
        var validation = _roleValidator.Validate(renamed);
        if (!validation.IsValid)
        {
            ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return;
        }

        // Everything is read again, the sub-assemblies with it, which carry their role's name. The Role picker keeps the
        // role it holds (LoadCommand), under its new name.
        await _roleRepository.UpdateAsync(renamed, CancellationToken.None);
        await LoadCommand.Execute();
    }

    /// <summary>
    /// The list's Delete Role, once it's confirmed: only for a role no sub-assembly in the list is in, and never the
    /// Packaging role. The menu greys it out, or leaves it out, otherwise. A role a product's own sub-assembly is in, which
    /// the list doesn't show, is refused by the database, and the screen says so.
    /// </summary>
    private Task DeleteRoleAsync(SubAssemblyFolderNode folder)
    {
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role
            || role.IsPackaging
            || Rows.Any(r => r.SubAssembly.RoleId == role.Id))
        {
            return Task.CompletedTask;
        }

        return RunWithConfirmationAsync(
            $"Delete the '{role.Name}' role? It holds no sub-assemblies here. This can't be undone.",
            async () =>
            {
                await _roleRepository.DeleteAsync(role.Id, CancellationToken.None);

                // A new sub-assembly's form may have had it picked: the undo steps forget it, so none puts back a role
                // there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.RoleId == role.Id ? snapshot with { RoleId = null } : snapshot);
            });
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        SubAssemblyTree.Dispose();

        _rowCache.Dispose();
        _productOpenRequests.Dispose();
        _rawMaterialOpenRequests.Dispose();
        _picks.Dispose();
        _newProductRequests.Dispose();
    }
}
