using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Products screen: a product's recipe (the sub-assemblies it links and new ones not saved yet, each at its own
/// piece size, and its packaging), its costing, its packaging link, and cloning it or making a variant of it. The name, description,
/// recipe, packaging and costing are edited in memory, and Save writes them together
/// (IProductRepository.SaveAsync). A composite product's recipe is the products it holds instead, in its Products tab
/// (<see cref="ProductComponents.ComponentEdits"/>), each bringing its recipe cost. Its parts each hold a piece of it: the
/// list (<see cref="List"/>), the recipe's sections and tabs (<see cref="Recipe"/>), the section form
/// (<see cref="UsageForm"/>), the Packaging tab (<see cref="Packaging"/>), a composite's Products table
/// (<see cref="ComponentsTable"/>), the Costing tab (<see cref="Costing"/>) and the History tab (<see cref="VersionHistory"/>).
/// The screen keeps the form's own fields, which product it shows, loading, Save, the costs, and the undo history over them all.
/// </summary>
public sealed class ProductsViewModel : RecordFormViewModel<ProductFormSnapshot>, IDisposable, IRecordScreen, IRevertScreen,
    IProductsListHost, IProductRecipeHost, IProductPackagingHost, IProductVersionHistoryHost, IProductUsageFormHost, IProductComponentsHost
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, Clone, then History.</summary>
    public const int RecipeTab = 0;

    /// <summary>Recipe 0, Costing 1, then Clone.</summary>
    public const int LinksTab = 2;

    /// <summary>After Clone: the product's versions (ProductVersionHistory). A saved product's only, as Clone is.</summary>
    public const int HistoryTab = 3;

    /// <summary>How a clone's first version names where it came from: "Cloned from Pandesal, version 4".</summary>
    public const string ClonedFrom = "Cloned from";

    /// <summary>How a variant's first version names where it came from: "Variant of Pandesal, version 4".</summary>
    public const string VariantOf = "Variant of";

    private readonly IProductRepository _productRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyRepository _subAssemblyRepository;

    // Reads the product opened here, as Reports and Print read theirs.
    private readonly SavedProductReader _savedProducts;

    // What a LoadCommand reads, apart from its merge into the form (LoadAsync).
    private readonly ProductsLoadReader _loadReader;

    // Save, Clone and Create Variant, each written with the versions it records.
    private readonly VersionedSaves _versionedSaves;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IValidator<ProductCategory> _categoryValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categories;

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>
    /// Each product picked, by the user or for them (Open, a clone made, Save's reload), nothing picked included: what
    /// loads a product into the form. Fed by <see cref="SelectedProduct"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<Product?> _picks = new(null);

    /// <summary>The Category picker's naming of a new category (BeginCategoryEdit, CommitCategoryTextAsync).</summary>
    private readonly CategoryPicker<ProductCategory> _categoryPicker;

    /// <summary>The products each load brings, whose markups the list's flags are worked out from.</summary>
    private readonly Subject<IReadOnlyList<Product>> _markupChecks = new();

    private readonly Subject<PrintScope> _printRequests = new();

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>A product whose versions the History tab should read: pushed as the tab shows, and after a Save.</summary>
    private readonly Subject<int> _versionReads = new();

    /// <summary>
    /// A sub-assembly <see cref="StartNewProductFrom"/> was asked to link, for LoadCommand to start the new product with
    /// once its lists are fresh. New drops it.
    /// </summary>
    private int? _subAssemblyToStartFrom;

    private int _editingId;

    /// <summary>The product whose recipe, packaging, costing and links the form holds, or null for a new product.</summary>
    private int? _detailProductId;

    /// <summary>
    /// The detail Save has read back already, for the load its selection starts to show at once instead of reading it
    /// again (ShowSavedProduct).
    /// </summary>
    private (int ProductId, ProductDetail Detail)? _preloadedDetail;

    /// <summary>Every sub-assembly that could be linked or copied, as last loaded; the section form's LinkableSubAssemblies filters it.</summary>
    private IReadOnlyList<SubAssembly> _linkable = [];

    public ProductsViewModel(
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository,
        ICostingDefaultsRepository costingDefaultsRepository,
        IPackagingLinkRepository packagingLinkRepository,
        IProductCloner productCloner,
        IUnitOfWork unitOfWork,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        ISubAssemblyRoleRepository subAssemblyRoleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialCategoryRepository rawMaterialCategoryRepository,
        IProductCategoryRepository categoryRepository,
        IProductFolderRepository folderRepository,
        IProductVersionRepository versionRepository,
        IValidator<Product> productValidator,
        IValidator<CostingParameters> costingParametersValidator,
        IValidator<ProductCategory> categoryValidator,
        IViewStateStore viewStateStore,
        ILogger logger)
        : base(logger)
    {
        _categoryPicker = new CategoryPicker<ProductCategory>(
            () => EditingCategory, category => AsUndoStep(() => EditingCategory = category), () => Categories, c => c.Name,
            () => CategorySearchText, text => CategorySearchText = text, async typed => await PromptForNewCategory.Handle(typed),
            SaveNewCategoryAsync, RunSafelyAsync, message => SuccessMessage = message, ex => ShowFailure(ex));
        _productRepository = productRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _productCloner = productCloner;
        _subAssemblyRepository = subAssemblyRepository;
        _savedProducts = new SavedProductReader(
            packagingLinkRepository, usageRepository, packagingLineRepository, costingParametersRepository, subAssemblyLineRepository, productRepository,
            costingDefaultsRepository, unitOfWork);
        _loadReader = new ProductsLoadReader(
            productRepository, subAssemblyRoleRepository, categoryRepository, folderRepository, rawMaterialRepository, subAssemblyRepository,
            subAssemblyLineRepository, costingDefaultsRepository, packagingLinkRepository, packagingLineRepository, _savedProducts);

        // The lists the form and its parts read, bound first: the parts take them, as the screen keeps them (each load
        // merges into them rather than replacing them). The categories before the list part's own subscription to them, so
        // the category filter's choices read them changed.
        _productCache.Connect()
            .SortAndBind(out _products, SortExpressionComparer<Product>.Ascending(p => p.Name))
            .Subscribe();
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe();

        Packaging = new ProductPackaging(this, PackagingMaterials, subAssemblyRepository, packagingLineRepository);
        UsageForm = new ProductUsageForm(this, Ingredients);
        ComponentsTable = new ProductComponents(this, Products, _savedProducts);
        VersionHistory = new ProductVersionHistory(
            this, Products, Ingredients, PackagingMaterials, Roles, versionRepository, productRepository, _savedProducts, subAssemblyRepository,
            subAssemblyLineRepository, packagingLineRepository);
        _versionedSaves = new VersionedSaves(
            unitOfWork,
            productRepository,
            versionRepository,
            costingParametersRepository,
            _savedProducts,
            new ProductVersionRecorder(_savedProducts, productRepository, versionRepository));
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        // The parts that hold the recipe and the list. Each subscribes to what it holds alone (the recipe's tabs and which
        // of its tables shows, the list's search and filter), as it's made; what reaches from one part to another, or to
        // the form, is subscribed below, in the order it always ran.
        Recipe = new ProductRecipe(this, UsageForm, ComponentsTable, Ingredients, Roles, subAssemblyLineRepository, rawMaterialCategoryRepository);
        List = new ProductsList(this, _productCache, _categoryCache, Categories, NewProductPlace, productRepository, folderRepository, viewStateStore);

        // What follows the form's fields as they change is called by their setters (SelectionChanged and the rest), and what
        // follows a part's by the part's events, subscribed below. What follows here runs in this order: several run as
        // they're made, WirePicks' included, so each comes after what it reads.

        // The list's row follows the product picked (SelectionChanged), from the start.
        List.SyncTreeSelection();

        // The list's flags for a price short of its target markup, after each load: a newer load cancels one still working.
        LoadLatest(_markupChecks, FlagBelowTargetMarkupAsync);

        WireDetailsTabs();
        WirePicks();
        WireUsageForm();
        WireCosts();
        WireUnsavedChanges();

        // The commands. Making one runs nothing, so they can come in any order.

        // Runs each time the page opens (ScreenBase) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere: a repriced ingredient, or a linked sub-assembly its owner
        // changed.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(LoadAsync));

        // A new product replaces the one showing, so its unsaved changes are asked about first.
        // Each adds the new product's row to the list, for its name to be typed into.
        NewCommand = new ScreenCommand(() => AfterUnsavedChangesAsync(() => List.NewInList()));
        NewCompositeCommand = new ScreenCommand(() => AfterUnsavedChangesAsync(() => List.NewInList(isComposite: true)));

        DeleteCommand = new ScreenCommand(() => DeleteProductAsync(SelectedProduct!), () => SelectedProduct is not null);

        // A clone copies the product as saved, and a variant links it as saved: unsaved edits stay with this one.
        bool CanMakeFromSelected() => SelectedProduct is not null && !string.IsNullOrWhiteSpace(EditingCloneName);
        CloneCommand = new ScreenCommand(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CloneAsync, ClonedFrom), CanMakeFromSelected);
        CreateVariantCommand = new ScreenCommand(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CreateVariantAsync, VariantOf), CanMakeFromSelected);

        // A linked standalone sub-assembly's badge, the Packaging tab's for a packaging set.
        OpenSubAssemblyCommand = ScreenCommand.Of<int>(_subAssemblyOpenRequests.OnNext);

        SaveCommand = new ScreenCommand(() => RunSaveAsync(SaveAsync), () => History.IsDirty);

        // Revert puts the form back as the product was last saved (RevertToSavedAsync), once it's confirmed.
        RevertCommand = new ScreenCommand(() => RunWithConfirmationAsync(
            "Undo every change made since this product was last saved?",
            async () =>
            {
                await RevertToSavedAsync();
                if (SelectedProduct is { } product)
                {
                    SuccessMessage = $"{product.Name} was put back as it was last saved.";
                }
            }));

        // Print shows the product as the screen does, unsaved edits and all. A form left
        // open is part of that, as it's part of what Save writes, so it's applied first; a rule refusing it shows its
        // message, and the Print tab doesn't open. It stays enabled while a picked product's details are on their way,
        // and does nothing if clicked then: disabled for that moment, it flickered on every pick. (Save is off then
        // anyway, with nothing unsaved, so it doesn't flicker.)
        PrintCommand = new ScreenCommand(
            () => RunSafelyAsync(() =>
            {
                if (!AreDetailsReady)
                {
                    return;
                }

                // On the History tab it prints the version picked there (PreparePrintout), so the form isn't applied: its
                // pages aren't the form's.
                if (SelectedDetailsTab == HistoryTab)
                {
                    if (VersionHistory.SelectedRow is null)
                    {
                        ErrorMessage = "Pick a version in the History tab's table to print it.";
                        return;
                    }
                }
                else
                {
                    Recipe.ApplyOpenForm();
                }

                _printRequests.OnNext(new PrintScope.ThisProduct());
            }));

        ToggleCostBasisCommand = ScreenCommand.Of(ToggleCostBasis);
    }

    /// <summary>The list: every product in its stage and the user's folders, searched, filtered, its menus, moves and folders.</summary>
    public ProductsList List { get; }

    /// <summary>The recipe: its sections and their tabs, the Packaging tab and a composite's Products tab, and the section form's flow.</summary>
    public ProductRecipe Recipe { get; }

    /// <summary>The Costing tab: the costing percentages, each metric's amount, and the cost summary (Costs).</summary>
    public ProductCosting Costing { get; } = new();

    /// <summary>
    /// The Recipe Structure's Packaging tab, which every product has: the packaging lines for each piece, typed in or what
    /// they link, Link and Copy, Move to Sub-Assemblies, and the bulk packaging.
    /// </summary>
    public ProductPackaging Packaging { get; }

    /// <summary>A composite's Products table: its lines, the add row, the product cell's options, and what each product brings.</summary>
    public ProductComponents ComponentsTable { get; }

    /// <summary>The add/edit sub-assembly form: the section it edits or adds, its fields and tables, and what it shows.</summary>
    public ProductUsageForm UsageForm { get; }

    /// <summary>
    /// The History tab: the product's versions, read while the tab shows (<see cref="RequestVersions"/>), and its Restore,
    /// which the screen puts into the form (IProductVersionHistoryHost.ApplyVersion).
    /// </summary>
    public ProductVersionHistory VersionHistory { get; }

    /// <summary>Every product in use (none of the deleted), for the packaging link and clone pickers.</summary>
    public ReadOnlyObservableCollection<Product> Products => _products;

    /// <summary>
    /// The products in the list's Deleted folder, by name (IProductRepository.GetDeletedAsync). Kept apart from
    /// <see cref="Products"/>, so no picker offers one; only the list shows them, to restore or delete permanently.
    /// </summary>
    public IReadOnlyList<Product> DeletedProducts { get; private set; } = [];

    private IReadOnlyList<ProductFolder> _folders = [];
    /// <summary>
    /// The Products list's folders (ProductFolder), in both stages, as last read: the user's own, holding products and other
    /// folders, apart from the categories. A new product started in one of them goes to its stage with it, should it have
    /// moved to the other since (NewProductPlace.FoldersRead), as Save would file it there; the header names its place as it
    /// is now (NewProductTag).
    /// </summary>
    public IReadOnlyList<ProductFolder> Folders
    {
        get => _folders;
        private set
        {
            NewProductPlace.FoldersRead(value);
            RaiseAndSetIfChanged(ref _folders, value);
        }
    }

    /// <summary>Where Save files a new product: its stage and folder, as New, New Product Here and Settings decide them.</summary>
    public NewProductPlace NewProductPlace { get; } = new();

    /// <summary>What the Category picker lists. A name that isn't one of them is added by typing it (CommitCategoryTextAsync).</summary>
    public ReadOnlyObservableCollection<ProductCategory> Categories => _categories;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only, for a sub-assembly's lines: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    private bool _isEditingComposite;
    /// <summary>
    /// The product showing is a composite: made of other products (its Products tab), its own packaging, and sections only
    /// in product-owned roles (a topping, a filling), with no Quantity Required of its own. Picked by New composite product, and fixed once it's saved.
    /// </summary>
    public bool IsEditingComposite
    {
        get => _isEditingComposite;
        private set => RaiseAndSetIfChanged(ref _isEditingComposite, value);
    }

    /// <summary>The product showing is made of sub-assemblies, as every product that isn't a composite is.</summary>
    public bool IsEditingSingle => !IsEditingComposite;

    private Product? _selectedProduct;
    /// <summary>
    /// The product the form is about. Setting it is a pick, which loads that product into the form (_picks); a load that
    /// only finds a fresh copy of it re-points it quietly (<see cref="RepointSelection"/>), leaving the form and whatever's
    /// typed into it alone.
    /// </summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedProduct, value))
            {
                SelectionChanged();
                _picks.OnNext(value);
            }
        }
    }

    /// <summary>
    /// What follows the product showing, a pick's or a load's fresh copy: one opened, by its Save included, ends the new
    /// product's row, since the form no longer shows the new one; the list's row follows it; a hidden tab can't stay the one
    /// showing, so leaving a saved product while on Clone or History goes back to Recipe; and the History tab reads its
    /// versions.
    /// </summary>
    private void SelectionChanged()
    {
        if (SelectedProduct is not null)
        {
            List.EndNewRow();
        }

        List.SyncTreeSelection();
        if (SelectedProduct is null && SelectedDetailsTab is LinksTab or HistoryTab)
        {
            SelectedDetailsTab = RecipeTab;
        }

        RequestVersions();
    }

    private bool _areDetailsReady = true;
    /// <summary>
    /// The details showing are the picked product's. False from picking another product until its details
    /// arrive (ResetDetails to LoadProductDetailAsync): the previous product's stay showing meanwhile, so no
    /// empty product is drawn between the two, but they're no longer the ones the form is about, so nothing
    /// may act on them (the details tabs ignore the mouse, and Save does nothing).
    /// </summary>
    public bool AreDetailsReady
    {
        get => _areDetailsReady;
        private set
        {
            if (RaiseAndSetIfChanged(ref _areDetailsReady, value))
            {
                RequestVersions();
            }
        }
    }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingDescription = string.Empty;
    public string EditingDescription
    {
        get => _editingDescription;
        set => RaiseAndSetIfChanged(ref _editingDescription, value);
    }

    private int _editingQuantityRequired = 1;
    /// <summary>
    /// How many finished pieces make one sellable unit (Product.QuantityRequired): 1 for a product sold by the
    /// piece, 8 for a "Box of 8". The recipe stays per piece; every cost and price on this screen is for one
    /// whole unit, so this multiplies the sub-assemblies' share of it.
    /// </summary>
    public int EditingQuantityRequired
    {
        get => _editingQuantityRequired;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingQuantityRequired, value))
            {
                _updates.Run(RecomputeCosts);
            }
        }
    }

    private decimal? _editingCurrentPrice;
    /// <summary>What the shop charges for one sellable unit now, with VAT (Product.CurrentPrice); null when not entered.</summary>
    public decimal? EditingCurrentPrice
    {
        get => _editingCurrentPrice;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingCurrentPrice, value))
            {
                _updates.Run(RecomputeCosts);
            }
        }
    }

    private string _categorySearchText = string.Empty;
    /// <summary>
    /// What's typed into the Category picker (bound to its Text): settled into a category, or a new one, once the
    /// picker is left (CommitCategoryTextAsync).
    /// </summary>
    public string CategorySearchText
    {
        get => _categorySearchText;
        set => RaiseAndSetIfChanged(ref _categorySearchText, value);
    }

    private ProductCategory? _editingCategory;
    /// <summary>The product's category, or null for none: a product doesn't need one. It can set the Wastage % the product is priced with.</summary>
    public ProductCategory? EditingCategory
    {
        get => _editingCategory;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingCategory, value))
            {
                _updates.Run(RecomputeCosts);
            }
        }
    }

    /// <summary>The header's title, and the usage example's: the product's name, or "New product".</summary>
    public string HeaderTitle =>
        SelectedProduct is null && string.IsNullOrWhiteSpace(EditingName) ? (IsEditingComposite ? "New composite product" : "New product") : EditingName;

    /// <summary>
    /// The tag beside the header's title while the form holds a product not saved yet: where Save will file it, "Goes in
    /// Production" or "Goes in Innovation", its folder's path as it is now (renamed or moved since New Product Here, it says
    /// so). Null while a saved product is showing.
    /// </summary>
    public string? NewProductTag =>
        SelectedProduct is null ? $"Goes in {ProductFolderRules.PathTitle(Folders, NewProductPlace.Stage, NewProductPlace.FolderId)}" : null;

    /// <summary>When the product was last saved, e.g. "Sep 14, 2026, 6:27 PM", or "—".</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    /// <summary>
    /// The details tab showing. Moves to Recipe when Clone is about to hide (SelectionChanged); the History tab reads the
    /// product's versions as it shows.
    /// </summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedDetailsTab, value))
            {
                RequestVersions();
            }
        }
    }

    /// <summary>A saved product: links and clones need one, so the tab only shows then.</summary>
    public bool ShowLinks => SelectedProduct is not null;

    private string _editingCloneName = string.Empty;
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

    public ScreenCommand LoadCommand { get; }

    public ScreenCommand NewCommand { get; }

    /// <summary>New's New composite product: a new product made of other products, with its own packaging and toppings or fillings.</summary>
    public ScreenCommand NewCompositeCommand { get; }

    public ScreenCommand SaveCommand { get; }

    /// <summary>
    /// Revert: reads the product back as it was last saved, throwing away everything staged since — the details, the
    /// recipe's sections and their lines, the packaging, the packs and the costing. The sub-assembly form has no
    /// discard of its own, so this is what abandons a section being added or edited, along with the rest.
    /// </summary>
    public ScreenCommand RevertCommand { get; }

    /// <summary>
    /// Print: asks for the Print tab (<see cref="PrintRequests"/>), which shows the product's recipe and costing as the
    /// screen shows them, unsaved edits included (<see cref="PreparePrintout"/>). A sub-assembly form left open is applied first, as Save applies it, so a rule
    /// refusing it shows its message here and the tab doesn't open.
    /// </summary>
    public ScreenCommand PrintCommand { get; }

    /// <summary>Unbuffered: switches every cost on the screen between the materials' buffered costs and their true ones (ProductCosting.CostBasis).</summary>
    public ScreenCommand ToggleCostBasisCommand { get; }

    /// <summary>
    /// What a Print here asks to print: the header's, This product; a folder's menu, the folder; the list's, all products.
    /// ScreenLinks opens the Print screen with that choice (PrintTabViewModel.Pick), and that screen asks
    /// <see cref="PreparePrintout"/> for This product.
    /// </summary>
    public IObservable<PrintScope> PrintRequests => _printRequests;

    /// <summary>
    /// A linked standalone sub-assembly's badge clicked (a recipe section's, or linked packaging's for a packaging set):
    /// asks for the sub-assembly with this id to be opened, through <see cref="SubAssemblyOpenRequests"/>.
    /// </summary>
    public ScreenCommand<int> OpenSubAssemblyCommand { get; }

    /// <summary>
    /// The sub-assemblies linked badges ask to open, by id. ScreenLinks subscribes and opens each on the Sub-Assemblies
    /// screen, as Sub-Assemblies' Dependents tiles open their products here.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    public ScreenCommand DeleteCommand { get; }

    public ScreenCommand CloneCommand { get; }

    /// <summary>Create Variant on the Clone tab: a new product that links this one's recipe and packaging (IProductCloner.CreateVariantAsync).</summary>
    public ScreenCommand CreateVariantCommand { get; }

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with.
    /// Output: the name to add it as, or null if it was cancelled. The page answers it with a dialog (Prompts).
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

    /// <summary>The selection pointed at <paramref name="fresh"/>, a load's copy of the product picked: shown, not picked again.</summary>
    private void RepointSelection(Product? fresh)
    {
        if (RaiseAndSetIfChanged(ref _selectedProduct, fresh, nameof(SelectedProduct)))
        {
            SelectionChanged();
        }
    }

    /// <summary>
    /// Flags each product in the list whose current price earns less than its target markup: every product
    /// priced as saved, as Reports prices them (SavedProductReader.ReadAllAsync, a dozen or so queries however many there are),
    /// off the UI thread. A product's flag follows its Save at the next load.
    /// </summary>
    private async Task FlagBelowTargetMarkupAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        var saved = await Task.Run(() => _savedProducts.ReadAllAsync(products, CostBasis.Buffered, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var below = saved.Where(s => s.Costs.IsBelowTargetMarkup).Select(s => s.Product.Id).ToHashSet();
        foreach (var product in products)
        {
            if (List.ProductTree.Find(product.Id) is { } leaf)
            {
                leaf.IsBelowTargetMarkup = below.Contains(product.Id);
            }
        }
    }

    /// <summary>The products other than this one that use <paramref name="subAssemblyId"/>, by name.</summary>
    private List<string> OtherProductsUsing(int subAssemblyId, IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> dependents) =>
        [.. dependents.GetValueOrDefault(subAssemblyId, [])
            .Where(p => p.Id != _detailProductId)
            .Select(p => p.Name)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The versions the History tab reads.</summary>
    private void WireDetailsTabs()
    {
        // The History tab reads the product's versions while it shows, once per product: as it's picked, and as another
        // product's details arrive under it (SelectedDetailsTab, AreDetailsReady, SelectionChanged). A Save reads them
        // again (RequestVersions(force: true)).
        RequestVersions();
        LoadLatest(_versionReads, (productId, cancellationToken) => VersionHistory.ReadAsync(
            productId, () => SelectedProduct?.Id == productId, cancellationToken));
    }

    /// <summary>A product picked in the list loads into the form.</summary>
    private void WirePicks()
    {
        // A pick loads the product into the form, in these three steps, in this order. Only a pick does (_picks): a load
        // re-points the selection at its fresh copy quietly, so nothing here has to tell the two apart.
        // Another product's name and price land in the form ahead of its details, so its history goes first, as
        // ClearDetailsForAnother would take it anyway: measured against the previous product's saved form, they read
        // as unsaved changes, and Save lit up until the details arrived — a flicker on every pick.
        _picks
            .Where(product => product is not null)
            .Subscribe(product =>
            {
                if (product!.Id != _detailProductId)
                {
                    History.Clear();
                }

                LoadIntoForm(product);
            });

        _picks.Subscribe(ResetDetails);

        // A newer pick cancels an older one still loading; one that fails shows why, and the next pick loads as usual.
        LoadLatest(_picks, LoadProductDetailAsync);
    }

    /// <summary>
    /// The section form wired to the recipe: its way in, its lines priced as they're typed, and the sub-assembly picked to
    /// link or copy.
    /// </summary>
    private void WireUsageForm()
    {
        // The sub-assembly form: where its sub-assembly comes from, then the one it links or copies. The form starts
        // again empty in its new way, which is nothing to save yet, as a fresh add form isn't. Picks only
        // (ProductUsageForm.ModePicks and the rest): an undo putting the form back, or the options rebuilt, isn't one.
        UsageForm.ModePicks
            .Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage)
            .Subscribe(_ =>
            {
                Recipe.StartUsageFormMode();
                Recipe.RebaseSettledForm();
            });

        // A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.
        UsageForm.FormLines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .AutoRefresh(l => l.IsFormulaBase)
            .Subscribe(_ => _updates.Run(Recipe.FormLinesEdited));

        // An add-in picked, typed or deleted: it's priced, and the batch it joins follows.
        UsageForm.FormAddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => _updates.Run(Recipe.FormAddInsEdited));

        // The preview, and whether Save moves a new one to the Sub-Assemblies screen, follow the form's mode, role and source.
        UsageForm.KindChanged += () => _updates.Run(Recipe.FormKindChanged);
        _updates.Run(Recipe.FormKindChanged);

        LoadLatest(
            UsageForm.SourcePicks.Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage),
            Recipe.LoadUsageSourceAsync);

        // A sub-assembly picked to link instead relinks the section once its lines are in; a later pick overtakes it.
        LoadLatest(
            UsageForm.LinkTargetPicks
                .Where(target => target is not null && UsageForm.IsChangingLink)
                .Select(target => target!),
            Recipe.ChangeLinkAsync);
    }

    /// <summary>
    /// What reprices the product (its packaging, a composite's products, the costing percentages, its price, quantity
    /// and category), and the captions naming its figures.
    /// </summary>
    private void WireCosts()
    {
        // A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.
        Packaging.LineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(PackagingLinesEdited));

        // The same for a bulk packaging line, and the products a bulk pack holds.
        Packaging.BulkQuantityChanged += () => _updates.Run(PackagingLinesEdited);
        Packaging.BulkEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(PackagingLinesEdited));

        // A composite's line picked, typed or deleted in its Products table: it's priced once its product's recipe cost
        // has been read (a new pick's is read then), and the composite's costs follow.
        //
        // One refresh operator for all four properties, not an AutoRefresh each: each operator takes its own lock, and the
        // subscriber sets the rows' costs (Resolve), so with a chain a cost set on one thread could hold one operator's lock
        // while waiting for the next, and a cost set on another thread the other way round. One operator
        // can't deadlock, whatever thread a cost is set on, and is the simpler way to say it.
        ComponentsTable.ComponentEdits.ToObservableChangeSet()
            .AutoRefreshOnObservable(r => r.WhenAnyPropertyChanged(
                nameof(ProductComponentRow.ComponentProductId),
                nameof(ProductComponentRow.Quantity),
                nameof(ProductComponentRow.RecipeCost),
                nameof(ProductComponentRow.PackagingCost)))
            .Subscribe(_ => _updates.Run(ComponentsEdited));

        // The costing percentages, and a composite's products' packaging joining its own or leaving it. The form's price,
        // quantity and category too, through their setters.
        Costing.PercentEdited += () => _updates.Run(RecomputeCosts);
        ComponentsTable.IncludesPackagingChanged += () => _updates.Run(RecomputeCosts);

        // Once now, from the blank form, as everything above it follows.
        _updates.Run(RecomputeCosts);
    }

    /// <summary>Save offers itself whenever the form holds something the saved product doesn't.</summary>
    private void WireUnsavedChanges()
    {
        // Save offers itself only when the form holds something the saved product doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        PropertyChanged += (_, _) => History.RefreshDirty();
        Recipe.PropertyChanged += (_, _) => History.RefreshDirty();
        Costing.PropertyChanged += (_, _) => History.RefreshDirty();
        Packaging.PropertyChanged += (_, _) => History.RefreshDirty();
        ComponentsTable.PropertyChanged += (_, _) => History.RefreshDirty();
        UsageForm.PropertyChanged += (_, _) => History.RefreshDirty();
        WatchForSave(Recipe.UsageEdits);
        WatchForSave(Packaging.LineEdits);
        WatchForSave(Packaging.BulkEdits);
        WatchForSave(ComponentsTable.ComponentEdits);
        WatchForSave(UsageForm.FormLines);
        WatchForSave(UsageForm.FormAddIns);
        ((INotifyPropertyChanged)Recipe.RecipeTabs).PropertyChanged += (_, _) => Recipe.RebaseSettledForm();
    }

    /// <summary>Load: everything the screen shows is read again, and merged in.</summary>
    private async Task LoadAsync()
    {
        // A linked section reads a sub-assembly kept on the Sub-Assemblies screen, which can change there (its
        // lines, name or role), so those are fetched again; the product's own and new sections only need their
        // ingredients' new costs. Every saved one is fetched: a role made product-owned, or no longer, in Settings
        // turns a linked one into the product's own, or back.
        var detailProductId = _detailProductId;
        HashSet<int> linkedIds = [.. Recipe.UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId)];

        // A composite's products can have changed elsewhere since they were read (a recipe, an ingredient's price, a
        // quantity required), so what each brings is read again; its lines may hold unsaved edits and stay as they are.
        List<int> componentIds = [.. ComponentsTable.ProductIds];

        // Everything at once, on the thread pool (ProductsLoadReader), touching no ViewModel state.
        var load = await _loadReader.ReadAsync(detailProductId, linkedIds, componentIds);

        // Linked packaging is kept elsewhere (another product, or a set on the Sub-Assemblies screen) and can
        // have changed there, so it's fetched again — but only while the form still shows the link as saved. The
        // product's own lines aren't, since they may hold unsaved edits, and nor is a link picked but not saved.
        var savedLink = load.PackagingLink is { } packagingLink ? PackagingLinkTarget.From(packagingLink) : null;
        var showsSavedLink = savedLink is not null && Packaging.ShowsSavedLink(savedLink);
        var linkedPackagingLines = showsSavedLink
            ? await _loadReader.ReadLinkedPackagingAsync(load.PackagingLink!.ProductId)
            : null;

        // A role made product-owned in Settings, or no longer, since the product was loaded: a section it linked is its
        // own now, or one of its own is linked now. With nothing typed into the form, the product is read again, so what
        // Save compares against is how it's saved now; with something typed, RefreshRows turns the section round
        // in place, and the next Save writes it as it shows.
        var ownershipChanged = Recipe.UsageEdits.Any(u => load.LinkedSubAssemblies.FirstOrDefault(s => s.Id == u.SubAssemblyId) is { } saved
            && (saved.OwnerProductId is not null && saved.OwnerProductId == detailProductId) != u.IsOwned);
        var reloadDetails = ownershipChanged && !History.IsDirty && detailProductId is not null;

        // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
        var selectedId = SelectedProduct?.Id;
        var categoryId = EditingCategory?.Id;
        var usageRoleId = UsageForm.EditingUsageRole?.Id;
        var usageSourceKey = UsageForm.EditingUsageSource?.Key;

        ApplyRefresh(() =>
        {
            Roles.MergeFrom(load.Roles, r => r.Id);
            _categoryCache.MergeFrom(load.Categories);
            EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
            Ingredients.MergeFrom([.. load.RawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
            PackagingMaterials.MergeFrom([.. load.RawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
            // The Deleted folder and the folders first, so the tree, refreshed as the products merge, files a product
            // deleted, restored or moved since in its new place in one go. The tree is refreshed again for a change to
            // those alone.
            DeletedProducts = load.DeletedProducts;
            Folders = load.Folders;
            _productCache.MergeFrom(load.Products);
            List.RefreshTree();
            _markupChecks.OnNext(load.Products);
            _linkable = load.Linkable;
            Packaging.TakePackagingSets(load.PackagingSets);
            RefreshCostingDefaults(load.CostingDefaults);

            // A composite's lines point at the fresh products (a rename shows) at their fresh recipe costs.
            ComponentsTable.TakeFresh(load.ComponentCosts, load.ComponentLinks);
            RefreshComponentOptions();

            RepointSelection(Products.FirstOrDefault(p => p.Id == selectedId));
            if (SelectedProduct is not null)
            {
                ShowUpdatedAt(SelectedProduct.UpdatedAt);
            }

            if (detailProductId == _detailProductId && linkedPackagingLines is not null)
            {
                // A renamed product or set reads right in the banner too.
                Packaging.ShowLink(savedLink);
                Packaging.LoadLines(linkedPackagingLines);
            }

            Recipe.RefreshRows(load.LinkedSubAssemblies, load.LinkedLines);

            // The packaging lines at their materials' refreshed costs and units, in place, so a line being typed isn't lost.
            Packaging.ResolveRows();
            UsageForm.ResolveRows();
            Recipe.RefreshLinkableOptions();

            // An add form opened before the roles were loaded takes the first role it can have, as it would have.
            UsageForm.ShowRole(UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usageRoleId)
                ?? (usageRoleId is null && UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage ? UsageForm.FormRoleOptions.FirstOrDefault() : null));
            UsageForm.ShowSource(UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == usageSourceKey));

            UsageForm.Recompute();
            RecomputeCosts();
        });

        if (reloadDetails && SelectedProduct is { } reloaded && reloaded.Id == detailProductId)
        {
            await LoadProductDetailAsync(reloaded, CancellationToken.None);
        }

        // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the product's form.
        OpenRecordAskedFor();

        if (_subAssemblyToStartFrom is { } subAssemblyId)
        {
            _subAssemblyToStartFrom = null;
            StartNewProductLinking(subAssemblyId);
        }

        // A save elsewhere can have recorded a version of this product since (a sub-assembly it links, saved on the
        // Sub-Assemblies screen): a History tab showing reads its versions again, and one hidden when it next shows.
        if (SelectedDetailsTab == HistoryTab)
        {
            RequestVersions(force: true);
        }
        else
        {
            VersionHistory.Clear();
        }
    }

    /// <summary>
    /// Makes a product named <paramref name="name"/> from <paramref name="sourceProductId"/> as saved (a clone or a
    /// variant) with its first version, noted "<paramref name="madeAs"/>" the source, both or neither
    /// (VersionedSaves.MakeProductFromAsync), and opens it. Opening it replaces the product showing, so that one's unsaved
    /// changes are asked about first: saved, they're in what's copied.
    /// </summary>
    private async Task MakeFromAsync(int sourceProductId, string name, Func<int, string, CancellationToken, Task<int>> make, string madeAs)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var newId = await Task.Run(() => _versionedSaves.MakeProductFromAsync(
                sourceProductId, name, make, madeAs, DateTime.Now, CancellationToken.None));

            await LoadCommand.ExecuteAsync();
            SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
        });
    }

    /// <summary>
    /// Moves <paramref name="product"/> to the Deleted folder once it's confirmed. The form starts a new product if it was
    /// showing this one, as it was after the header's Delete, which only ever deletes the product showing.
    /// </summary>
    private Task DeleteProductAsync(Product product) =>
        RunWithConfirmationAsync(
            $"Delete '{product.Name}'? It moves to the Deleted folder at the bottom of the list, where it can be restored as it is.",
            async () =>
            {
                await _productRepository.DeleteAsync(product.Id, CancellationToken.None);
                if (SelectedProduct?.Id == product.Id || _editingId == product.Id)
                {
                    StartNewProduct();
                }

                await LoadCommand.ExecuteAsync();
            });

    /// <summary>
    /// Takes <paramref name="product"/> out of the Deleted folder, back to its own, and opens it, as a click on it would,
    /// asking about the unsaved changes of the product showing first.
    /// </summary>
    private async Task RestoreProductAsync(Product product)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _productRepository.RestoreAsync(product.Id, CancellationToken.None);
            await LoadCommand.ExecuteAsync();
            TrySelectRecord(product.Id);
        });
    }

    /// <summary>Deletes <paramref name="product"/>, from the Deleted folder, for good once it's confirmed.</summary>
    private Task DeletePermanentlyAsync(Product product) =>
        RunWithConfirmationAsync(
            product.IsComposite
                ? $"Delete '{product.Name}' permanently? The products it holds stay. This can't be undone."
                : $"Delete '{product.Name}' permanently? Its own sub-assemblies go with it; the ones it links stay on the Sub-Assemblies screen. This can't be undone.",
            async () =>
            {
                await _productRepository.DeletePermanentlyAsync(product.Id, CancellationToken.None);
                await LoadCommand.ExecuteAsync();
            });

    /// <summary>The Last Updated figure: the day and the time; "—" for a product never saved since it was added.</summary>
    private void ShowUpdatedAt(DateTime? updatedAt) =>
        LastUpdatedText = ProductPrintout.LastUpdatedTextFor(updatedAt);

    private void LoadIntoForm(Product product)
    {
        // Not for the same product loaded again, as a Save does: the boxes still hold what's its.
        if (product.Id != _editingId)
        {
            FormVersion++;
        }

        _editingId = product.Id;
        EditingName = product.Name;
        EditingDescription = product.Description ?? string.Empty;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
        EditingQuantityRequired = product.QuantityRequired;
        ComponentsTable.IncludesPackaging = product.IncludesComponentPackaging;
        EditingCurrentPrice = product.CurrentPrice;
        ShowUpdatedAt(product.UpdatedAt);
        EditingCloneName = $"{product.Name} Copy";
    }

    /// <summary>
    /// What follows a new selection: nothing picked or open. Another product's details wait for its own to
    /// arrive and go with them (AreDetailsReady); the same product picked again (a fresh copy after Save) keeps
    /// showing until its reload, and none picked clears them.
    /// </summary>
    private void ResetDetails(Product? product)
    {
        // Another product's details replace these when they arrive, in the same pass (ClearDetailsForAnother).
        // Cleared here, the screen drew an empty product in between: a flicker, with the recipe's frame
        // collapsing and growing back.
        if (product is not null && product.Id != _detailProductId)
        {
            AreDetailsReady = false;
            return;
        }

        AreDetailsReady = true;
        Recipe.CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;

        if (product?.Id != _detailProductId)
        {
            ClearDetails();
        }
    }

    /// <summary>
    /// What ResetDetails leaves for the picked product's details to replace: the previous product's. Its recipe's tabs
    /// stay for the picked recipe's sections to take over (ProductRecipe.ShowSaved), unless nothing is coming to replace them.
    /// </summary>
    private void ClearDetailsForAnother(bool keepRecipeTabs)
    {
        Recipe.CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        VersionHistory.Clear();
        ClearDetails(keepRecipeTabs);
    }

    /// <summary>
    /// Starts a new product that links a standalone sub-assembly, for Sub-Assemblies' New Product. It waits for the next
    /// LoadCommand, which showing the screen runs, so the sub-assembly is found as it's saved now: it may have been made or
    /// renamed there since this screen last loaded.
    /// </summary>
    public void StartNewProductFrom(int subAssemblyId)
    {
        ForgetRecordToOpen();
        _subAssemblyToStartFrom = subAssemblyId;
    }

    /// <summary><see cref="StartNewProductFrom"/>, asking first about the unsaved changes of the product showing. False when the user stays on it.</summary>
    public async Task<bool> StartNewProductFromAsync(int subAssemblyId)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        StartNewProductFrom(subAssemblyId);
        return true;
    }

    /// <summary>
    /// A new product named after the sub-assembly, with the Link form open on it, as Add Sub-Assembly's Link an existing
    /// one would have it: its lines load, and the piece size is left to type. Like New, it replaces whatever the form
    /// showed and has nothing to undo; it's measured against a blank product, so Save is offered straight away.
    /// </summary>
    private void StartNewProductLinking(int subAssemblyId)
    {
        if (_linkable.FirstOrDefault(s => s.Id == subAssemblyId && s.OwnerProductId is null && s.Role?.IsPackaging != true) is not { } subAssembly)
        {
            ErrorMessage = "That sub-assembly is no longer on the Sub-Assemblies screen.";
            return;
        }

        StartNewProduct();
        EditingName = subAssembly.Name;
        Recipe.OpenAddSubAssemblyTab();
        UsageForm.EditingUsageMode = UsageFormMode.Link;
        UsageForm.EditingUsageSource = UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.SubAssembly?.Id == subAssemblyId);
        History.RefreshDirty();
    }

    protected override int? FindRecordByName(string text) => RecordNameMatch.Best(Products, product => product.Name, text)?.Id;

    /// <summary>Any product but the one showing, and not a deleted one: that's only picked in the Deleted folder, and replaces nothing.</summary>
    protected override bool ReplacesShowing(int id) => id != SelectedProduct?.Id && DeletedProducts.All(p => p.Id != id);

    protected override bool TrySelectRecord(int id)
    {
        // A deleted one (a Dependents tile names it as deleted) has its row picked in the Deleted folder, opened; the form stays.
        if (DeletedProducts.Any(p => p.Id == id))
        {
            if (List.ProductTree.Find(id) is null)
            {
                List.ClearListFilters();
            }

            List.ProductTree.DeletedFolder.IsExpanded = true;
            List.SelectedTreeNode = List.ProductTree.Find(id);
            return true;
        }

        if (Products.FirstOrDefault(p => p.Id == id) is not { } product)
        {
            return false;
        }

        if (!List.VisibleProducts.Contains(product))
        {
            List.ClearListFilters();
        }

        SelectedProduct = product;
        return true;
    }

    /// <summary>
    /// New, from the header or the list. A product still waiting for LoadCommand to open it, or a sub-assembly waiting
    /// for it to start a product from, no longer is: it would load over the new form, and a name typed into it since
    /// would then be saved as that product's.
    /// </summary>
    private void NewProduct(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        ForgetRecordToOpen();
        _subAssemblyToStartFrom = null;
        StartNewProduct(stage, folderId, isComposite);
    }

    /// <summary>
    /// A new product: no name, recipe, packaging or links, its costing at the defaults, and nothing selected. Save files it
    /// in <paramref name="stage"/>, or Settings' stage for a new product when it's null, in the user's folder
    /// <paramref name="folderId"/>, or at the top of the stage for none; it has no category until one is picked.
    /// <paramref name="isComposite"/> makes it a composite, which opens on its empty Products table.
    /// </summary>
    private void StartNewProduct(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        NewProductPlace.StartNew(stage, folderId);
        SelectedProduct = null;
        FormVersion++;
        _editingId = 0;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        EditingCategory = null;
        EditingQuantityRequired = 1;
        ComponentsTable.IncludesPackaging = false;
        EditingCurrentPrice = null;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run. The kind goes
        // first, so the tab ClearDetails picks is the new kind's.
        Recipe.CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        SetComposite(isComposite);
        ClearDetails();
        RefreshComponentOptions();
        Costing.ApplyDefaults();

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// Makes the form a composite's, with its Products tab first, before its sections' tabs, or a product of
    /// sub-assemblies', without it.
    /// </summary>
    private void SetComposite(bool isComposite)
    {
        IsEditingComposite = isComposite;
        Recipe.ShowComponentsTab(isComposite);
    }

    /// <summary>What the Products table's product cell offers, for the product showing (a new one can hold any).</summary>
    private void RefreshComponentOptions() => ComponentsTable.RefreshOptions(_detailProductId ?? 0, Products);

    /// <summary>
    /// Empties the recipe, packaging, costing and links, and what Save compares them against. With
    /// <paramref name="keepRecipeTabs"/>, the recipe's sections and the Packaging tab stay for another product's to
    /// replace in the same pass: the tab strip then keeps its tabs rather than building a new one for each.
    /// </summary>
    private void ClearDetails(bool keepRecipeTabs = false)
    {
        // The history is this product's form; another product's steps would put back a recipe that isn't its.
        History.Clear();

        _detailProductId = null;

        Recipe.Clear(keepRecipeTabs);
        Packaging.Clear();

        // A composite's lines, and what its products were read to bring, are that composite's.
        ComponentsTable.Clear();
        Costing.Load(null);
        Recipe.RefreshLinkableOptions();
        RecomputeCosts();

        // A picked product's sub-assemblies are on their way, and pick the tab when they arrive; a blank form opens on its
        // first tab, a composite's on its Products, whatever showed before.
        Recipe.AfterClear(forAnother: SelectedProduct is not null);
    }

    private async Task LoadProductDetailAsync(Product? product, CancellationToken cancellationToken)
    {
        if (product is null)
        {
            // Nothing picked: the form is a blank one, and there's nothing to save until it's typed into.
            History.MarkClean();
            return;
        }

        // Read on the thread pool, so the UI thread comes back once, when all of it has arrived, instead of
        // after each read to start the next. The reads touch no ViewModel state; everything below them does.
        // Save's reload has read it already: then this runs through without awaiting, in the pass that
        // picked the product, so the form isn't drawn closed (ResetDetails) before it opens again.
        ProductDetail detail;
        try
        {
            detail = _preloadedDetail is { } preloaded && preloaded.ProductId == product.Id
                ? preloaded.Detail
                : await Task.Run(() => _savedProducts.ReadDetailAsync(product.Id, cancellationToken), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && product.Id != _detailProductId)
        {
            // Nothing of the picked product's came: the previous product's mustn't stay showing under its name.
            ClearDetailsForAnother(keepRecipeTabs: false);
            AreDetailsReady = true;
            throw;
        }

        // Only the product picked last is shown; a newer pick cancels this load.
        cancellationToken.ThrowIfCancellationRequested();

        // Out with the previous product's details and in with these, in one pass, so nothing is drawn between them. Its
        // recipe's tabs stay, for this recipe's sections to take over.
        if (product.Id != _detailProductId)
        {
            ClearDetailsForAnother(keepRecipeTabs: true);
        }

        // Whose details these are, before they're ready: the History tab reads the versions as AreDetailsReady turns true,
        // only when they're the picked product's (RequestVersions). Set after, a product picked on that tab showed none.
        _detailProductId = product.Id;
        AreDetailsReady = true;

        // A composite's Products tab (or none) goes in before the tabs are rebuilt, and its lines before the recipe
        // settles what Save compares it against, which they're part of.
        SetComposite(product.IsComposite);
        ComponentsTable.Load(detail.Components);

        // The packaging first, so the recipe's tabs, rebuilt next, can reselect the Packaging tab if it showed.
        Packaging.LoadLink(detail.PackagingLink);
        Packaging.LoadLines(detail.Lines);
        Packaging.LoadBulk(detail.BulkLines, product.BulkPackagingQuantity);
        Recipe.ShowSaved(detail.Usages, detail.SubAssemblyLines, detail.AddIns);
        Costing.Load(detail.Costing);
        Recipe.RefreshLinkableOptions();
        RefreshComponentOptions();
        RecomputeCosts();
        History.MarkClean();

        // Read and priced all the same, so it can be put right here; Save refuses it until one product is taken out of another.
        if (detail.HoldsItself)
        {
            ErrorMessage = CompositeProductRules.HoldsItselfText(product.Name);
        }
        else if (detail.PackagingLinkedRoundInALoop)
        {
            ErrorMessage = PackagingLinkRules.LinkedRoundInALoopText(product.Name);
        }
    }

    /// <summary>Save: the product with its recipe, packaging and costing, and the versions it records, all or nothing.</summary>
    private async Task SaveAsync()
    {
        // The picked product's details haven't replaced the previous one's yet: saving now would write that recipe
        // under this product's name. Save is off then, since the history records nothing until they come, so nothing
        // is unsaved (UndoHistory's canRecord); this is the backstop for an Execute that doesn't ask.
        if (!AreDetailsReady)
        {
            return;
        }

        // A form left open is part of what's being saved, unless it's an add form nothing was entered in, which
        // has nothing to save. Its sub-assembly's tab shows, as Apply would show it, so an add made this way
        // stays the tab showing through the reload.
        Recipe.ApplyFormForSave();

        var product = new Product
        {
            Id = _editingId,
            Name = EditingName.Trim(),
            Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
            CategoryId = EditingCategory?.Id,
            QuantityRequired = EditingQuantityRequired,
            CurrentPrice = EditingCurrentPrice,

            // Only a new product's is written: a saved one's changes by moving it in the list.
            Stage = NewProductPlace.Stage,
            FolderId = NewProductPlace.FolderId,

            // A new one's to say; a saved one's is the kind it was saved as, which the form never changes.
            IsComposite = IsEditingComposite,
            IncludesComponentPackaging = IsEditingComposite && ComponentsTable.IncludesPackaging,
        };

        var validation = _productValidator.Validate(product);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return;
        }

        var costing = Costing.ToParameters();
        var costingValidation = _costingParametersValidator.Validate(costing);
        if (!costingValidation.IsValid)
        {
            ErrorMessage = costingValidation.ToString(" ");
            return;
        }

        var recipe = Recipe.CurrentDraft();
        if (!await ConfirmSharedLinesAsync())
        {
            return;
        }

        // Written with its versions, all or nothing (VersionedSaves): a saved product with none yet first gets how it
        // stood until now, so there's one to go back to; then the product as saved; and the products sharing its
        // sections' lines or linking its packaging each get one if this Save changes them. Only linked lines edited
        // here and the packaging reach those, so a Save of neither reads none: a price-only Save would
        // otherwise spend half a second reading every product sharing its dough, twice.
        var packagingLink = Packaging.ChangedLink();
        var packagingLines = Packaging.ChangedLines();
        var bulkToSave = Packaging.ChangedBulk();
        var changesSharing = Recipe.UsageEdits.Any(u => u.AreLinkedLinesEdited) || packagingLines is not null || packagingLink is not null;
        var recipeToSave = Recipe.IsAsSaved(recipe) ? null : recipe;
        var costingToSave = Costing.IsChanged ? costing : null;
        var saved = await Task.Run(() => _versionedSaves.SaveProductAsync(
            product, recipeToSave, packagingLink, packagingLines, bulkToSave, costingToSave, changesSharing, DateTime.Now, CancellationToken.None));

        // What's saved is the new starting point: stepping back past it would show changes already written.
        // Save's own reload keeps the product selected, so ClearDetails doesn't run and wouldn't clear this.
        History.Clear();

        // The list picks up the new name; then the form shows what was saved, which is what the next Save compares
        // against.
        await LoadCommand.ExecuteAsync();
        ShowSavedProduct(saved.Product, saved.Detail);

        // Read again now if the History tab shows; otherwise the versions it holds are out of date, and go, so showing it
        // reads them, so a Save made from the Recipe tab doesn't leave the tab without the new version.
        if (SelectedDetailsTab == HistoryTab)
        {
            RequestVersions(force: true);
        }
        else
        {
            VersionHistory.Clear();
        }

        if (ProductVersionRecorder.RecordedMessage(saved.SharingVersioned) is { } message)
        {
            SuccessMessage = message;
        }
    }

    /// <summary>
    /// Asks before Save changes the lines of a linked sub-assembly that other products use too, or a made ingredient is made
    /// from, naming them as they are now rather than as last loaded. True, without asking, when nothing else would change.
    /// </summary>
    private async Task<bool> ConfirmSharedLinesAsync()
    {
        var edited = Recipe.UsageEdits.Where(u => u.AreLinkedLinesEdited).ToList();
        if (edited.Count == 0)
        {
            return true;
        }

        var dependents = await _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None);
        var madeFrom = await _subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(CancellationToken.None);
        var shared = edited
            .Select(u => (u.LinkedName, Others: (IReadOnlyList<string>)
            [
                .. OtherProductsUsing(u.SubAssemblyId, dependents),
                .. (madeFrom.GetValueOrDefault(u.SubAssemblyId) ?? []).Select(m => m.Name),
            ]))
            .Where(s => s.Others.Count > 0)
            .Select(s => $"{s.LinkedName}, also used by {string.Join(", ", s.Others)}")
            .ToList();
        if (shared.Count == 0)
        {
            return true;
        }

        return await ConfirmDelete.Handle(
            $"Save changes the lines of {string.Join("; and ", shared)}. Their recipes and costs change with it. Save anyway?");
    }

    /// <summary>
    /// Selects the product as Save left it, the fresh copy, so the form shows (and compares against) what was saved:
    /// <paramref name="saved"/> and <paramref name="detail"/>, as the Save read them back. A new product, saved for the
    /// first time, is still the recipe showing, not another product's: it keeps the tab it showed, as an existing one does.
    ///
    /// The detail goes in with the selection, so it shows in one pass. Picked with the detail still to read, the same
    /// product closed its form (ResetDetails) and drew the screen that way until the read came back and opened it again:
    /// a flicker on every Save.
    /// </summary>
    private void ShowSavedProduct(Product saved, ProductDetail detail)
    {
        _productCache.AddOrUpdate(saved);
        _detailProductId = saved.Id;
        _preloadedDetail = (saved.Id, detail);
        try
        {
            SelectedProduct = saved;
        }
        finally
        {
            _preloadedDetail = null;
        }
    }

    /// <summary>A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.</summary>
    private void PackagingLinesEdited()
    {
        Packaging.ResolveRows();
        Packaging.RaiseCostsChanged();
        RecomputeCosts();
    }

    /// <summary>
    /// A composite's line picked, typed or deleted in its Products table: it's priced once its product's recipe cost has
    /// been read (a new pick's is read then), and the composite's costs follow.
    /// </summary>
    private void ComponentsEdited()
    {
        ComponentsTable.ResolveRows();
        ComponentsTable.ReadMissingCosts();
        RecomputeCosts();
    }

    /// <summary>
    /// Applies Settings' costing defaults (labor, overhead, administrative and markup) to <paramref name="products"/>, once
    /// it's confirmed. The product the form shows, when it's one of them, has its unsaved changes saved or thrown away
    /// first, and shows its new costing after.
    /// </summary>
    private async Task ApplyCostingDefaultsAsync(IReadOnlyList<Product> products)
    {
        if (products.Count == 0 || (products.Any(p => p.Id == _detailProductId) && !await ResolveUnsavedChangesAsync(revert: true)))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var defaults = await Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var question = $"Set the costing of {ProductNames(products)} to the costing defaults: labor {defaults.LaborPercent:0.##}%, overhead "
                + $"{defaults.OverheadPercent:0.##}%, administrative {defaults.AdministrativePercent:0.##}% and markup {defaults.MarginPercent:0.##}%?"
                + "\n\nA version is recorded of each one this changes.";
            if (!await ConfirmDelete.Handle(question))
            {
                return;
            }

            var costing = new CostingParameters
            {
                LaborPercent = defaults.LaborPercent,
                OverheadPercent = defaults.OverheadPercent,
                AdministrativePercent = defaults.AdministrativePercent,
                MarginPercent = defaults.MarginPercent,
            };
            var changed = await Task.Run(() => _versionedSaves.ApplyCostingAsync(
                [.. products.Select(p => p.Id)], costing, DateTime.Now, CancellationToken.None));
            await LoadCommand.ExecuteAsync();

            // The product the form shows reads its new costing, as after its own Save.
            if (_detailProductId is { } openId && products.Any(p => p.Id == openId)
                && await Task.Run(() => _productRepository.GetByIdAsync(openId, CancellationToken.None)) is { } open)
            {
                var detail = await Task.Run(() => _savedProducts.ReadDetailAsync(openId, CancellationToken.None));
                History.Clear();
                ShowSavedProduct(open, detail);
                if (SelectedDetailsTab == HistoryTab)
                {
                    RequestVersions(force: true);
                }
                else
                {
                    VersionHistory.Clear();
                }
            }

            SuccessMessage = changed.Count == 0
                ? $"{ProductNames(products)} already {(products.Count == 1 ? "has" : "have")} the costing defaults."
                : $"The costing defaults were applied to {ProductNames(changed)}, with a version of each.";
        });

        static string ProductNames<T>(IReadOnlyList<T> items) where T : notnull
        {
            List<string> names = [.. items.Select(i => i is Product p ? p.Name : i.ToString()!)];
            return names.Count switch
            {
                1 => names[0],
                <= 5 => $"{string.Join(", ", names[..^1])} and {names[^1]}",
                _ => $"{string.Join(", ", names.Take(4))} and {names.Count - 4} more",
            };
        }
    }

    /// <summary>
    /// Takes up the defaults as LoadCommand read them. A new product still showing the ones it started with (the screen's
    /// first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed into keeps it.
    /// A new product started by New goes in the new stage whatever was typed, since its stage was never picked.
    /// </summary>
    private void RefreshCostingDefaults(CostingDefaults defaults)
    {
        var untouchedNewProduct = _editingId == 0 && Costing.ShowsDefaults;
        var shopFiguresChanged = Costing.SetDefaults(defaults);
        if (shopFiguresChanged)
        {
            // Every product is priced with the shop's wastage, VAT and price rounding, the one open included: its Costing
            // tab follows at once.
            RecomputeCosts();
        }
        NewProductPlace.TakeDefault(defaults.NewProductStage, formIsNew: _editingId == 0);
        if (untouchedNewProduct)
        {
            Costing.ApplyDefaults();

            // The defaults landing on a form that was still showing the previous ones isn't the user's change -
            // on the screen's first load they land on a blank form costed at nothing - so what Save measures
            // against follows them. Only the costing moves: a product whose costing was typed into isn't here.
            History.Rebase((settled, now) => settled with { Costing = now.Costing });
        }
    }

    /// <summary>
    /// Unbuffered or Buffered, pressed: every figure moves to the other of the materials' costs. The lines tables' rows are priced
    /// again, the composite's products from what's already been read of them, and the sections from the costs they
    /// already hold, so nothing is read again, and nothing about the product changes.
    /// </summary>
    private void ToggleCostBasis()
    {
        Costing.CostBasis = Costing.CostBasis == CostBasis.Buffered ? CostBasis.Unbuffered : CostBasis.Buffered;
        UsageForm.Reprice();
        Packaging.Reprice();
        ComponentsTable.Reprice();

        RecomputeCosts();
        UsageForm.Recompute();
        VersionHistory.Reprice(Costing.CostBasis);
    }

    /// <summary>
    /// The cost summary, its captions and the below-cost warning (ProductCosting.Recompute), from the recipe and packaging
    /// as they are now, the product's fields and the costing.
    /// </summary>
    private void RecomputeCosts()
    {
        // A composite's recipe cost is what its products bring, each line its quantity at the product's recipe cost (a
        // line still being filled in brings nothing yet), then its own sections, each per whole composite.
        List<ProductComponentRow> lines = IsEditingComposite
            ? [.. ComponentsTable.ComponentEdits.Where(r => r.Product is not null && r.Quantity > 0)]
            : [];
        List<(string Name, decimal Cost)> components = [.. lines.Select(r => (r.LineName, r.RecipeCostTotal))];

        // The Products table's figure adds up its Cost column: the products' packaging is in it while the box is ticked.
        ComponentsTable.ComponentsCost = lines.Sum(r => r.Cost);

        // With the box ticked, each product's own packaging for one sellable unit, times its quantity, joins the
        // composite's packaging, not its recipe cost (a product whose costs haven't been read yet brings none until they are).
        decimal? componentPackaging = IsEditingComposite && ComponentsTable.IncludesPackaging
            ? lines.Sum(r => r.PackagingCostTotal)
            : null;
        var usages = Recipe.UsageEdits;
        Costing.Recompute(
            new ProductCostInputs
            {
                Parts = [.. components, .. usages.Select(u => (u.SectionName, u.CostPerPieceAt(Costing.CostBasis)))],
                QuantityRequired = EditingQuantityRequired,
                PackagingCosts = [.. Packaging.LineEdits.Select(l => l.CostPerUnit), .. Packaging.BulkEdits.Count > 0 ? [Packaging.BulkCostPerUnit] : Array.Empty<decimal>()],
                ComponentPackaging = componentPackaging,
                CurrentPrice = EditingCurrentPrice,
                IsComposite = IsEditingComposite,
                HasOwnSections = IsEditingComposite && usages.Count > 0,
            },
            EditingCategory?.WastagePercent,
            usages.SelectMany(u => u.Lines.Concat(u.AddIns)).Select(l => l.Ingredient)
                .Concat(Packaging.AllLineEdits.Select(l => l.PackagingMaterial).OfType<RawMaterial>()));
    }

    /// <summary>
    /// The product the screen has open, as the Print tab shows it: asked for each time that tab is shown, so it follows
    /// the edits made here since, and the product opened here since. A sub-assembly form
    /// left open is applied first, as Print applies it. Empty while the details are still arriving, and a
    /// <see cref="PrintProblem"/> when a rule refuses the form, with its message (shown on this screen too, for when it's
    /// next shown): the tab then keeps what it had.
    /// </summary>
    public PreparedPrintout PreparePrintout()
    {
        if (!AreDetailsReady)
        {
            return default;
        }

        // With the History tab showing, the pages are the version picked there.
        if (SelectedDetailsTab == HistoryTab)
        {
            return VersionHistory.SelectedRow is { } row && SelectedProduct is { } product
                ? VersionHistory.PrintoutOf(row, product)
                : default(PreparedPrintout);
        }

        try
        {
            Recipe.ApplyOpenForm();
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
            return new PrintProblem(ex.Message);
        }

        return CurrentPrintout();
    }

    /// <summary>
    /// What Print puts on paper: the product as the screen shows it now, its details, every section in recipe order, its
    /// packaging and its costing, with a note when that isn't what's saved.
    /// </summary>
    private ProductPrintout CurrentPrintout() => new(
        string.IsNullOrWhiteSpace(EditingName) ? (IsEditingComposite ? "New composite product" : "New product") : EditingName.Trim(),
        EditingCategory?.Name ?? "Uncategorized",
        ProductRules.StageTitle(SelectedProduct?.Stage ?? NewProductPlace.Stage),
        Costing.CostUnitCaption,
        LastUpdatedText,
        _detailProductId is null
            ? ProductPrintout.NotSavedNote
            : History.IsDirty ? ProductPrintout.UnsavedChangesNote : null,
        DateTime.Now,
        [.. Recipe.UsageEdits.Select(u => PrintedSection.From(u.ToPriced(), Costing.CostBasis))],
        Packaging.LineEdits.Count > 0 || Packaging.BulkEdits.Count > 0
            ? PrintedPackaging.From(
                Packaging.LineEdits.Select(l => l.ToPriced()),
                Packaging.IsPackagingLinked ? Packaging.PackagingLinkedToText : null,
                Packaging.BulkEdits.Count == 0 ? null : new PricedBulkPackaging([.. Packaging.BulkEdits.Select(l => l.ToPriced())], Packaging.BulkQuantity))
            : null,
        Costing.Costs,
        IsEditingComposite ? PrintedComponent.From(ComponentsTable.ComponentEdits) : null)
    {
        CostBasis = Costing.CostBasis,
    };

    /// <summary>
    /// Reads the picked product's versions for the History tab, when it shows and they aren't showing already;
    /// <paramref name="force"/> reads them again anyway (a Save has just recorded one).
    /// </summary>
    private void RequestVersions(bool force = false)
    {
        if (SelectedDetailsTab == HistoryTab
            && AreDetailsReady
            && SelectedProduct is { } product
            && product.Id == _detailProductId
            && (force || VersionHistory.ProductId != product.Id))
        {
            _versionReads.OnNext(product.Id);
        }
    }

    /// <summary>
    /// Saves a new category and reads the categories again; null, with the reason showing, if its name is refused. Only
    /// the categories are read: nothing else on the screen changes with a new one.
    /// </summary>
    private async Task<ProductCategory?> SaveNewCategoryAsync(string name)
    {
        var category = new ProductCategory { Name = name };
        var validation = _categoryValidator.Validate(category);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return null;
        }

        // AddAsync fills in the saved Id, which the copy read back is found by.
        await _categoryRepository.AddAsync(category, CancellationToken.None);
        await ReloadCategoriesAsync();
        return Categories.FirstOrDefault(c => c.Id == category.Id);
    }

    /// <summary>
    /// Reads the categories again, keeping the Category picker on the one it holds: a picker whose item gets replaced can
    /// clear its own selection mid-merge.
    /// </summary>
    private async Task ReloadCategoriesAsync()
    {
        var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
        var editingId = EditingCategory?.Id;
        ApplyRefresh(() =>
        {
            _categoryCache.MergeFrom(categories);
            EditingCategory = Categories.FirstOrDefault(c => c.Id == editingId);
        });
    }

    /// <summary>The Category picker took the cursor (CategoryPicker.Begin).</summary>
    public void BeginCategoryEdit() => _categoryPicker.Begin();

    /// <summary>
    /// What's typed into the Category picker, settled as it's left (CategoryPicker.CommitAsync), as on Raw Materials: a
    /// name that isn't a category yet is offered as a new one. Save saves the product's choice.
    /// </summary>
    public Task CommitCategoryTextAsync() => _categoryPicker.CommitAsync();

    /// <summary>Nothing is recorded until a product's details have arrived, nor while the screen is being written to wholesale.</summary>
    protected override bool CanRecordUndo => AreDetailsReady && !IsBusyApplying;

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim()
            : SelectedProduct?.Name ?? (IsEditingComposite ? "the new composite product" : "the new product");

    protected override Task SaveFormAsync() => SaveCommand.ExecuteAsync();

    /// <summary>
    /// Puts the form back as the product was last saved: Revert, and Don't Save on leaving the screen. It reads the picked
    /// product's detail again, which replaces the recipe, packaging, packs and costing wholesale, so anything staged but
    /// unsaved goes: a section added or edited in the form included, since the form has no discard of its own. A new
    /// product was never saved, so it goes back to being a blank one.
    /// </summary>
    protected override async Task RevertToSavedAsync()
    {
        if (SelectedProduct is not { } product)
        {
            // A new composite named in the list keeps its row until it's saved: put back blank, it has none.
            List.EndNewRow();
            StartNewProduct(NewProductPlace.ChosenStage, NewProductPlace.FolderId, IsEditingComposite);
            return;
        }

        History.Clear();
        Recipe.CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        LoadIntoForm(product);
        await LoadProductDetailAsync(product, CancellationToken.None);
    }

    /// <summary>
    /// The form as it stands: everything an undo step puts back, the product's own fields and its costing, and each part's
    /// own share. See ProductFormSnapshot for what's left out.
    /// </summary>
    protected override ProductFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Description = EditingDescription,
        CurrentPrice = EditingCurrentPrice,
        QuantityRequired = EditingQuantityRequired,
        IncludesComponentPackaging = ComponentsTable.IncludesPackaging,
        CategoryId = EditingCategory?.Id,
        CloneName = EditingCloneName,
        Costing = Costing.Percentages,
        Recipe = Recipe.Capture(),
        Packaging = Packaging.Capture(),
        Components = ComponentsTable.Capture(),
    };

    /// <summary>
    /// Puts the whole form back as <paramref name="snapshot"/> had it. Order matters throughout, and is explained
    /// step by step below. Every figure the writes would work out again waits for the end (_updates' batch), so the
    /// figures are worked out once from the restored form rather than over and over from a half-restored one.
    /// </summary>
    /// <param name="settledThen">What Save measured against when <paramref name="snapshot"/> was taken, or null.</param>
    protected override void RestoreSnapshot(ProductFormSnapshot snapshot, ProductFormSnapshot? settledThen)
    {
        // A message about a form that no longer exists would outlive what it was about.
        ErrorMessage = null;

        ApplyRestore(() => _updates.Batch(() =>
        {
            // 1. The form goes first (ProductRecipe.CloseForRestore): its section has to be let go of before the sections
            //    are replaced. No row stays picked in any table.
            Recipe.CloseForRestore();
            Packaging.SelectedLineEdit = null;
            Packaging.SelectedBulkEdit = null;
            ComponentsTable.SelectedComponentEdit = null;

            // 2. The product's own fields. The category goes back by id, as the reloaded list's own copy.
            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingDescription = snapshot.Description;
            EditingCurrentPrice = snapshot.CurrentPrice;
            EditingQuantityRequired = snapshot.QuantityRequired;
            ComponentsTable.IncludesPackaging = snapshot.IncludesComponentPackaging;
            EditingCloneName = snapshot.CloneName;
            _editingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            RaisePropertyChanged(nameof(EditingCategory));
            _updates.Run(RecomputeCosts);

            // 3. Costing.
            Costing.Percentages = snapshot.Costing;

            // 4. Packaging (ProductPackaging.Restore), the link as saved left alone: it's what Save compares against.
            Packaging.Restore(snapshot.Packaging);

            // A composite's lines, each finding its product and the recipe cost read for it again (a cost not read yet is
            // read now, and prices the line when it arrives).
            ComponentsTable.Restore(snapshot.Components);

            // 5 and 6. The sections and their tabs, under the recipe's guard against applying the form the tab moves off,
            //    then the form itself, last, its option lists rebuilt before its pickers are put back (ProductRecipe.Restore).
            Recipe.Restore(snapshot.Recipe);
        }));

        // 8. One pass over the restored form, after the batch. FormLinesChanged is the recipe's own "the lines
        //    changed, bring everything into line" entry point: it raises what the form's tables show, works the
        //    derived piece-size figure out again, and runs the single ApplyFormulaBase pass that gives every row
        //    back its BaseWeight and BatchWeight.
        UsageForm.RaiseKindChanged();
        Recipe.FormLinesChanged();
        RecomputeCosts();

        // 9. What Save measures the form against goes back to where it was when the step was taken, for the form and
        //    tabs only (WithFormOf): the step being undone may have opened another section's form (Make a Copy, Unlink),
        //    which moved it there (RebaseSettledForm), and a restore doesn't rebase, since the form it puts back may
        //    hold typing that isn't in the recipe yet. Taking the settled form of that moment instead keeps both
        //    right: a form put back as it was saved is clean again, and typing put back still counts. The recipe,
        //    costing and the rest stay as settled now, so a costing-defaults rebase since then isn't lost.
        if (settledThen is not null)
        {
            History.Rebase((settled, _) => settled.WithFormOf(settledThen));
        }
    }

    // What the list asks of the screen (IProductsListHost): the lists it loaded, the product the form shows, and what the
    // list's actions do to them.
    IReadOnlyList<ProductFolder> IProductsListHost.Folders
    {
        get => Folders;
        set => Folders = value;
    }

    Task IProductsListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void IProductsListHost.Pick(Product product) =>
        PickFromList(product.Id, () => SelectedProduct = product, List.SyncTreeSelection);

    void IProductsListHost.StartNew(ProductStage? stage, int? folderId, bool isComposite) => NewProduct(stage, folderId, isComposite);

    /// <summary>
    /// The name typed into a new product's row: the form, as New left it, is saved under it, as Save saves it, with its
    /// first version, and opened, and the row gives way to the product's own (ProductsList.EndNewRow, as it opens). Refused
    /// (a name taken), the row stays, and why shows.
    /// </summary>
    async Task IProductsListHost.SaveNewAsync(string name)
    {
        // Not an edit of the user's: there's no undo step for it, and nothing unsaved to ask about if the save is refused.
        ApplyRefresh(() => EditingName = name);
        await RunSafelyAsync(SaveAsync);
        if (_editingId == 0 && ErrorMessage is null)
        {
            // Save turned it down without a reason of its own (the details weren't ready yet).
            ErrorMessage = $"{name} couldn't be saved yet. Press Enter to try again.";
        }
    }

    void IProductsListHost.NameNew(string name) => AsUndoStep(() => EditingName = name);

    void IProductsListHost.ClearNewName() => ApplyRefresh(() => EditingName = string.Empty);

    void IProductsListHost.RenamedOnForm(Product product, string name)
    {
        if (_editingId != product.Id)
        {
            return;
        }

        // The Clone tab's name follows too, while it's still the one it started with.
        var oldCloneName = $"{product.Name} Copy";
        var newCloneName = $"{name} Copy";
        ApplyRefresh(() =>
        {
            EditingName = name;
            if (EditingCloneName == oldCloneName)
            {
                EditingCloneName = newCloneName;
            }
        });
        History.RewriteAll(snapshot => snapshot with
        {
            Name = name,
            CloneName = snapshot.CloneName == oldCloneName ? newCloneName : snapshot.CloneName,
        });
    }

    void IProductsListHost.ShowMoved(IReadOnlyList<Product> moved)
    {
        if (SelectedProduct is { } selected && moved.FirstOrDefault(p => p.Id == selected.Id) is { } fresh)
        {
            RepointSelection(fresh);
        }

        if (moved.FirstOrDefault(p => p.Id == _editingId) is { } onForm)
        {
            ShowUpdatedAt(onForm.UpdatedAt);
        }
    }

    Task IProductsListHost.CloneAsync(Product product, string name) => MakeFromAsync(product.Id, name, _productCloner.CloneAsync, ClonedFrom);

    Task IProductsListHost.CreateVariantAsync(Product product, string name) => MakeFromAsync(product.Id, name, _productCloner.CreateVariantAsync, VariantOf);

    Task IProductsListHost.DeleteAsync(Product product) => DeleteProductAsync(product);

    Task IProductsListHost.RestoreAsync(Product product) => RestoreProductAsync(product);

    Task IProductsListHost.DeletePermanentlyAsync(Product product) => DeletePermanentlyAsync(product);

    Task IProductsListHost.ApplyCostingDefaultsAsync(IReadOnlyList<Product> products) => ApplyCostingDefaultsAsync(products);

    void IProductsListHost.Print(PrintScope scope) => _printRequests.OnNext(scope);

    // What the recipe asks of the screen (IProductRecipeHost): what can be linked, the product showing and its costs.
    IReadOnlyList<SubAssembly> IProductRecipeHost.Linkable => _linkable;

    int? IProductRecipeHost.DetailProductId => _detailProductId;

    void IProductRecipeHost.RecomputeCosts() => RecomputeCosts();

    void IProductRecipeHost.OpenProduct(int productId) => OpenRecord(productId);

    void IProductRecipeHost.OpenSubAssembly(int subAssemblyId) => _subAssemblyOpenRequests.OnNext(subAssemblyId);

    // What the Packaging tab asks of the screen (IProductPackagingHost); its cost basis is the Costing part's.
    CostBasis IProductPackagingHost.CostBasis => Costing.CostBasis;

    IEnumerable<Product> IProductPackagingHost.OtherProducts => Products.Where(p => p.Id != _detailProductId);

    void IProductPackagingHost.RecomputeCosts() => RecomputeCosts();

    // What the composite's Products table asks of the screen (IProductComponentsHost): its cost basis, the Costing part's.
    CostBasis IProductComponentsHost.CostBasis => Costing.CostBasis;

    // What the section form asks of the screen (IProductUsageFormHost); its cost basis is the Costing part's.
    CostBasis IProductUsageFormHost.CostBasis => Costing.CostBasis;

    IReadOnlyList<SubAssembly> IProductUsageFormHost.Linkable => _linkable;

    IEnumerable<int> IProductUsageFormHost.SectionSubAssemblyIds => Recipe.UsageEdits.Select(u => u.SubAssemblyId);

    // What the History tab asks of the screen (IProductVersionHistoryHost): the product showing, the costs it prices at, and
    // the form a restored version goes into. Its Packaging tab is the screen's own.
    Product? IProductVersionHistoryHost.ProductShowing => AreDetailsReady ? SelectedProduct : null;

    CostBasis IProductVersionHistoryHost.CostBasis => Costing.CostBasis;

    IReadOnlySet<int> IProductVersionHistoryHost.OwnedSectionIds => Recipe.SavedOwnSectionIds;

    string? IProductVersionHistoryHost.SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) => Recipe.SectionLabelOf(subAssemblyName, role);

    /// <summary>
    /// A version Restore worked out (ProductVersionHistory.RestoreCommand), into the form as unsaved edits in one undo step:
    /// its sections, packaging, products, costing, quantity required and price, over the form as it stands, so the name,
    /// description, category and stage stay.
    /// </summary>
    void IProductVersionHistoryHost.ApplyVersion(RestoredVersion version) => AsUndoStep(() =>
    {
        // The form goes, as any restore closes it: what it held comes back with Undo.
        Recipe.CloseUsageForm();
        var now = CaptureSnapshot();
        RestoreSnapshot(
            now with
            {
                CurrentPrice = version.CurrentPrice,
                QuantityRequired = version.QuantityRequired,
                IncludesComponentPackaging = version.IncludesComponentPackaging,
                Costing = version.Costing,
                Recipe = now.Recipe with
                {
                    Usages = version.Sections,
                    SelectedTab = version.Sections.Count > 0 ? new RecipeTabChoice.Usage(0)
                        : version.IsComposite ? new RecipeTabChoice.Components()
                        : new RecipeTabChoice.Packaging(),
                    HasAddSubAssemblyTab = false,
                    Form = now.Recipe.Form with { IsOpen = false, IsAdding = false, EditingUsageIndex = null, Lines = [], AddIns = [] },
                },
                Packaging = new PackagingSnapshot
                {
                    Link = version.PackagingLink,
                    IsMoving = false,
                    Lines = version.PackagingLines,
                    BulkLines = version.BulkLines,
                    BulkQuantity = version.BulkQuantity,
                },
                Components = version.Components,
            },
            null);

        // The section tab showing opens its form, as showing it would: the restore puts tabs back without opening any,
        // and a section's tab with its form closed showed an empty form (no lines, a 0 g batch).
        if (Recipe.SelectedRecipeTab is SubAssemblyRecipeTab { Usage: var shown })
        {
            Recipe.OpenUsageForm(shown);
        }

        // The form closing and opening isn't the user's change, as it isn't when a tab change does it: only the recipe,
        // packaging, costing and price the version put back count for Save. Without this, a version the same as the
        // saved product (one whose only difference is a linked sub-assembly's lines, kept as they are now) left Save lit.
        Recipe.RebaseSettledForm();
    });

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        List.Dispose();

        _productCache.Dispose();
        _categoryCache.Dispose();
        _subAssemblyOpenRequests.Dispose();
        _printRequests.Dispose();
        _versionReads.Dispose();
        _markupChecks.Dispose();
        _picks.Dispose();
        UsageForm.Dispose();
        Packaging.Dispose();
    }
}
