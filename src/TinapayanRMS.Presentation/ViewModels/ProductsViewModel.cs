using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Products screen: a product's recipe (the sub-assemblies it links and new ones not saved yet, each at its own
/// piece size, and its packaging), its costing, its packaging link, and cloning it or making a variant of it. The name, description,
/// recipe, packaging and costing are edited in memory, and Save writes them together
/// (IProductRepository.SaveAsync). A composite product's recipe is the products it holds instead, in its Products tab
/// (<see cref="ProductComponents.ComponentEdits"/>), each bringing its recipe cost.
/// </summary>
public sealed partial class ProductsViewModel : RecordFormViewModel<ProductFormSnapshot>, IDisposable, ICategoryPickerScreen, IRecordScreen, IRevertScreen, IListTreeScreen
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
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly IPackagingLinkRepository _packagingLinkRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IRawMaterialCategoryRepository _rawMaterialCategoryRepository;
    private readonly IProductVersionRepository _versionRepository;

    // Reads the product opened here, as Reports and Print read theirs, from the repositories above.
    private readonly SavedProductReader _savedProducts;

    // Save, Clone and Create Variant, each written with the versions it records.
    private readonly VersionedSaves _versionedSaves;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IValidator<ProductCategory> _categoryValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private ReadOnlyObservableCollection<Product> _products;
    private ReadOnlyObservableCollection<Product> _visibleProducts;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private ReadOnlyObservableCollection<ProductCategory> _categories;

    /// <summary>Every product in use (none of the deleted), for the packaging link and clone pickers.</summary>
    public ReadOnlyObservableCollection<Product> Products => _products;

    /// <summary>The products the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<Product> VisibleProducts => _visibleProducts;

    /// <summary>
    /// The products in the list's Deleted folder, by name (IProductRepository.GetDeletedAsync). Kept apart from
    /// <see cref="Products"/>, so no picker offers one; only the list shows them, to restore or delete permanently.
    /// </summary>
    public IReadOnlyList<Product> DeletedProducts { get; private set; } = [];

    /// <summary>The Products list (ProductListCard): VisibleProducts filed in a folder for their stage, then their category.</summary>
    public ProductTree ProductTree { get; }

    /// <summary>The view state key the closed folders of the Products list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "Products.ClosedFolders";

    /// <summary>
    /// The view state key the list kept its open folders under, before it kept the closed ones: read while there are no
    /// closed ones kept, so the list opens as it was left (FolderMemory.Read).
    /// </summary>
    public const string OpenFoldersKey = "Products.OpenFolders";

    private readonly FolderStateWriter _closedFoldersWriter;

    private readonly TreeSelection _treeSelection = new();

    /// <summary>
    /// The rows picked in the Products list, bound to the tree's SelectedItems, which it shares: one row, or several with
    /// Ctrl or Shift held. Its first is <see cref="SelectedTreeNode"/>. The tree changes it as rows are clicked, and this
    /// screen as SelectedProduct changes (SyncTreeSelection).
    /// </summary>
    public ObservableCollection<object> SelectedTreeNodes => _treeSelection.Nodes;

    private object? _selectedTreeNode;
    /// <summary>
    /// The first row picked in the Products list: a product's (ProductLeafNode), a folder's, or null. Picking a product's
    /// row picks the product; picking a folder's leaves the product showing as it is, as a file explorer does, and so do
    /// more rows picked with Ctrl. SelectedProduct changed any other way (opened from Sub-Assemblies, saved, cloned, or
    /// cleared for a new one) moves the row to it. Setting it picks that row alone, as a click does.
    /// </summary>
    public object? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set => _treeSelection.Pick(value);
    }

    /// <summary>The products picked in the list, in the order they were picked, deleted ones left out.</summary>
    public IReadOnlyList<ProductLeafNode> SelectedTreeProducts => [.. SelectedTreeNodes.OfType<ProductLeafNode>().Where(l => !l.IsDeleted && !l.IsNew)];

    private readonly TreeNaming _treeNaming;

    /// <summary>
    /// The row of the list whose name is being typed in place, as in a file explorer, or null: a product or category being
    /// renamed (Rename, F2), or a new product or folder being named (New Product, New Folder). The list draws a box on it,
    /// which gives the name typed to <see cref="CommitTreeNameAsync"/>, or Esc to <see cref="CancelTreeName"/>.
    /// </summary>
    public TreeNameEdit? TreeNameEdit => _treeNaming.Edit;

    private string? _moveNotice;
    /// <summary>
    /// What the last move in the list did, "Moved Pandesal to Production › Breads", on the list's notice bar with Undo
    /// beside it (UndoMoveCommand); null once it's undone or dismissed. The bar hides itself after a few seconds.
    /// </summary>
    public string? MoveNotice
    {
        get => _moveNotice;
        private set => this.RaiseAndSetIfChanged(ref _moveNotice, value);
    }

    /// <summary>Where the products the last move took were before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<ProductMove> _moveUndo = [];

    private ProductStage _newProductStage = CostingDefaults.DefaultNewProductStage;
    /// <summary>
    /// The stage Save files a new product in: the one Settings → Costing Defaults gives a new product after New
    /// (Production unless it says otherwise), or the stage of the folder New Product Here was picked on.
    /// Revert keeps it. The header says so (NewProductTag).
    /// </summary>
    public ProductStage NewProductStage
    {
        get => _newProductStage;
        private set => this.RaiseAndSetIfChanged(ref _newProductStage, value);
    }

    /// <summary>The category New Product Here started the product in, which Revert puts back; null after New.</summary>
    private int? _newProductCategoryId;

    /// <summary>
    /// Whether the new product showing goes in Settings' stage rather than a folder's: started by New, not New Product
    /// Here. Such a product follows the setting when it changes (RefreshCostingDefaults).
    /// </summary>
    private bool _newProductInDefaultStage = true;

    /// <summary>What the Category picker lists. A name that isn't one of them is added by typing it (CommitCategoryTextAsync).</summary>
    public ReadOnlyObservableCollection<ProductCategory> Categories => _categories;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only, for a sub-assembly's lines: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>Every sub-assembly, saved or not, its own and linked, in recipe order (ProductUsageEdit.RecipeOrder).</summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    /// <summary>
    /// The Recipe Structure's tabs: a composite's ComponentsTab first, then a sub-assembly's for each of UsageEdits, in
    /// the same order (MirrorUsageTabs, SectionTabOffset), then AddSubAssemblyTab while a sub-assembly is being added,
    /// then PackagingTab, always last and always there, so bulk packaging can be typed into with no packaging for each piece.
    /// A new product has only Packaging, and a new composite its Products and Packaging. Only this screen changes them: the
    /// tab strip can't drag them into another order (RecipeStructureCard).
    /// </summary>
    public ObservableCollection<RecipeTab> RecipeTabs { get; } = [];

    /// <summary>The recipe has a tab besides Packaging (a section's, New Sub-Assembly's, or a composite's Products); with none, the card says how to start.</summary>
    public bool HasRecipeSections => RecipeTabs.Any(t => t is not PackagingRecipeTab);

    /// <summary>The New Sub-Assembly tab, in RecipeTabs only while Add Sub-Assembly's form is open in it.</summary>
    public AddSubAssemblyRecipeTab AddSubAssemblyTab { get; } = new();

    public PackagingRecipeTab PackagingTab { get; } = new();

    /// <summary>A composite's Products tab, in RecipeTabs only while the product showing is a composite (SetComposite).</summary>
    public ComponentsRecipeTab ComponentsTab { get; } = new();

    private bool _isEditingComposite;
    /// <summary>
    /// The product showing is a composite: made of other products (its Products tab), its own packaging, and sections only
    /// in product-owned roles (a topping, a filling), with no Quantity Required of its own. Picked by New composite product, and fixed once it's saved.
    /// </summary>
    public bool IsEditingComposite
    {
        get => _isEditingComposite;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isEditingComposite, value);
            this.RaisePropertyChanged(nameof(IsEditingSingle));
        }
    }

    /// <summary>The product showing is made of sub-assemblies, as every product that isn't a composite is.</summary>
    public bool IsEditingSingle => !IsEditingComposite;

    private ObservableAsPropertyHelper<bool> _isComponentsShowing;
    /// <summary>A composite's Products table shows under the Recipe Structure's tabs: on its Products tab.</summary>
    public bool IsComponentsShowing => _isComponentsShowing.Value;

    private bool _editingIncludesComponentPackaging;
    /// <summary>
    /// A composite's products each bring their own packaging too (Product.IncludesComponentPackaging), the checkbox beside
    /// its Products table's Remove Line: it joins the composite's packaging, not its recipe cost.
    /// </summary>
    public bool EditingIncludesComponentPackaging
    {
        get => _editingIncludesComponentPackaging;
        set
        {
            // Each line's Cost, and the add row's, take the products' packaging in or leave it out: first, since the change
            // recomputes the costs from the rows as it's raised.
            ComponentsTable.SetIncludesPackaging(value);

            this.RaiseAndSetIfChanged(ref _editingIncludesComponentPackaging, value);
            this.RaisePropertyChanged(nameof(IsComponentPackagingTicked));
            this.RaisePropertyChanged(nameof(ComponentsCaption));
            this.RaisePropertyChanged(nameof(MaterialCostTooltip));
        }
    }

    /// <summary>
    /// The Include each product's packaging checkbox's side of <see cref="EditingIncludesComponentPackaging"/>: a tick or
    /// an untick from the screen is one undo step, whether it came by mouse, key or UI Automation. A step around the box's
    /// focus couldn't give that, since the box keeps the cursor from one click to the next.
    /// </summary>
    public bool IsComponentPackagingTicked
    {
        get => EditingIncludesComponentPackaging;
        set
        {
            if (value != EditingIncludesComponentPackaging)
            {
                AsUndoStep(() => EditingIncludesComponentPackaging = value);
            }
        }
    }

    /// <summary>The Products tab's caption: what each line brings, and where the products' own packaging goes.</summary>
    public string ComponentsCaption => EditingIncludesComponentPackaging
        ? "Each product brings its recipe (ingredients) cost and its own packaging for how many of it go in: Cost is both. On the Costing tab the packaging joins this product's Packaging. A topping or filling over the whole goes on a tab of its own, with Add Sub-Assembly."
        : "Each product brings its recipe (ingredients) cost for how many of it go in. Its own packaging stays out: this product's Packaging covers the box and anything wrapping the pieces. A topping or filling over the whole goes on a tab of its own, with Add Sub-Assembly.";

    private RecipeTab? _selectedRecipeTab;
    /// <summary>The Recipe Structure tab showing. Showing New Sub-Assembly opens the form that adds one.</summary>
    public RecipeTab? SelectedRecipeTab
    {
        get => _selectedRecipeTab;
        set => this.RaiseAndSetIfChanged(ref _selectedRecipeTab, value);
    }

    private ObservableAsPropertyHelper<bool> _isUsageFormShowing;
    /// <summary>
    /// The sub-assembly form shows under the Recipe Structure's tabs: on a sub-assembly's tab, and on New
    /// Sub-Assembly's while its form is open. There's one form for them all (RecipeStructureCard).
    /// </summary>
    public bool IsUsageFormShowing => _isUsageFormShowing.Value;

    private ObservableAsPropertyHelper<bool> _isPackagingShowing;
    /// <summary>The packaging shows under the Recipe Structure's tabs: on the Packaging tab.</summary>
    public bool IsPackagingShowing => _isPackagingShowing.Value;

    /// <summary>The last tab other than New Sub-Assembly that showed, for cancelling an add.</summary>
    private RecipeTab? _lastRecipeContentTab;

    /// <summary>The tab whose form is open: the one a change of tab is leaving, whose form is applied on the way out.</summary>
    private RecipeTab? _shownRecipeTab;

    /// <summary>Set while a refused form puts the tab showing back, so the tab it puts back doesn't apply its own form again.</summary>
    private bool _revertingRecipeTab;

    /// <summary>
    /// Set while the sections are rebuilt, or New Sub-Assembly's tab taken away. The tab strip picks a neighbour
    /// when the tab showing is removed; that pick is ignored, and ReselectRecipeTab picks one after.
    /// </summary>
    private bool _arrangingRecipeTabs;

    /// <summary>
    /// Set while SelectRecipeTab's pick is on its way to the tab strip, which hears of it after this screen does:
    /// New Sub-Assembly's tab can't be taken away until the strip has moved off it, or the strip would pick a
    /// neighbour of its own in place of this one.
    /// </summary>
    private bool _selectingRecipeTab;

    /// <summary>
    /// The recipe showing was cleared for another product's, which opens on its first tab once it arrives
    /// (ReplaceUsages). What the tab strip was left on as the tabs went says nothing: that's always Packaging.
    /// </summary>
    private bool _isRecipeClearedForAnother;

    /// <summary>A packaging material as the screen last loaded it, for a row that prices itself.</summary>
    private RawMaterial? FindPackagingMaterial(int id) => PackagingMaterials.FirstOrDefault(m => m.Id == id);

    /// <summary>The standalone packaging sets (sub-assemblies in the Packaging role), by name, as last loaded, for the Packaging tab's Link and Copy.</summary>
    private List<SubAssembly> _packagingSets = [];


    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private Product? _selectedProduct;

    /// <summary>
    /// Each product picked, by the user or for them (Open, a clone made, Save's reload), nothing picked included: what
    /// loads a product into the form. Fed by <see cref="SelectedProduct"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<Product?> _picks = new(null);

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
            if (EqualityComparer<Product?>.Default.Equals(_selectedProduct, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedProduct, value);
            _picks.OnNext(value);
        }
    }

    /// <summary>The selection pointed at <paramref name="fresh"/>, a load's copy of the product picked: shown, not picked again.</summary>
    private void RepointSelection(Product? fresh) => this.RaiseAndSetIfChanged(ref _selectedProduct, fresh, nameof(SelectedProduct));

    /// <summary>
    /// A product for LoadCommand to open at its end: one <see cref="OpenProduct"/> was asked for before the list held it,
    /// or the one in the screen's address (<see cref="OpenProductOnLoad"/>). New drops it.
    /// </summary>
    private int? _productToOpen;

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
        private set => this.RaiseAndSetIfChanged(ref _areDetailsReady, value);
    }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingDescription = string.Empty;
    public string EditingDescription
    {
        get => _editingDescription;
        set => this.RaiseAndSetIfChanged(ref _editingDescription, value);
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
        set => this.RaiseAndSetIfChanged(ref _editingQuantityRequired, value);
    }

    /// <summary>What the figures on this screen are for: "Per piece", or "Per unit of 8 pieces".</summary>
    public string CostUnitCaption => ProductPrintout.CostUnitCaptionFor(EditingQuantityRequired, IsEditingComposite);

    /// <summary>The Costing Summary's price row: "Suggested Selling Price per Piece", or "… per Unit of 8 Pieces".</summary>
    public string SuggestedSellingPriceLabel => ProductPrintout.SuggestedSellingPriceLabelFor(CostUnitCaption);

    /// <summary>The header's Material Cost tooltip: what goes into it, which for a composite is what its products bring.</summary>
    public string MaterialCostTooltip => (IsEditingComposite
        ? $"{CostUnitCaption}: the recipe cost of the products it holds and its own sub-assemblies, plus its own packaging"
            + (EditingIncludesComponentPackaging ? " and theirs" : string.Empty)
        : $"{CostUnitCaption}: the share of every sub-assembly it holds, plus its packaging")
        + (IsUnbuffered ? ", at the materials' true costs, without their cost buffers" : ", at the materials' buffered costs");

    /// <summary>What the Costing tab's labor, overhead and admin metrics are a percentage of, for their tooltips.</summary>
    public string MetricBaseTooltip => IsEditingComposite
        ? $"Of the recipe (ingredients) cost of the products it holds and its own sub-assemblies, packaging not included. {CostUnitCaption}."
        : $"Of the sub-assemblies (ingredients) cost, packaging not included. {CostUnitCaption}.";

    private decimal? _editingCurrentPrice;
    /// <summary>What the shop charges for one sellable unit now, with VAT (Product.CurrentPrice); null when not entered.</summary>
    public decimal? EditingCurrentPrice
    {
        get => _editingCurrentPrice;
        set => this.RaiseAndSetIfChanged(ref _editingCurrentPrice, value);
    }

    private string _categorySearchText = string.Empty;
    /// <summary>
    /// What's typed into the Category picker (bound to its Text): settled into a category, or a new one, once the
    /// picker is left (CommitCategoryTextAsync).
    /// </summary>
    public string CategorySearchText
    {
        get => _categorySearchText;
        set => this.RaiseAndSetIfChanged(ref _categorySearchText, value);
    }

    private ProductCategory? _editingCategory;
    /// <summary>The product's category, or null for none: a product doesn't need one.</summary>
    public ProductCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    /// <summary>The category the picker held when it took the cursor, for a cancelled prompt to put back (BeginCategoryEdit).</summary>
    private ProductCategory? _categoryBeforeEdit;

    /// <summary>Set while CommitCategoryTextAsync runs: its prompt takes the cursor from the picker, which would settle it again.</summary>
    private bool _isCommittingCategory;

    private ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The header's title, and the usage example's: the product's name, or "New product".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private ObservableAsPropertyHelper<string?> _newProductTag;
    /// <summary>
    /// The tag beside the header's title while the form holds a product not saved yet: where Save will file it, "Goes in
    /// Production" or "Goes in Innovation". Null while a saved product is showing.
    /// </summary>
    public string? NewProductTag => _newProductTag.Value;

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the materials' costs every figure on the screen is at: buffered, as always, until the header's Unbuffered
    /// button (<see cref="ToggleCostBasisCommand"/>) switches it to their true costs for a look without the buffer. It isn't part
    /// of the product, so it's no edit: nothing to undo or save, and it stays as it is from one product to the next until
    /// the app closes. Print prints at it too.
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

    /// <summary>The screen shows its unbuffered costs: the header's badge says so.</summary>
    public bool IsUnbuffered => CostBasis == CostBasis.Unbuffered;

    private string? _belowCostText;
    /// <summary>
    /// The header's "Priced below cost" badge's tooltip, naming the purchased materials in the recipe and packaging that
    /// prices have risen past their fixed buffered cost (RawMaterialRules.IsBufferedBelowCost): the product is priced at the
    /// buffered costs, so under what those cost. Null, and no badge, while none is. A composite's products' materials aren't
    /// in the form, so they aren't counted; each says so when it's opened.
    /// </summary>
    public string? BelowCostText
    {
        get => _belowCostText;
        private set => this.RaiseAndSetIfChanged(ref _belowCostText, value);
    }

    /// <summary>
    /// The header's switch, named for the costs a click switches to: "Unbuffered" while
    /// the screen is buffered, "Buffered" while it isn't.
    /// </summary>
    public string CostBasisButtonLabel => IsUnbuffered ? "Buffered" : "Unbuffered";

    /// <summary>What the header's switch does from here.</summary>
    public string CostBasisButtonTooltip => IsUnbuffered
        ? "Shows every cost on this screen, and prints it, at the materials' buffered costs again. Nothing is saved."
        : "Shows every cost on this screen, and prints it, at the materials' true costs, without their cost buffers. Nothing is saved.";

    /// <summary>When the product was last saved, e.g. "Sep 14, 2026, 6:27 PM", or "—".</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    /// <summary>The details tab showing. Moves to Recipe when Clone is about to hide (ShowLinks).</summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private ObservableAsPropertyHelper<bool> _showLinks;
    /// <summary>A saved product: links and clones need one, so the tab only shows then.</summary>
    public bool ShowLinks => _showLinks.Value;

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

    /// <summary>The products each load brings, whose markups the list's flags are worked out from.</summary>
    private readonly Subject<IReadOnlyList<Product>> _markupChecks = new();

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
            if (ProductTree.Find(product.Id) is { } leaf)
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

    private string _editingCloneName = string.Empty;
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => this.RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

    /// <summary>Every sub-assembly that could be linked or copied, as last loaded; LinkableSubAssemblies filters it.</summary>
    private IReadOnlyList<SubAssembly> _linkable = [];

    // What the product had when it was loaded, so Save only sends the parts that changed.
    private RecipeDraft _savedRecipe = new([], []);

    // The shop-wide Wastage % (Settings → Costing Defaults), as of the last LoadCommand: what a product is priced with unless
    // its category sets its own (EditingCategory.WastagePercent).
    private decimal _wastagePercent = CostingDefaults.DefaultWastagePercent;

    // The shop's VAT (Settings → Costing Defaults), as of the last LoadCommand: what every product is priced with.
    private decimal _vatPercent;

    /// <summary>What the Costing tab's rounded selling price is rounded up to a multiple of (CostingDefaults.PriceRoundingStep).</summary>
    private decimal _priceRoundingStep;

    // The stage New files a product in (Settings → Costing Defaults), as of the last LoadCommand.
    private ProductStage _defaultNewProductStage = CostingDefaults.DefaultNewProductStage;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; private set; }

    /// <summary>
    /// What Save does, to be run in RunSafelyAsync: also how a new product named in the list is saved, which has no
    /// unsaved change for SaveCommand to be on for.
    /// </summary>
    private Func<Task> _saveForm;

    /// <summary>
    /// Revert: reads the product back as it was last saved, throwing away everything staged since — the details, the
    /// recipe's sections and their lines, the packaging, the packs and the costing. The sub-assembly form has no
    /// discard of its own, so this is what abandons a section being added or edited, along with the rest.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RevertCommand { get; private set; }

    /// <summary>
    /// Print: asks for the Print tab (<see cref="PrintRequests"/>), which shows the product's recipe and costing as the
    /// screen shows them, unsaved edits included (<see cref="PreparePrintout"/>). A sub-assembly form left open is applied first, as Save applies it, so a rule
    /// refusing it shows its message here and the tab doesn't open.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> PrintCommand { get; private set; }

    /// <summary>Unbuffered: switches every cost on the screen between the materials' buffered costs and their true ones (<see cref="CostBasis"/>).</summary>
    public ReactiveCommand<RxVoid, RxVoid> ToggleCostBasisCommand { get; private set; }

    private readonly Subject<PrintScope> _printRequests = new();

    /// <summary>
    /// What a Print here asks to print: the header's, This product; a folder's menu, the folder; the list's, all products.
    /// The main window, which owns the tabs, shows its Print tab with that choice (PrintTabViewModel.Pick), and the tab
    /// asks <see cref="PreparePrintout"/> for This product each time it's shown.
    /// </summary>
    public IObservable<PrintScope> PrintRequests => _printRequests;

    /// <summary>Print Folder, or Print Stage, on the list's folder: its products, as saved, on the Print tab.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> PrintFolderCommand { get; private set; }

    /// <summary>Print All Products on the list's empty space: every product, as saved, on the Print tab.</summary>
    public ReactiveCommand<RxVoid, RxVoid> PrintAllCommand { get; private set; }

    /// <summary>
    /// A linked standalone sub-assembly's badge clicked (a recipe section's, or linked packaging's for a packaging set):
    /// asks for the sub-assembly with this id to be opened, through <see cref="SubAssemblyOpenRequests"/>.
    /// </summary>
    public ReactiveCommand<int, RxVoid> OpenSubAssemblyCommand { get; private set; }

    /// <summary>
    /// A linked recipe section's badge clicked: opens where it's kept, the product whose own it is for a variant's link
    /// (OpenProduct), and otherwise the Sub-Assemblies screen, as <see cref="OpenSubAssemblyCommand"/> does.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> OpenLinkedSourceCommand { get; private set; }

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>
    /// The sub-assemblies linked badges ask to open, by id. The main window, which owns the tabs, subscribes and opens
    /// each on the Sub-Assemblies screen, as Sub-Assemblies' Dependents tiles open their products here.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; private set; }

    /// <summary>
    /// Products moved to other folders of the Products list (ProductListCard), by a drag, Move To, or Promote: files each
    /// under its folder's stage and category straight away, with no Save, and says so on the list's notice bar with an
    /// Undo (MoveNotice). On the product the form shows, the Category picker follows, and the form's undo steps take the
    /// new category as given, so no undo brings the old one back for a Save to write.
    /// </summary>
    public ReactiveCommand<IReadOnlyList<ProductMove>, RxVoid> MoveProductsCommand { get; private set; }

    /// <summary>
    /// Sets the costing of the products picked in the list (or the one its menu was opened on) to Settings' costing
    /// defaults, once it's confirmed, recording a version of each one it changes (VersionedSaves.ApplyCostingAsync).
    /// </summary>
    public ReactiveCommand<IReadOnlyList<Product>, RxVoid> ApplyCostingDefaultsCommand { get; private set; }

    /// <summary>Undo on the list's notice bar: puts the products the last move took back where they were.</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; private set; }

    /// <summary>The notice bar's close button, and its timeout: the move stays, and can't be undone from there any more.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; private set; }

    /// <summary>
    /// New Product Here on a folder of the list: a new product, as New starts one, filed in that folder's stage and
    /// category. Null, from the list's empty space, is New itself. Like New, it adds the product's row to the list for its
    /// name to be typed into (TreeNameEdit), and the name typed saves it.
    /// </summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewProductInCommand { get; private set; }

    /// <summary>New's New composite product: a new product made of other products, with its own packaging and toppings or fillings.</summary>
    public ReactiveCommand<RxVoid, RxVoid> NewCompositeCommand { get; private set; }

    /// <summary>New Composite Product Here on a folder of the list, as New Product Here; null, from the list's empty space, is New composite product itself.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewCompositeProductInCommand { get; private set; }

    /// <summary>The Products table's Remove Line: takes out the line selected.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveComponentCommand { get; private set; }

    /// <summary>
    /// Rename on a product of the list: its name becomes a box to type into, in place (TreeNameEdit), and the name typed is
    /// saved at once, like a move (IProductRepository.RenameAsync).
    /// </summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> RenameProductCommand { get; private set; }

    /// <summary>Clone on a product of the list: asks for the new product's name, then clones it as saved, as the Clone tab does.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> CloneProductCommand { get; private set; }

    /// <summary>Create Variant on a product of the list: asks for the variant's name, then makes it, as the Clone tab does.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> CreateVariantOfCommand { get; private set; }

    /// <summary>Delete on a product of the list: Delete for that product, asking first.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> DeleteProductCommand { get; private set; }

    /// <summary>Restore on a product in the Deleted folder: puts it back in its folder, and picks it.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> RestoreProductCommand { get; private set; }

    /// <summary>Delete Permanently on a product in the Deleted folder: asks first, then deletes it for good.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> DeletePermanentlyCommand { get; private set; }

    /// <summary>
    /// New Folder on the list: adds a "New folder" row to a stage for its name to be typed into (TreeNameEdit), and the
    /// name typed adds a category, a folder in both stages. Its parameter is the folder it was asked on; from the list's
    /// empty space, the stage of the row picked, or Production.
    /// </summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewCategoryCommand { get; private set; }

    /// <summary>Rename Category on a category's folder: its name becomes a box to type into, in place, and the name typed renames the category, and so its folder in both stages.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> RenameCategoryCommand { get; private set; }

    /// <summary>Delete Category on a category's folder: asks first, then deletes it, leaving its products uncategorized.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> DeleteCategoryCommand { get; private set; }

    /// <summary>Expand All: opens every folder in the stage given, or in both.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> ExpandAllCommand { get; private set; }

    /// <summary>Collapse All: closes every folder in the stage given, or in both, leaving the stages open.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> CollapseAllCommand { get; private set; }

    /// <summary>
    /// Input: what the prompt is for, its box's label, the name it starts with, and a line saying more. Output: the name
    /// typed, or null if it was cancelled. The View shows a TextPromptWindow. Asked by the list's Clone and Create Variant;
    /// the list names everything else in place (TreeNameEdit).
    /// </summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with.
    /// Output: the name to add it as, or null if it was cancelled. The View shows a TextPromptWindow.
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

    /// <summary>Adds a sub-assembly the way the parameter says: Add Sub-Assembly's drop-down carries one item per way.</summary>
    public ReactiveCommand<UsageFormMode, RxVoid> AddUsageCommand { get; private set; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> EditUsageCommand { get; private set; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> MakeCopyCommand { get; private set; }

    /// <summary>Unlink on a linked section: it becomes a copy of the product's own, in whatever role, and the link goes with Save.</summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> UnlinkCommand { get; private set; }

    /// <summary>The linked badge's Edit: shows the picker of what the section could link instead (IsChangingLink).</summary>
    public ReactiveCommand<RxVoid, RxVoid> StartChangingLinkCommand { get; private set; }

    /// <summary>Puts the badge back without changing the link.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelChangingLinkCommand { get; private set; }

    /// <summary>
    /// Move to Sub-Assemblies on one of the product's own: Save puts it on the Sub-Assemblies screen, keeping its name and
    /// lines, and links it. Unlink the other way round.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> MoveToSubAssembliesCommand { get; private set; }

    /// <summary>
    /// Move to In-House on one of the product's own: asks for the made ingredient's name and Made category, then Save moves
    /// the section to the Sub-Assemblies screen, links it there, and adds that ingredient, made from it.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> MoveToInHouseCommand { get; private set; }

    /// <summary>
    /// Input: Move to In-House's prompt, its name box starting with the section's saved name and its picker on a Made
    /// category. Output: the name typed and the category picked, or null if it was cancelled. The View shows a
    /// TextPromptWindow with a picker.
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>Delete Sub-Assembly: takes the sub-assembly whose tab is showing out of the recipe.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveUsageCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormLineCommand { get; private set; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveFormAddInCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; private set; }

    /// <summary>Removes the line picked in the Packaging tab's Bulk Packaging table, as one undo step.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveBulkLineCommand { get; private set; }

    /// <summary>Make a Copy on a linked Packaging tab: the lines it shows become the product's own, and the link goes with Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyPackagingCommand { get; private set; }

    /// <summary>Link on the Packaging tab: a picker of the packaging sets, the one picked linked in place of the packaging per piece.</summary>
    public ReactiveCommand<RxVoid, RxVoid> StartLinkingPackagingCommand { get; private set; }

    /// <summary>Copy on the Packaging tab: a picker of the packaging sets and the other products, the one picked's lines copied in as this product's own.</summary>
    public ReactiveCommand<RxVoid, RxVoid> StartCopyingPackagingCommand { get; private set; }

    /// <summary>Closes the Packaging tab's Link or Copy picker with nothing picked.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelPickingPackagingCommand { get; private set; }

    /// <summary>Remove Link on a linked Packaging tab: the link and the lines it shows go, leaving no packaging per piece.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemovePackagingLinkCommand { get; private set; }

    /// <summary>
    /// Move to Sub-Assemblies on the Packaging tab, while its lines are the product's own: Save puts them on the
    /// Sub-Assemblies screen as a packaging set named after the product, and links it (PackagingLinkDraft.NewSet). Make a
    /// Copy the other way round.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> MovePackagingToSubAssembliesCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> CloneCommand { get; private set; }

    /// <summary>Create Variant on the Clone tab: a new product that links this one's recipe and packaging (IProductCloner.CreateVariantAsync).</summary>
    public ReactiveCommand<RxVoid, RxVoid> CreateVariantCommand { get; private set; }

    /// <summary>The History tab: the product's versions, read while the tab shows (<see cref="RequestVersions"/>).</summary>
    public ProductVersionHistory VersionHistory { get; }

    /// <summary>
    /// Restore on the History tab: the picked version's recipe, packaging, costing and price into the form, as unsaved
    /// edits in one undo step (<see cref="RestoreVersionAsync"/>).
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RestoreVersionCommand { get; private set; }

    /// <summary>A product whose versions the History tab should read: pushed as the tab shows, and after a Save.</summary>
    private readonly Subject<int> _versionReads = new();

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
        IProductVersionRepository versionRepository,
        IValidator<Product> productValidator,
        IValidator<CostingParameters> costingParametersValidator,
        IValidator<ProductCategory> categoryValidator,
        IViewStateStore viewStateStore,
        ILogger logger)
        : base(logger)
    {
        _treeNaming = new TreeNaming(
            _treeSelection, () => this.RaisePropertyChanged(nameof(TreeNameEdit)), IsNewRow, CommitTreeNameToAsync, TreeNameCancelled);
        _versionRepository = versionRepository;
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _packagingLinkRepository = packagingLinkRepository;
        _productCloner = productCloner;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _rawMaterialCategoryRepository = rawMaterialCategoryRepository;
        _savedProducts = new SavedProductReader(
            packagingLinkRepository, usageRepository, packagingLineRepository, costingParametersRepository, subAssemblyLineRepository, productRepository,
            costingDefaultsRepository, unitOfWork);
        Packaging = new ProductPackaging(FindPackagingMaterial, () => CostBasis);
        UsageForm = new ProductUsageForm(id => Ingredients.FirstOrDefault(m => m.Id == id), () => CostBasis);
        ComponentsTable = new ProductComponents(
            _savedProducts,
            id => Products.FirstOrDefault(p => p.Id == id),
            () => Products.ToDictionary(p => p.Id),
            () => CostBasis,
            () => EditingIncludesComponentPackaging,
            (ex, text) => ShowFailure(ex, text),
            Work);
        VersionHistory = new ProductVersionHistory(
            versionRepository,
            productRepository,
            _savedProducts,
            () =>
            {
                // Today's materials are the screen's own.
                Dictionary<int, RawMaterial> materialsNow = [];
                foreach (var material in Ingredients.Concat(PackagingMaterials))
                {
                    materialsNow.TryAdd(material.Id, material);
                }

                return materialsNow;
            },
            () => CostBasis,
            RunSafelyAsync);
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

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter); Dispose writes any change still waiting.
        ProductTree = new ProductTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey, OpenFoldersKey, ProductFolderNode.CurrentFolderKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, ProductTree.ExpansionChanged, ProductTree.ClosedFolderKeys);

        // The subscriptions, in this order: two following one property run in the order they were made, and some count on it
        // (WireDetailsTabs). Several run as they're made, WirePicks' included, so each comes after what it reads.
        WireRecipeTabLists();
        WireList();
        WireHeader();
        WireDetailsTabs();
        WireRecipeTabFlags();
        WirePicks();
        WireRecipeTabPicks();
        WireUsageForm();
        WireCosts();
        WireUnsavedChanges();

        // The commands. Making one runs nothing but its can-execute flag, so they can come in any order.
        WireLoadCommand(subAssemblyRoleRepository, rawMaterialRepository);
        WireRecordCommands();
        WireListCommands();
        WireRecipeCommands();
        WireUsageFormCommands();
        WireSaveCommands();
        WireVersionCommands();
        WireCostingCommands();
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        ProductTree.Dispose();

        _productCache.Dispose();
        _categoryCache.Dispose();
        _subAssemblyOpenRequests.Dispose();
        _printRequests.Dispose();
        _versionReads.Dispose();
        _markupChecks.Dispose();
        _picks.Dispose();
        UsageForm.Dispose();
    }
}
