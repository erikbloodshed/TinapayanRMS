using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using DynamicData;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing.Versions;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Sub-Assemblies screen: standalone sub-assemblies, which no product owns. Products link them (and
/// follow every change saved here) or copy them, from their recipe on the Products screen. One Save writes
/// the name, the role and the lines together.
/// </summary>
public sealed partial class SubAssembliesViewModel : ListFormViewModel<SubAssemblyFormSnapshot>, IDisposable, IRecordScreen, IListTreeScreen
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IRawMaterialCategoryRepository _rawMaterialCategoryRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly IValidator<SubAssemblyRole> _roleValidator;
    private readonly VersionedSaves _versionedSaves;
    private readonly SourceCache<StandaloneSubAssemblyRow, int> _rowCache = new(r => r.Id);
    private ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _rows;
    private ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _visibleRows;

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

    /// <summary>The sub-assemblies picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<SubAssemblyLeafNode> SelectedTreeSubAssemblies => [.. SelectedTreeNodes.OfType<SubAssemblyLeafNode>().Where(l => !l.IsNew)];

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

    /// <summary>A packaging set's lines and what the set costs, the packaging table's (SubAssemblyPackagingSet).</summary>
    public SubAssemblyPackagingSet PackagingSet { get; }

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
    public ReactiveCommand<RxVoid, RxVoid> ScaleCommand { get; private set; }

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
    public ReactiveCommand<int, RxVoid> OpenRawMaterialCommand { get; private set; }

    private readonly Subject<int> _rawMaterialOpenRequests = new();

    /// <summary>The made ingredients Dependents tiles ask to open, by id. The main window subscribes and opens each on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; private set; }

    /// <summary>Starts a new sub-assembly from the one the form shows (<see cref="CopyForm"/>), saved by Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; private set; }

    /// <summary>Gives sub-assemblies another role, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ReactiveCommand<IReadOnlyList<SubAssemblyMove>, RxVoid> MoveSubAssembliesCommand { get; private set; }

    /// <summary>Puts back the roles the last move took the sub-assemblies from (the notice bar's Undo).</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; private set; }

    /// <summary>
    /// The list's New … Here: a new sub-assembly in the folder's role. With no folder (the list's empty space), as New
    /// starts one. Like New, it adds the sub-assembly's row to the list for its name to be typed into (TreeNameEdit).
    /// </summary>
    public ReactiveCommand<SubAssemblyFolderNode?, RxVoid> NewSubAssemblyInCommand { get; private set; }

    /// <summary>The list's Rename: the sub-assembly's name becomes a box to type into, in place, and the name typed is saved at once.</summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> RenameSubAssemblyCommand { get; private set; }

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> DeleteSubAssemblyCommand { get; private set; }

    /// <summary>
    /// Make In-House Ingredient on a recipe of the list: asks for the made ingredient's name and Made category, then adds
    /// that ingredient, made from the recipe, at once. The recipe stays as it is.
    /// </summary>
    public ReactiveCommand<SubAssemblyLeafNode, RxVoid> MakeInHouseIngredientCommand { get; private set; }

    /// <summary>
    /// Input: Make In-House Ingredient's prompt (MadeIngredientPrompt), its name box starting with the sub-assembly's name. Output:
    /// the name typed and the Made category picked, or null if it was cancelled. The View shows a TextPromptWindow with a picker.
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>
    /// The list's New Folder: a "New folder" row for its name to be typed into (TreeNameEdit), and the name typed adds a
    /// role after the others, as Settings → Roles adds one.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> NewRoleCommand { get; private set; }

    /// <summary>The list's Rename Role, typed in place, which keeps the role's place and whether products own it.</summary>
    public ReactiveCommand<SubAssemblyFolderNode, RxVoid> RenameRoleCommand { get; private set; }

    /// <summary>The list's Delete Role, offered only for a role no sub-assembly in the list is in, and never the Packaging role.</summary>
    public ReactiveCommand<SubAssemblyFolderNode, RxVoid> DeleteRoleCommand { get; private set; }

    public ReactiveCommand<RxVoid, RxVoid> ExpandAllCommand { get; private set; }

    public ReactiveCommand<RxVoid, RxVoid> CollapseAllCommand { get; private set; }

    /// <summary>A Dependents tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; private set; }

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
    public ReactiveCommand<RxVoid, RxVoid> NewProductCommand { get; private set; }

    private readonly Subject<int> _newProductRequests = new();

    /// <summary>
    /// The sub-assemblies New Product asks a new product to link, by id. The main window subscribes and starts the new
    /// product on the Products screen, as it opens a Dependents tile's product.
    /// </summary>
    public IObservable<int> NewProductRequests => _newProductRequests;

    /// <summary>What a Save of a recipe products link is asked about with; null in a screen built without it.</summary>
    private readonly SharedRecipeImpact? _recipeImpact;

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

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
        PackagingSet = new SubAssemblyPackagingSet(PackagingMaterials);

        // The subscriptions, in the order they were made: two following one property run in that order, and several run
        // as they're made (WhenAnyValue gives its current value), so each comes after what it reads.
        WireUnsavedChanges();
        WireList();
        WirePicks();
        WireBatch();

        // The commands. Making one runs nothing but its can-execute flag, so they can come in any order.
        WireLoadCommand(roleRepository, rawMaterialRepository);
        WireRecordCommands();
        WireSaveCommand();
        WireListCommands();
        WireBatchCommands();
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
