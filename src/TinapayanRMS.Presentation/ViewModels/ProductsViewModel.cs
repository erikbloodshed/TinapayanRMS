using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
public sealed class ProductsViewModel : ViewModelBase, IUndoScreen, IDisposable, ICategoryPickerScreen, IRecordScreen, IRevertScreen, IUnsavedChangesScreen, IListTreeScreen
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, Scaling, then Clone.</summary>
    public const int RecipeTab = 0;

    /// <summary>Recipe 0, Costing 1, then Scaling: the recipe scaled to a number of sellable units.</summary>
    public const int ScalingTab = 2;

    /// <summary>Recipe 0, Costing 1, Scaling 2, then Clone.</summary>
    public const int LinksTab = 3;

    /// <summary>After Clone: the product's versions (ProductVersionHistory). A saved product's only, as Clone is.</summary>
    public const int HistoryTab = 4;

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
    private readonly IIngredientBomLineRepository _ingredientBomLineRepository;
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
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly ReadOnlyObservableCollection<Product> _visibleProducts;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categories;

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
    public IReadOnlyList<ProductLeafNode> SelectedTreeProducts => [.. SelectedTreeNodes.OfType<ProductLeafNode>().Where(l => !l.IsDeleted)];

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
    /// then PackagingTab while the product has packaging (HasPackaging). A new product has none, and a new composite
    /// only its Products. Only this screen changes them: the tab strip can't drag them into another order
    /// (RecipeStructureCard).
    /// </summary>
    public ObservableCollection<RecipeTab> RecipeTabs { get; } = [];

    /// <summary>The Recipe Structure has a tab to show; with none, the card says how to start.</summary>
    public bool HasRecipeTabs => RecipeTabs.Count > 0;

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

    private readonly ObservableAsPropertyHelper<bool> _isComponentsShowing;
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

    private readonly ObservableAsPropertyHelper<bool> _isUsageFormShowing;
    /// <summary>
    /// The sub-assembly form shows under the Recipe Structure's tabs: on a sub-assembly's tab, and on New
    /// Sub-Assembly's while its form is open. There's one form for them all (RecipeStructureCard).
    /// </summary>
    public bool IsUsageFormShowing => _isUsageFormShowing.Value;

    private readonly ObservableAsPropertyHelper<bool> _isPackagingShowing;
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

    /// <summary>The standalone packaging sets (sub-assemblies in the Packaging role), by name, as last loaded, for the form's picker.</summary>
    private IReadOnlyList<SubAssembly> _packagingSets = [];


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

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The header's title, and the usage example's: the product's name, or "New product".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string?> _newProductTag;
    /// <summary>
    /// The tag beside the header's title while the form holds a product not saved yet: where Save will file it, "Goes in
    /// Production" or "Goes in Research and Development". Null while a saved product is showing.
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

    private readonly ObservableAsPropertyHelper<bool> _showLinks;
    /// <summary>A saved product: links and clones need one, so the tab only shows then.</summary>
    public bool ShowLinks => _showLinks.Value;

    private bool _hasPackaging;
    /// <summary>
    /// The product has packaging, its own lines or a link, so the Recipe Structure has its Packaging tab. A new product
    /// has none until Add Sub-Assembly adds it in the Packaging role; Delete Sub-Assembly on the tab takes it out.
    /// </summary>
    public bool HasPackaging
    {
        get => _hasPackaging;
        private set => this.RaiseAndSetIfChanged(ref _hasPackaging, value);
    }

    /// <summary>The Costing tab: the costing percentages, each metric's amount, and the cost summary (Costs).</summary>
    public ProductCosting Costing { get; } = new();

    /// <summary>The Recipe Structure's Packaging tab: the packaging lines, or what they link, and a Move to Sub-Assemblies.</summary>
    public ProductPackaging Packaging { get; }

    /// <summary>A composite's Products table: its lines, the add row, the product cell's options, and what each product brings.</summary>
    public ProductComponents ComponentsTable { get; }

    /// <summary>The add/edit sub-assembly form: the section it edits or adds, its fields and tables, and what it shows.</summary>
    public ProductUsageForm UsageForm { get; }

    /// <summary>The Scaling tab: the recipe as the form holds it, scaled to a number of sellable units.</summary>
    public ProductScaling Scaling { get; }

    /// <summary>What the Scaling tab scales: the recipe as the form holds it (as the Costing tab takes it), and a composite's products.</summary>
    private ScalingInput ScalingInputNow()
    {
        List<ProductComponentRow> components = IsEditingComposite
            ? [.. ComponentsTable.ComponentEdits.Where(r => r.Product is not null && r.Quantity > 0)]
            : [];
        return new ScalingInput(
            new ScalingRecipe(
                [.. UsageEdits.Select(u => u.ToPriced())],
                [.. Packaging.LineEdits.Select(l => l.ToPriced())],
                [.. components.Select(r => (r.Product!, r.Quantity!.Value))],
                EditingQuantityRequired,
                IsEditingComposite && EditingIncludesComponentPackaging),
            _detailProductId,
            [.. components.Select(r => r.Product!)]);
    }

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
    private List<string> OtherProductsUsing(int subAssemblyId, IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> usedBy) =>
        [.. usedBy.GetValueOrDefault(subAssemblyId, [])
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

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    /// <summary>
    /// Revert: reads the product back as it was last saved, throwing away everything staged since — the details, the
    /// recipe's sections and their lines, the packaging, the packs and the costing. The sub-assembly form has no
    /// discard of its own, so this is what abandons a section being added or edited, along with the rest.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RevertCommand { get; }

    /// <summary>
    /// Print: asks for the Print tab (<see cref="PrintRequests"/>), which shows the product's recipe and costing as the
    /// screen shows them, unsaved edits included, or its recipe scaled while the Scaling tab shows
    /// (<see cref="PreparePrintout"/>). A sub-assembly form left open is applied first, as Save applies it, so a rule
    /// refusing it shows its message here and the tab doesn't open.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> PrintCommand { get; }

    /// <summary>Unbuffered: switches every cost on the screen between the materials' buffered costs and their true ones (<see cref="CostBasis"/>).</summary>
    public ReactiveCommand<RxVoid, RxVoid> ToggleCostBasisCommand { get; }

    private readonly Subject<PrintScope> _printRequests = new();

    /// <summary>
    /// What a Print here asks to print: the header's, This product; a folder's menu, the folder; the list's, all products.
    /// The main window, which owns the tabs, shows its Print tab with that choice (PrintTabViewModel.Pick), and the tab
    /// asks <see cref="PreparePrintout"/> for This product each time it's shown.
    /// </summary>
    public IObservable<PrintScope> PrintRequests => _printRequests;

    /// <summary>Print Folder, or Print Stage, on the list's folder: its products, as saved, on the Print tab.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> PrintFolderCommand { get; }

    /// <summary>Print All Products on the list's empty space: every product, as saved, on the Print tab.</summary>
    public ReactiveCommand<RxVoid, RxVoid> PrintAllCommand { get; }

    /// <summary>
    /// A linked standalone sub-assembly's badge clicked (a recipe section's, or linked packaging's for a packaging set):
    /// asks for the sub-assembly with this id to be opened, through <see cref="SubAssemblyOpenRequests"/>.
    /// </summary>
    public ReactiveCommand<int, RxVoid> OpenSubAssemblyCommand { get; }

    /// <summary>
    /// A linked recipe section's badge clicked: opens where it's kept, the product whose own it is for a variant's link
    /// (OpenProduct), and otherwise the Sub-Assemblies screen, as <see cref="OpenSubAssemblyCommand"/> does.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> OpenLinkedSourceCommand { get; }

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>
    /// The sub-assemblies linked badges ask to open, by id. The main window, which owns the tabs, subscribes and opens
    /// each on the Sub-Assemblies screen, as Sub-Assemblies' Used By tiles open their products here.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    /// <summary>
    /// The form's undo and redo stacks. One step is one finished change — a cell edit committed, a row added or
    /// taken out, a sub-assembly applied, a field typed and left — never a keystroke. It holds what the screen
    /// shows, not what Save writes: undoing never touches the baselines Save compares against, so a change undone
    /// and one never made come to the same thing. Cleared whenever the form stops being the one it recorded:
    /// another product picked, a new one started, a Save, or a Revert.
    /// </summary>
    public UndoHistory<ProductFormSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    /// <summary>
    /// Products moved to other folders of the Products list (ProductListCard), by a drag, Move To, or Promote: files each
    /// under its folder's stage and category straight away, with no Save, and says so on the list's notice bar with an
    /// Undo (MoveNotice). On the product the form shows, the Category picker follows, and the form's undo steps take the
    /// new category as given, so no undo brings the old one back for a Save to write.
    /// </summary>
    public ReactiveCommand<IReadOnlyList<ProductMove>, RxVoid> MoveProductsCommand { get; }

    /// <summary>
    /// Sets the costing of the products picked in the list (or the one its menu was opened on) to Settings' costing
    /// defaults, once it's confirmed, recording a version of each one it changes (VersionedSaves.ApplyCostingAsync).
    /// </summary>
    public ReactiveCommand<IReadOnlyList<Product>, RxVoid> ApplyCostingDefaultsCommand { get; }

    /// <summary>Undo on the list's notice bar: puts the products the last move took back where they were.</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; }

    /// <summary>The notice bar's close button, and its timeout: the move stays, and can't be undone from there any more.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; }

    /// <summary>
    /// New Product Here on a folder of the list: a new product, as New starts one, whose Save files it in that folder's
    /// stage and category. Null, from the list's empty space, is New itself.
    /// </summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewProductInCommand { get; }

    /// <summary>New's New composite product: a new product made of other products, with its own packaging and toppings or fillings.</summary>
    public ReactiveCommand<RxVoid, RxVoid> NewCompositeCommand { get; }

    /// <summary>New Composite Product Here on a folder of the list, as New Product Here; null, from the list's empty space, is New composite product itself.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewCompositeProductInCommand { get; }

    /// <summary>The Products table's Remove Line: takes out the line selected.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveComponentCommand { get; }

    /// <summary>Rename on a product of the list: saves the new name at once, like a move (IProductRepository.RenameAsync).</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> RenameProductCommand { get; }

    /// <summary>Clone on a product of the list: asks for the new product's name, then clones it as saved, as the Clone tab does.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> CloneProductCommand { get; }

    /// <summary>Create Variant on a product of the list: asks for the variant's name, then makes it, as the Clone tab does.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> CreateVariantOfCommand { get; }

    /// <summary>Delete on a product of the list: Delete for that product, asking first.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> DeleteProductCommand { get; }

    /// <summary>Restore on a product in the Deleted folder: puts it back in its folder, and picks it.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> RestoreProductCommand { get; }

    /// <summary>Delete Permanently on a product in the Deleted folder: asks first, then deletes it for good.</summary>
    public ReactiveCommand<ProductLeafNode, RxVoid> DeletePermanentlyCommand { get; }

    /// <summary>New Category on the list: asks for its name and adds it, a folder in both stages. Its parameter is the folder asked on.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> NewCategoryCommand { get; }

    /// <summary>Rename Category on a category's folder: renames the category, and so its folder in both stages.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> RenameCategoryCommand { get; }

    /// <summary>Delete Category on a category's folder: asks first, then deletes it, leaving its products uncategorized.</summary>
    public ReactiveCommand<ProductFolderNode, RxVoid> DeleteCategoryCommand { get; }

    /// <summary>Expand All: opens every folder in the stage given, or in both.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> ExpandAllCommand { get; }

    /// <summary>Collapse All: closes every folder in the stage given, or in both, leaving the stages open.</summary>
    public ReactiveCommand<ProductFolderNode?, RxVoid> CollapseAllCommand { get; }

    /// <summary>
    /// Input: what the prompt is for, its box's label, the name it starts with, and a line saying more. Output: the name
    /// typed, or null if it was cancelled. The View shows a TextPromptWindow. Asked by the list's Rename, Clone, Create
    /// Variant, New Category and Rename Category.
    /// </summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with.
    /// Output: the name to add it as, or null if it was cancelled. The View shows a TextPromptWindow.
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

    /// <summary>Adds a sub-assembly the way the parameter says: Add Sub-Assembly's drop-down carries one item per way.</summary>
    public ReactiveCommand<UsageFormMode, RxVoid> AddUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> EditUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> MakeCopyCommand { get; }

    /// <summary>Unlink on a linked section: it becomes a copy of the product's own, in whatever role, and the link goes with Save.</summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> UnlinkCommand { get; }

    /// <summary>The linked badge's Edit: shows the picker of what the section could link instead (IsChangingLink).</summary>
    public ReactiveCommand<RxVoid, RxVoid> StartChangingLinkCommand { get; }

    /// <summary>Puts the badge back without changing the link.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelChangingLinkCommand { get; }

    /// <summary>
    /// Move to Sub-Assemblies on one of the product's own: Save puts it on the Sub-Assemblies screen, keeping its name and
    /// lines, and links it. Unlink the other way round.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> MoveToSubAssembliesCommand { get; }

    /// <summary>
    /// Move to In-House on one of the product's own: asks for the made ingredient's name and Made category, then Save moves
    /// the section to the Sub-Assemblies screen, links it there, and adds that ingredient, made from it.
    /// </summary>
    public ReactiveCommand<ProductUsageEdit, RxVoid> MoveToInHouseCommand { get; }

    /// <summary>
    /// Input: Move to In-House's prompt, its name box starting with the section's saved name and its picker on a Made
    /// category. Output: the name typed and the category picked, or null if it was cancelled. The View shows a
    /// TextPromptWindow with a picker.
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>Delete Sub-Assembly: takes the sub-assembly whose tab is showing out of the recipe.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormLineCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveFormAddInCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    /// <summary>The add form's Remove Line under its packaging table.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormPackagingLineCommand { get; }

    /// <summary>Make a Copy on a linked Packaging tab: the lines it shows become the product's own, and the link goes with Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyPackagingCommand { get; }

    /// <summary>
    /// Move to Sub-Assemblies on the Packaging tab, while its lines are the product's own: Save puts them on the
    /// Sub-Assemblies screen as a packaging set named after the product, and links it (PackagingLinkDraft.NewSet). Make a
    /// Copy the other way round.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> MovePackagingToSubAssembliesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloneCommand { get; }

    /// <summary>Create Variant on the Clone tab: a new product that links this one's recipe and packaging (IProductCloner.CreateVariantAsync).</summary>
    public ReactiveCommand<RxVoid, RxVoid> CreateVariantCommand { get; }

    /// <summary>The History tab: the product's versions, read while the tab shows (<see cref="RequestVersions"/>).</summary>
    public ProductVersionHistory VersionHistory { get; }

    /// <summary>
    /// Restore on the History tab: the picked version's recipe, packaging, costing and price into the form, as unsaved
    /// edits in one undo step (<see cref="RestoreVersionAsync"/>).
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RestoreVersionCommand { get; }

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
        IIngredientBomLineRepository ingredientBomLineRepository,
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
        _ingredientBomLineRepository = ingredientBomLineRepository;
        _rawMaterialCategoryRepository = rawMaterialCategoryRepository;
        _savedProducts = new SavedProductReader(
            packagingLinkRepository, usageRepository, packagingLineRepository, costingParametersRepository, subAssemblyLineRepository, productRepository,
            costingDefaultsRepository);
        Packaging = new ProductPackaging(FindPackagingMaterial, () => CostBasis);
        UsageForm = new ProductUsageForm(id => Ingredients.FirstOrDefault(m => m.Id == id), FindPackagingMaterial, () => CostBasis);
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
        Scaling = new ProductScaling(
            ScalingInputNow,
            () => Ingredients.ToDictionary(i => i.Id),
            () => CostBasis,
            _savedProducts,
            (ex, text) => ShowFailure(ex, text),
            Work);
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
        ProductTree = new ProductTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey, OpenFoldersKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, ProductTree.ExpansionChanged, ProductTree.ClosedFolderKeys);

        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

        // A new product starts with no tabs at all: packaging, like a recipe section, comes in through Add Sub-Assembly.
        RecipeTabs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasRecipeTabs));
        UsageEdits.CollectionChanged += (_, e) =>
        {
            MirrorUsageTabs(e);
            MarkRepeatedRoles();
        };

        var byName = SortExpressionComparer<Product>.Ascending(p => p.Name);

        _productCache.Connect()
            .SortAndBind(out _products, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<Product, bool>)(p => text.Length == 0 || p.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        // The list's tree follows both lists once each change has reached them: a product added, removed, renamed, moved or
        // filtered out, and a category added, renamed or deleted. The categories are bound first, as the tree reads both.
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe(_ => RefreshProductTree());

        _productCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleProducts, byName)
            .Subscribe(_ => RefreshProductTree());

        // The search decides whether empty folders show, even when it leaves the same products showing.
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshProductTree());

        this.WhenAnyValue(x => x.SelectedProduct)
            .Subscribe(_ => SyncTreeSelection());

        _headerTitle = this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingName, x => x.IsEditingComposite, (product, name, isComposite) =>
                product is null && string.IsNullOrWhiteSpace(name) ? (isComposite ? "New composite product" : "New product") : name)
            .ToProperty(this, x => x.HeaderTitle, "New product");

        _newProductTag = this.WhenAnyValue(x => x.SelectedProduct, x => x.NewProductStage, (product, stage) =>
                product is null ? $"Goes in {ProductRules.StageTitle(stage)}" : null)
            .ToProperty(this, x => x.NewProductTag);

        // A hidden tab can't stay the one showing: leaving a saved product while on Clone or History goes
        // back to Recipe. Subscribed before ShowLinks, so the tab strip moves before the tab hides; a
        // TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is null && SelectedDetailsTab is LinksTab or HistoryTab)
            .Subscribe(_ => SelectedDetailsTab = RecipeTab);

        // The History tab reads the product's versions while it shows, once per product: as it's picked, and as another
        // product's details arrive under it. A Save reads them again (RequestVersions(force: true)).
        this.WhenAnyValue(x => x.SelectedDetailsTab, x => x.AreDetailsReady, x => x.SelectedProduct)
            .Subscribe(_ => RequestVersions());
        LoadLatest(_versionReads, (productId, cancellationToken) => VersionHistory.ReadAsync(
            productId, () => SelectedProduct?.Id == productId, cancellationToken));

        // The Scaling tab is worked out while it shows, and again as anything it scales changes: every change to the
        // recipe, the packaging, a composite's products, the quantity required or the cost basis gives new Costs.
        this.WhenAnyValue(x => x.SelectedDetailsTab).Subscribe(tab => Scaling.IsShowing = tab == ScalingTab);
        this.WhenAnyValue(x => x.Costing.Costs).Subscribe(_ => Scaling.Refresh());
        // The list's flags for a price short of its target markup, after each load: a newer load cancels one still working.
        LoadLatest(_markupChecks, FlagBelowTargetMarkupAsync);


        _showLinks =this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowLinks);

        _isUsageFormShowing = this.WhenAnyValue(
                x => x.SelectedRecipeTab,
                x => x.UsageForm.IsUsageFormOpen,
                (tab, isFormOpen) => tab is SubAssemblyRecipeTab || (tab is AddSubAssemblyRecipeTab && isFormOpen))
            .ToProperty(this, x => x.IsUsageFormShowing);

        _isPackagingShowing = this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Select(tab => tab is PackagingRecipeTab)
            .ToProperty(this, x => x.IsPackagingShowing);

        _isComponentsShowing = this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Select(tab => tab is ComponentsRecipeTab)
            .ToProperty(this, x => x.IsComponentsShowing);

        // Nothing is recorded until a product's details have arrived, or while the screen is being written to
        // wholesale: a load merging in fresh data and a restore putting a form back are neither of them edits.
        // Set up before the subscriptions below rather than beside the Undo and Redo commands: the selection ones
        // run as they're wired, with nothing selected, and the load that fires takes the blank form as the
        // settled one - a history that didn't exist yet would take the whole subscription down with it.
        History = new UndoHistory<ProductFormSnapshot>(
            CaptureSnapshot,
            (before, after) => before.ShowsSameAs(after),
            () => AreDetailsReady && !IsBusyApplying,
            (settled, now) => settled.SavesSameAs(now));

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

        this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Subscribe(RecipeTabPicked);

        // The sub-assembly form: where its sub-assembly comes from, then the one it links or copies. The form starts
        // again empty in its new way, which is nothing to save yet, as StartAddingUsage's fresh form isn't. Picks only
        // (ProductUsageForm.ModePicks and the rest): an undo putting the form back, or the options rebuilt, isn't one.
        UsageForm.ModePicks
            .Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage)
            .Subscribe(_ =>
            {
                StartUsageFormMode();
                RebaseSettledForm();
            });

        // Every row put in a lines table, however it got there (a product read, an undo, a form opened), is priced at the
        // screen's costs, buffered or not; the add rows' factories see to a row still being typed.
        UsageForm.FormLines.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        UsageForm.FormAddIns.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        UsageForm.FormPackagingLines.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        Packaging.LineEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();

        // A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.
        UsageForm.FormLines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .AutoRefresh(l => l.IsFormulaBase)
            .Subscribe(_ => _updates.Run(FormLinesEdited));

        // An add-in picked, typed or deleted: it's priced, and the batch it joins follows.
        UsageForm.FormAddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => _updates.Run(FormAddInsEdited));

        // A packaging line picked, typed or deleted in the add form: it's priced, and the preview follows.
        UsageForm.FormPackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(FormPackagingEdited));

        // Whether the form adds packaging follows its mode, role and source.
        this.WhenAnyValue(x => x.UsageForm.IsAddingUsage, x => x.UsageForm.EditingUsageMode, x => x.UsageForm.EditingUsageRole, x => x.UsageForm.EditingUsageSource)
            .Subscribe(_ => _updates.Run(FormAddsChanged));

        LoadLatest(
            UsageForm.SourcePicks.Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage),
            LoadUsageSourceAsync);

        // A sub-assembly picked to link instead relinks the section once its lines are in; a later pick overtakes it.
        LoadLatest(
            UsageForm.LinkTargetPicks
                .Where(target => target is not null && UsageForm.IsChangingLink)
                .Select(target => target!),
            ChangeLinkAsync);

        // The add form's piece size starts as its role has it (StartingPieceSize), so it follows a role picked, or a
        // copy's taken, while it's still what the role before started it at: neither figure typed.
        UsageForm.RolePicks
            .Where(pick => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage && IsPieceSizeAsStarted(pick.Before))
            .Subscribe(_ => StartPieceSize());

        // A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.
        Packaging.LineEdits.ToObservableChangeSet()
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

        Costing.WhenAnyValue(
                c => c.EditingLaborPercent,
                c => c.EditingOverheadPercent,
                c => c.EditingAdministrativePercent,
                c => c.EditingMarginPercent)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // The category too: it can set the Wastage % the product is priced with.
        this.WhenAnyValue(x => x.EditingCurrentPrice, x => x.EditingQuantityRequired, x => x.EditingCategory)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // A composite's products' packaging joining its own, or leaving it.
        this.WhenAnyValue(x => x.EditingIncludesComponentPackaging)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // The caption on every figure names what it's for, so it follows the quantity, and a composite's is always one unit.
        this.WhenAnyValue(x => x.EditingQuantityRequired, x => x.IsEditingComposite)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(CostUnitCaption));
                this.RaisePropertyChanged(nameof(SuggestedSellingPriceLabel));
                this.RaisePropertyChanged(nameof(MaterialCostTooltip));
                this.RaisePropertyChanged(nameof(MetricBaseTooltip));
            });

        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, History.WhenAnyValue(h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, History.WhenAnyValue(h => h.CanRedo));

        // Save offers itself only when the form holds something the saved product doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        Costing.Changed.Subscribe(_ => History.RefreshDirty());
        Packaging.Changed.Subscribe(_ => History.RefreshDirty());
        ComponentsTable.Changed.Subscribe(_ => History.RefreshDirty());
        UsageForm.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(UsageEdits);
        WatchForSave(Packaging.LineEdits);
        WatchForSave(ComponentsTable.ComponentEdits);
        WatchForSave(UsageForm.FormLines);
        WatchForSave(UsageForm.FormAddIns);
        WatchForSave(UsageForm.FormPackagingLines);
        ((INotifyPropertyChanged)RecipeTabs).PropertyChanged += (_, _) => RebaseSettledForm();

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere: a repriced ingredient, or a linked sub-assembly its owner
        // changed.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // A linked section reads a sub-assembly kept on the Sub-Assemblies screen, which can change there (its
            // lines, name or role), so those are fetched again; the product's own and new sections only need their
            // ingredients' new costs. Every saved one is fetched: a role made product-owned, or no longer, in Settings
            // turns a linked one into the product's own, or back.
            var detailProductId = _detailProductId;
            HashSet<int> linkedIds = [.. UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId)];

            // A composite's products can have changed elsewhere since they were read (a recipe, an ingredient's price, a
            // quantity required), so what each brings is read again; its lines may hold unsaved edits and stay as they are.
            List<int> componentIds = [.. ComponentsTable.ProductIds];

            // All at once, on the thread pool: each read is mostly a round trip's wait, so one after another they took
            // several times as long as the slowest of them, and the UI thread came back after each to start the next.
            // Each has its own context (AddDbContextFactory), and none touches ViewModel state.
            var rolesRead = Task.Run(() => subAssemblyRoleRepository.GetAllAsync(CancellationToken.None));
            var categoriesRead = Task.Run(() => _categoryRepository.GetAllAsync(CancellationToken.None));
            var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(CancellationToken.None));
            var productsRead = Task.Run(() => _productRepository.GetAllAsync(CancellationToken.None));
            var deletedRead = Task.Run(() => _productRepository.GetDeletedAsync(CancellationToken.None));
            var linkableRead = Task.Run(() => _subAssemblyRepository.GetLinkableAsync(CancellationToken.None));
            var standaloneRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
            var costingDefaultsRead = Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var linkedSubAssembliesRead = Task.Run(() => _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None));
            var linkedLinesRead = Task.Run(() => _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None));
            var packagingLinkRead = detailProductId is { } linkProductId
                ? Task.Run(() => _packagingLinkRepository.GetForProductAsync(linkProductId, CancellationToken.None))
                : Task.FromResult<PackagingLink?>(null);
            var componentLinksRead = Task.Run(() => _productRepository.GetAllComponentsAsync(CancellationToken.None));
            var componentCostsRead = Task.Run(() => _savedProducts.ReadComponentCostsAsync(componentIds, CancellationToken.None));
            var bomLinesRead = Task.Run(() => _ingredientBomLineRepository.GetAllAsync(CancellationToken.None));
            await Task.WhenAll(
                rolesRead, categoriesRead, rawMaterialsRead, productsRead, deletedRead, linkableRead, standaloneRead, costingDefaultsRead,
                linkedSubAssembliesRead, linkedLinesRead, packagingLinkRead, componentLinksRead, componentCostsRead, bomLinesRead);

            var roles = await rolesRead;
            var categories = await categoriesRead;
            var rawMaterials = await rawMaterialsRead;
            var products = await productsRead;
            var deleted = await deletedRead;
            var linkable = await linkableRead;
            var packagingSets = (await standaloneRead).Where(s => s.Role?.IsPackaging == true).ToList();
            var costingDefaults = await costingDefaultsRead;
            var linkedSubAssemblies = await linkedSubAssembliesRead;
            var linkedLines = await linkedLinesRead;
            var packagingLink = await packagingLinkRead;
            var componentLinks = await componentLinksRead;
            var componentCosts = await componentCostsRead;
            var bomLines = await bomLinesRead;

            // Linked packaging is kept elsewhere (another product, or a set on the Sub-Assemblies screen) and can
            // have changed there, so it's fetched again — but only while the form still shows the link as saved. The
            // product's own lines aren't, since they may hold unsaved edits, and nor is a link picked but not saved.
            var savedLink = packagingLink is null ? null : PackagingLinkTarget.From(packagingLink);
            var showsSavedLink = savedLink is not null && Packaging.ShowsSavedLink(savedLink);
            var linkedPackagingLines = showsSavedLink
                ? await _packagingLineRepository.GetEffectiveForProductAsync(packagingLink!.ProductId, CancellationToken.None)
                : null;

            // A role made product-owned in Settings, or no longer, since the product was loaded: a section it linked is its
            // own now, or one of its own is linked now. With nothing typed into the form, the product is read again, so what
            // Save compares against is how it's saved now; with something typed, RefreshRecipeRows turns the section round
            // in place, and the next Save writes it as it shows.
            var ownershipChanged = UsageEdits.Any(u => linkedSubAssemblies.FirstOrDefault(s => s.Id == u.SubAssemblyId) is { } saved
                && (saved.OwnerProductId is not null && saved.OwnerProductId == detailProductId) != u.IsOwned);
            var reloadDetails = ownershipChanged && !History.IsDirty && detailProductId is not null;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedProduct?.Id;
            var categoryId = EditingCategory?.Id;
            var usageRoleId = UsageForm.EditingUsageRole?.Id;
            var usageSourceKey = UsageForm.EditingUsageSource?.Key;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                _categoryCache.MergeFrom(categories);
                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                // The Deleted folder first, so the tree, refreshed as the products merge, files a product deleted or
                // restored since in its new place in one go. The tree is refreshed again for a change to Deleted alone.
                DeletedProducts = deleted;
                _productCache.MergeFrom(products);
                RefreshProductTree();
                _markupChecks.OnNext(products);
                _linkable = linkable;
                _packagingSets = packagingSets;
                RefreshCostingDefaults(costingDefaults);

                // A composite's lines point at the fresh products (a rename shows) at their fresh recipe costs.
                ComponentsTable.TakeFresh(componentCosts, componentLinks);
                RefreshComponentOptions();

                // The Scaling tab opens made ingredients into their BOMs as they are now, and a composite's products
                // into their recipes as they're saved now, which it reads again when it next shows them.
                Scaling.TakeLoaded(bomLines.ToLookup(l => l.IngredientId));

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

                RefreshRecipeRows(linkedSubAssemblies, linkedLines);
                RefreshFormLines();
                RefreshLinkableOptions();

                // An add form opened before the roles were loaded takes the first role it can have, as it would have.
                UsageForm.ShowRole(UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usageRoleId)
                    ?? (usageRoleId is null && UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage ? UsageForm.FormRoleOptions.FirstOrDefault() : null));
                UsageForm.ShowSource(UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == usageSourceKey));
                PackagingLineRow.Resolve(UsageForm.FormPackagingLines, PackagingMaterials.ToDictionary(m => m.Id));

                UsageForm.Recompute();
                RecomputeCosts();
            });

            if (reloadDetails && SelectedProduct is { } reloaded && reloaded.Id == detailProductId)
            {
                await LoadProductDetailAsync(reloaded, CancellationToken.None);
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the product's form.
            if (_productToOpen is { } productId)
            {
                _productToOpen = null;
                TrySelectProduct(productId);
            }

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
        }));

        // A new product replaces the one showing, so its unsaved changes are asked about first.
        NewCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewProduct()));
        NewCompositeCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewProduct(isComposite: true)));
        NewCompositeProductInCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(folder => AfterUnsavedChangesAsync(() => NewProduct(
            folder?.Stage, folder?.CategoryId, isComposite: true)));
        RemoveComponentCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => ComponentsTable.ComponentEdits.Remove(ComponentsTable.SelectedComponentEdit!)),
            ComponentsTable.WhenAnyValue(c => c.SelectedComponentEdit).Select(row => row is not null));

        // The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor).
        MoveProductsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<ProductMove>>(
            moves => RunSafelyAsync(() => MoveProductsAsync(moves, isUndo: false)));
        ApplyCostingDefaultsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<Product>>(ApplyCostingDefaultsAsync);
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveProductsAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewProductInCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(folder => AfterUnsavedChangesAsync(
            () => NewProduct(folder?.Stage, folder?.CategoryId)));
        RenameProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(RenameProductAsync);
        CloneProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Clone Product", "Name", $"{leaf.Product.Name} Copy", $"A copy of {leaf.Product.Name} as last saved, filed in Research and Development."),
            _productCloner.CloneAsync,
            ClonedFrom));
        CreateVariantOfCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Create Variant", "Name", $"{leaf.Product.Name} Copy", $"A new product that links {leaf.Product.Name}'s recipe and packaging, filed in Research and Development."),
            _productCloner.CreateVariantAsync,
            VariantOf));
        DeleteProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeleteProductAsync(leaf.Product));
        RestoreProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => RestoreProductAsync(leaf.Product));
        DeletePermanentlyCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeletePermanentlyAsync(leaf.Product));
        NewCategoryCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(NewCategoryAsync);
        RenameCategoryCommand = ReactiveCommand.CreateFromTask<ProductFolderNode>(RenameCategoryAsync);
        DeleteCategoryCommand = ReactiveCommand.CreateFromTask<ProductFolderNode>(DeleteCategoryAsync);
        ExpandAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.ExpandAll);
        PrintFolderCommand = ReactiveCommand.Create<ProductFolderNode>(folder => _printRequests.OnNext(new PrintScope.Folder(folder)));
        PrintAllCommand = ReactiveCommand.Create(() => _printRequests.OnNext(new PrintScope.AllProducts()));
        CollapseAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.CollapseAll);

        OpenSubAssemblyCommand = ReactiveCommand.Create<int>(_subAssemblyOpenRequests.OnNext);
        OpenLinkedSourceCommand = ReactiveCommand.Create<ProductUsageEdit>(usage =>
        {
            if (usage.LinkedOwner is { } owner)
            {
                OpenProduct(owner.Id);
            }
            else
            {
                _subAssemblyOpenRequests.OnNext(usage.SubAssemblyId);
            }
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
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
            if (UsageForm.IsUsageFormOpen && !IsUsageFormUntouched())
            {
                SelectRecipeTab(ApplyForm());
            }

            var product = new Product
            {
                Id = _editingId,
                Name = EditingName.Trim(),
                Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
                CategoryId = EditingCategory?.Id,
                QuantityRequired = EditingQuantityRequired,
                CurrentPrice = EditingCurrentPrice,

                // Only a new product's is written: a saved one's changes by moving it in the list.
                Stage = NewProductStage,

                // A new one's to say; a saved one's is the kind it was saved as, which the form never changes.
                IsComposite = IsEditingComposite,
                IncludesComponentPackaging = IsEditingComposite && EditingIncludesComponentPackaging,
            };

            var validation = _productValidator.Validate(product);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var costing = Costing.ToParameters();
            var costingValidation = _costingParametersValidator.Validate(costing);
            if (!costingValidation.IsValid)
            {
                ErrorMessage = string.Join(" ", costingValidation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var recipe = CurrentRecipe();
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
            var changesSharing = UsageEdits.Any(u => u.AreLinkedLinesEdited) || packagingLines is not null || packagingLink is not null;
            var recipeToSave = IsSameRecipe(recipe, _savedRecipe) ? null : recipe;
            var costingToSave = Costing.IsChanged ? costing : null;
            var saved = await Task.Run(() => _versionedSaves.SaveProductAsync(
                product, recipeToSave, packagingLink, packagingLines, costingToSave, changesSharing, DateTime.Now, CancellationToken.None));

            // What's saved is the new starting point: stepping back past it would show changes already written.
            // Save's own reload keeps the product selected, so ClearDetails doesn't run and wouldn't clear this.
            History.Clear();

            // The list picks up the new name; then the form shows what was saved, which is what the next Save compares
            // against.
            await LoadCommand.Execute();
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
        }),
        History.WhenAnyValue(h => h.IsDirty));

        // Revert puts the form back as the product was last saved (RevertToSavedAsync), once it's confirmed.
        RevertCommand = ReactiveCommand.CreateFromTask(() => RunWithConfirmationAsync(
            "Undo every change made since this product was last saved?",
            async () =>
            {
                await RevertToSavedAsync();
                if (SelectedProduct is { } product)
                {
                    SuccessMessage = $"{product.Name} was put back as it was last saved.";
                }
            }));

        // Restore puts the picked version into the form, as Revert puts the saved product there, but as an undo step of
        // unsaved edits rather than by reloading: Save keeps it, and Undo takes it back.
        RestoreVersionCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() => VersionHistory.SelectedRow is { } row ? RestoreVersionAsync(row) : Task.CompletedTask),
            VersionHistory.WhenAnyValue(h => h.SelectedRow).Select(row => row is not null));

        // Print shows the product as the screen does, unsaved edits and all. A form left
        // open is part of that, as it's part of what Save writes, so it's applied first; a rule refusing it shows its
        // message, and the Print tab doesn't open. It stays enabled while a picked product's details are on their way,
        // and does nothing if clicked then: disabled for that moment, it flickered on every pick. (Save is off then
        // anyway, with nothing unsaved, so it doesn't flicker.)
        PrintCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() =>
            {
                if (!AreDetailsReady)
                {
                    return Task.CompletedTask;
                }

                // On the History tab it prints the version picked there (PreparePrintout), so the form isn't applied: its
                // pages aren't the form's.
                if (SelectedDetailsTab == HistoryTab)
                {
                    if (VersionHistory.SelectedRow is null)
                    {
                        ErrorMessage = "Pick a version in the History tab's table to print it.";
                        return Task.CompletedTask;
                    }
                }
                else
                {
                    ApplyOpenForm();
                }

                _printRequests.OnNext(new PrintScope.ThisProduct());
                return Task.CompletedTask;
            }));

        ToggleCostBasisCommand = ReactiveCommand.Create(ToggleCostBasis);

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => DeleteProductAsync(SelectedProduct!),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        // Add Sub-Assembly opens a New Sub-Assembly tab, after the sub-assemblies' and before Packaging's, with a fresh
        // add form in it, which also adds the packaging (the Packaging role, or a packaging source to link or copy). The form
        // showing is applied first, as leaving its tab would apply it; one a rule refuses keeps its tab, with the
        // message, and nothing opens. The tab goes again once another shows (RecipeTabShown).
        //
        // Its parameter is the way in, one per item on the drop-down. The tab is shown first and the mode set after:
        // showing it starts the form in New (RecipeTabShown), and the mode landing on it starts the form again for a
        // Link or a Copy (the EditingUsageMode subscription), so one path serves all three, in one undo step.
        //
        // The button stays live on the add tab, since the drop-down is the only place the way in can be changed now
        // that the form has no picker of its own. An item picked over an add form nothing was entered in only changes
        // its way, keeping the tab; over one already typed into it does what a second Add Sub-Assembly always did —
        // applies what's there and starts another, in the way picked.
        AddUsageCommand = ReactiveCommand.Create<UsageFormMode>(
            mode =>
            {
                if (SelectedRecipeTab is AddSubAssemblyRecipeTab && IsUsageFormUntouched())
                {
                    AsUndoStep(() => UsageForm.EditingUsageMode = mode);
                    return;
                }

                if (!CommitUsageFormOnLeave())
                {
                    return;
                }

                AsUndoStep(() =>
                {
                    InsertAddSubAssemblyTab();
                    SelectRecipeTab(AddSubAssemblyTab);
                    UsageForm.EditingUsageMode = mode;
                });
            });

        // A sub-assembly's tab is its form, so editing one is just showing its tab (RecipeTabShown opens the form).
        EditUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => SelectRecipeTab(TabFor(usage)));

        // A linked sub-assembly becomes a new section in the same role and at the same piece size, whose lines the form
        // edits; Save makes it the product's own in a product-owned role, and otherwise keeps it on the Sub-Assemblies
        // screen as a sub-assembly of its own, and links that instead.
        MakeCopyCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() => ReplaceUsage(usage, CopyOfLinked(usage, isOwn: false))));

        // The same copy, but the product's own whatever its role: Save keeps it with the product, off the
        // Sub-Assemblies screen, and the one it was linked to stays there as it is.
        UnlinkCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() => ReplaceUsage(usage, CopyOfLinked(usage, isOwn: true))));

        StartChangingLinkCommand = ReactiveCommand.Create(StartChangingLink, this.WhenAnyValue(x => x.UsageForm.IsEditingLinkedUsage));
        CancelChangingLinkCommand = ReactiveCommand.Create(() => { UsageForm.IsChangingLink = false; });

        // Anything typed into its form goes in first, so what moves is what shows; a form a rule refuses stays as it
        // is, with the message. It's still edited here until Save, which moves it and links it.
        MoveToSubAssembliesCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() =>
        {
            var wasEditing = ReferenceEquals(usage, UsageForm.EditingUsage);
            var applied = usage;
            if (wasEditing)
            {
                try
                {
                    ErrorMessage = null;
                    applied = ApplyUsageForm();
                }
                catch (BusinessRuleException ex)
                {
                    ErrorMessage = ex.Message;
                    return;
                }
            }

            var moving = applied.Released();
            ReplaceInRecipeOrder(applied, moving);
            RecomputeCosts();
            if (wasEditing)
            {
                OpenUsageForm(moving);
            }
        }));

        MoveToInHouseCommand = ReactiveCommand.CreateFromTask<ProductUsageEdit>(usage => RunSafelyAsync(() => MoveToInHouseAsync(usage)));


        // Delete Sub-Assembly takes out the one whose tab is showing. Nothing to confirm: it only leaves the recipe,
        // with Save, and the sub-assembly stays on the Sub-Assemblies screen. On the Packaging tab it takes out the
        // packaging: its lines, or its link, go with Save.
        RemoveUsageCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() =>
            {
                if (SelectedRecipeTab is PackagingRecipeTab)
                {
                    AsUndoStep(RemovePackaging);
                    return Task.CompletedTask;
                }

                var usage = ((SubAssemblyRecipeTab)SelectedRecipeTab!).Usage;
                AsUndoStep(() =>
                {
                    // Whatever was typed into its form goes with it, rather than being applied on the way out.
                    if (ReferenceEquals(usage, UsageForm.EditingUsage))
                    {
                        CloseUsageForm();
                    }

                    // Its neighbour's tab shows instead: the next sub-assembly's, else the one before, else a composite's
                    // Products, else Packaging.
                    var index = UsageEdits.IndexOf(usage);
                    var neighbour = index + 1 < UsageEdits.Count ? UsageEdits[index + 1] : index > 0 ? UsageEdits[index - 1] : null;
                    SelectRecipeTab(neighbour is null ? ComponentsTabIfShown ?? PackagingTabIfShown : TabFor(neighbour));

                    UsageEdits.Remove(usage);
                    RefreshLinkableOptions();
                    RecomputeCosts();
                });

                return Task.CompletedTask;
            }),
            this.WhenAnyValue(x => x.SelectedRecipeTab).Select(tab => tab is SubAssemblyRecipeTab or PackagingRecipeTab));

        RemoveFormLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => UsageForm.FormLines.Remove(UsageForm.SelectedFormLine!)),
            this.WhenAnyValue(x => x.UsageForm.SelectedFormLine).Select(line => line is not null));

        RemoveFormPackagingLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => UsageForm.FormPackagingLines.Remove(UsageForm.SelectedFormPackagingLine!)),
            this.WhenAnyValue(x => x.UsageForm.SelectedFormPackagingLine).Select(line => line is not null));

        // The linked packaging's lines, as they show, become the product's own; Save drops the link and writes them.
        CopyPackagingCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                Packaging.ShowLink(null);
                RefreshLinkableOptions();
            }),
            Packaging.WhenAnyValue(p => p.IsPackagingLinked));

        // The product's own packaging lines go to the Sub-Assemblies screen as a packaging set on Save, which it links.
        // They're typed straight into the tab rather than through a form, so they're checked now instead: a line half
        // filled in, or none at all, is refused with the message, as a recipe section's form a rule refuses is. They're
        // still typed into until Save.
        MovePackagingToSubAssembliesCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                ErrorMessage = null;
                int lineCount;
                try
                {
                    lineCount = Packaging.CurrentLines().Count;
                }
                catch (BusinessRuleException ex)
                {
                    ErrorMessage = ex.Message;
                    return;
                }

                if (lineCount == 0)
                {
                    ErrorMessage = "Add the packaging materials one piece takes first: a packaging set needs at least one.";
                    return;
                }

                Packaging.IsPackagingMoving = true;
            }),
            Packaging.WhenAnyValue(p => p.CanMovePackaging));

        RemoveFormAddInCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => UsageForm.FormAddIns.Remove(UsageForm.SelectedFormAddIn!)),
            this.WhenAnyValue(x => x.UsageForm.SelectedFormAddIn).Select(line => line is not null));

        RemoveLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => Packaging.LineEdits.Remove(Packaging.SelectedLineEdit!)),
            Packaging.WhenAnyValue(p => p.SelectedLineEdit, p => p.IsPackagingLinked, (line, linked) => line is not null && !linked));

        // A clone copies the product as saved, and a variant links it as saved: unsaved edits stay with this one.
        var canMakeFromSelected = this.WhenAnyValue(
            x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name));
        CloneCommand = ReactiveCommand.CreateFromTask(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CloneAsync, ClonedFrom), canMakeFromSelected);
        CreateVariantCommand = ReactiveCommand.CreateFromTask(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CreateVariantAsync, VariantOf), canMakeFromSelected);
    }

    /// <summary>
    /// A linked section as a new one whose lines the form edits, at the same role and piece size (Make a Copy, Unlink):
    /// <paramref name="isOwn"/> makes it the product's own whatever its role.
    /// </summary>
    private static ProductUsageEdit CopyOfLinked(ProductUsageEdit usage, bool isOwn) =>
        // Its add-ins become plain lines of the copy: the batch it was showing is the batch it keeps.
        // Never a duplicate ingredient, since an add-in can't be one the recipe already has.
        // In a role the recipe has more than one in, it keeps what it went by, so it's still told apart.
        new(0, usage.Role, [.. usage.Lines, .. usage.AddIns], usage.AuthoredField, usage.AuthoredValue,
            linkedName: null, name: usage.IsRoleRepeated ? usage.Label : null, isOwn: isOwn);

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

            await LoadCommand.Execute();
            SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
        });
    }

    /// <summary>The list's Clone and Create Variant: asks for the new product's name first, starting from "… Copy".</summary>
    private async Task MakeFromWithPromptAsync(
        ProductLeafNode leaf, TextPromptRequest prompt, Func<int, string, CancellationToken, Task<int>> make, string madeAs)
    {
        var name = (await PromptForName.Handle(prompt))?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            await MakeFromAsync(leaf.Product.Id, name, make, madeAs);
        }
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

                await LoadCommand.Execute();
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
            await LoadCommand.Execute();
            TrySelectProduct(product.Id);
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
                await LoadCommand.Execute();
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
        EditingIncludesComponentPackaging = product.IncludesComponentPackaging;
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
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;

        if (product?.Id != _detailProductId)
        {
            ClearDetails();
        }
    }

    /// <summary>
    /// What ResetDetails leaves for the picked product's details to replace: the previous product's. Its recipe's tabs
    /// stay for the picked recipe's sections to take over (ReplaceUsages), unless nothing is coming to replace them.
    /// </summary>
    private void ClearDetailsForAnother(bool keepRecipeTabs)
    {
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        VersionHistory.Clear();
        ClearDetails(keepRecipeTabs);
    }

    /// <summary>
    /// Opens a product as picking it in the list would, for another screen (Sub-Assemblies' Used By tiles). A search
    /// hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not loaded
    /// since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenProduct(int productId)
    {
        _productToOpen = TrySelectProduct(productId) ? null : productId;
    }

    /// <summary>
    /// Opens a product at the end of the next LoadCommand, for the one in the screen's address as the screen opens: its
    /// list is fresh only then. New clicked meanwhile is the later word, and the product isn't opened over it.
    /// </summary>
    public void OpenProductOnLoad(int productId)
    {
        _productToOpen = productId;
    }

    /// <summary>
    /// <see cref="OpenProduct"/>, asking first about the unsaved changes of the product showing when another is to replace
    /// it; a deleted one is only picked in the Deleted folder, and replaces nothing. False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenProductAsync(int productId)
    {
        if (productId != SelectedProduct?.Id
            && DeletedProducts.All(p => p.Id != productId)
            && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenProduct(productId);
        return true;
    }

    /// <summary>
    /// Starts a new product that links a standalone sub-assembly, for Sub-Assemblies' New Product. It waits for the next
    /// LoadCommand, which showing the screen runs, so the sub-assembly is found as it's saved now: it may have been made or
    /// renamed there since this screen last loaded.
    /// </summary>
    public void StartNewProductFrom(int subAssemblyId)
    {
        _productToOpen = null;
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

    public bool HasUnsavedChanges => History.IsDirty;

    /// <inheritdoc/>
    public Task<bool> ResolveUnsavedChangesAsync(bool revert) => AskAboutUnsavedChangesAsync(
        History.IsDirty,
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim()
            : SelectedProduct?.Name ?? (IsEditingComposite ? "the new composite product" : "the new product"),
        async () =>
        {
            await SaveCommand.Execute();
            return !History.IsDirty;
        },
        revert ? RevertToSavedAsync : null);

    /// <summary>Runs <paramref name="then"/>, which replaces the product showing, once its unsaved changes are saved or thrown away.</summary>
    private async Task AfterUnsavedChangesAsync(Action then)
    {
        if (await ResolveUnsavedChangesAsync(revert: false))
        {
            then();
        }
    }

    /// <summary>
    /// Puts the form back as the product was last saved: Revert, and Don't Save on leaving the screen. It reads the picked
    /// product's detail again, which replaces the recipe, packaging, packs and costing wholesale, so anything staged but
    /// unsaved goes: a section added or edited in the form included, since the form has no discard of its own. A new
    /// product was never saved, so it goes back to being a blank one.
    /// </summary>
    private async Task RevertToSavedAsync()
    {
        if (SelectedProduct is not { } product)
        {
            StartNewProduct(_newProductInDefaultStage ? null : NewProductStage, _newProductCategoryId, IsEditingComposite);
            return;
        }

        History.Clear();
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        LoadIntoForm(product);
        await LoadProductDetailAsync(product, CancellationToken.None);
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
        InsertAddSubAssemblyTab();
        SelectRecipeTab(AddSubAssemblyTab);
        UsageForm.EditingUsageMode = UsageFormMode.Link;
        UsageForm.EditingUsageSource = UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.SubAssembly?.Id == subAssemblyId);
        History.RefreshDirty();
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the product the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(Products, product => product.Name, text) is not { } product)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenProductAsync(product.Id));
        return true;
    }

    private bool TrySelectProduct(int productId)
    {
        // A deleted one (a Used By tile names it as deleted) has its row picked in the Deleted folder, opened; the form stays.
        if (DeletedProducts.Any(p => p.Id == productId))
        {
            if (ProductTree.Find(productId) is null)
            {
                SearchText = string.Empty;
            }

            ProductTree.DeletedFolder.IsExpanded = true;
            SelectedTreeNode = ProductTree.Find(productId);
            return true;
        }

        if (Products.FirstOrDefault(p => p.Id == productId) is not { } product)
        {
            return false;
        }

        if (!VisibleProducts.Contains(product))
        {
            SearchText = string.Empty;
        }

        SelectedProduct = product;
        return true;
    }

    private void RefreshProductTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its product the one picked.
        var search = SearchText.Trim();
        _treeSelection.Quietly(() => ProductTree.Refresh(
            Categories,
            VisibleProducts,
            isFiltered: search.Length > 0,
            [.. DeletedProducts.Where(p => search.Length == 0 || p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))]));

        // A folder picked stays picked, and so does a deleted product, which the form never shows. Otherwise the row goes
        // back to the product picked: a product moved to another folder leaves the tree's selection as it goes, and one a
        // search hid gets its row back when the search ends.
        if (SelectedTreeNode is not (ProductFolderNode or ProductLeafNode { IsDeleted: true }))
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// Puts SelectedProduct's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone, unless
    /// it's picked already, when the rows picked with it stay. Nothing picked, or a search hiding it, picks no row.
    /// </summary>
    private void SyncTreeSelection() =>
        _treeSelection.BringFirst(SelectedProduct is { } product ? ProductTree.Find(product.Id) : null);

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a product's row picked by the user picks the product.
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(nameof(SelectedTreeProducts));
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the product leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        // A deleted product's row is picked as a folder's is, leaving the form as it is: it's there to restore.
        if (!_treeSelection.IsSyncing && first is ProductLeafNode { IsDeleted: false } leaf && leaf.Product.Id != SelectedProduct?.Id)
        {
            if (History.IsDirty)
            {
                _ = RunSafelyAsync(() => PickAfterAskingAsync(leaf.Product.Id));
            }
            else
            {
                SelectedProduct = leaf.Product;
            }
        }
    }

    /// <summary>
    /// Another product's row picked while the form holds unsaved changes: the list goes back to the row of the product
    /// showing, and the one picked opens once the changes are saved or thrown away.
    /// </summary>
    private async Task PickAfterAskingAsync(int productId)
    {
        // The tree calls TreeSelectionChanged in the middle of changing its selection: going on once that's over lets the
        // list take the row of the product showing back.
        await Task.Yield();
        SyncTreeSelection();
        await OpenProductAsync(productId);
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
            await LoadCommand.Execute();

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

    private async Task MoveProductsAsync(IReadOnlyList<ProductMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _productCache.Lookup(m.ProductId))
            .Where(found => found.HasValue)
            .Select(found => new ProductMove(found.Value.Id, found.Value.Stage, found.Value.CategoryId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _productRepository.MoveAsync(moves, CancellationToken.None);
        var moved = await _productRepository.GetByIdsAsync([.. moves.Select(m => m.ProductId)], CancellationToken.None);

        // The list files them in their new folders. A selection is only re-pointed at the fresh copy, as a load re-points
        // it: the form isn't loaded again, so whatever's typed into it stays.
        var onForm = moved.FirstOrDefault(p => p.Id == _editingId);
        ApplyRefresh(() =>
        {
            _productCache.AddOrUpdate(moved);
            if (SelectedProduct is { } selected && moved.FirstOrDefault(p => p.Id == selected.Id) is { } fresh)
            {
                RepointSelection(fresh);
            }

            if (onForm is not null)
            {
                EditingCategory = Categories.FirstOrDefault(c => c.Id == onForm.CategoryId);
                ShowUpdatedAt(onForm.UpdatedAt);
            }
        });

        // The new category is saved, not typed: nothing for Save to write, and no undo step that puts the old one back.
        if (onForm is not null)
        {
            History.RewriteAll(snapshot => snapshot with { CategoryId = onForm.CategoryId });
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        _treeSelection.Restore(picked, r => r is not ProductLeafNode leaf || ProductTree.Find(leaf.Product.Id) == leaf);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Pandesal to Production › Breads", "Moved 3 products to Production ›
    /// Breads", "Moved 3 products to Production" (a Promote, each keeping its category), or "Moved 3 products".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<ProductMove> moves)
    {
        var what = moves.Count == 1 && _productCache.Lookup(moves[0].ProductId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} products";
        var first = moves[0];
        if (moves.All(m => m.Stage == first.Stage && m.CategoryId == first.CategoryId))
        {
            var folder = first.CategoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id)?.Name : ProductRules.UncategorizedTitle;
            return $"Moved {what} to {ProductRules.StageTitle(first.Stage)} › {folder}";
        }

        return moves.All(m => m.Stage == first.Stage)
            ? $"Moved {what} to {ProductRules.StageTitle(first.Stage)}"
            : $"Moved {what}";
    }

    /// <summary>
    /// New, from the header or the list. A product still waiting for LoadCommand to open it, or a sub-assembly waiting
    /// for it to start a product from, no longer is: it would load over the new form, and a name typed into it since
    /// would then be saved as that product's.
    /// </summary>
    private void NewProduct(ProductStage? stage = null, int? categoryId = null, bool isComposite = false)
    {
        _productToOpen = null;
        _subAssemblyToStartFrom = null;
        StartNewProduct(stage, categoryId, isComposite);
    }

    /// <summary>
    /// A new product: no name, recipe, packaging or links, its costing at the defaults, and nothing selected. Save files it
    /// in <paramref name="stage"/>, or Settings' stage for a new product when it's null, and <paramref name="categoryId"/>,
    /// which its Category picker starts on. <paramref name="isComposite"/> makes it a composite, which opens on its empty
    /// Products table.
    /// </summary>
    private void StartNewProduct(ProductStage? stage = null, int? categoryId = null, bool isComposite = false)
    {
        _newProductInDefaultStage = stage is null;
        NewProductStage = stage ?? _defaultNewProductStage;
        _newProductCategoryId = categoryId;
        SelectedProduct = null;
        FormVersion++;
        _editingId = 0;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
        EditingQuantityRequired = 1;
        EditingIncludesComponentPackaging = false;
        EditingCurrentPrice = null;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run. The kind goes
        // first, so the tab ClearDetails picks is the new kind's.
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        SetComposite(isComposite);
        ClearDetails();
        RefreshComponentOptions();
        Costing.ApplyDefaults();

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// Empties the recipe, packaging, costing and links, and what Save compares them against. With
    /// <paramref name="keepRecipeTabs"/>, the recipe's sections and the Packaging tab stay for another product's to
    /// replace in the same pass: the tab strip then keeps its tabs rather than building a new one for each.
    /// </summary>
    private void ClearDetails(bool keepRecipeTabs = false)
    {
        // The history is this product's form; another product's steps would put back a recipe that isn't its.
        History.Clear();

        var previousTab = SelectedRecipeTab;
        _detailProductId = null;

        _arrangingRecipeTabs = true;
        try
        {
            if (!keepRecipeTabs)
            {
                UsageEdits.Clear();
            }

            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        Packaging.Clear();
        if (!keepRecipeTabs)
        {
            SetHasPackaging(false);
        }

        // A composite's lines, and what its products were read to bring, are that composite's.
        ComponentsTable.Clear();
        Scaling.Forget();
        _savedRecipe = new RecipeDraft([], []);
        Costing.Load(null);
        RefreshLinkableOptions();
        RecomputeCosts();

        // A picked product's sub-assemblies are on their way, and pick the tab when they arrive.
        _isRecipeClearedForAnother = SelectedProduct is not null;
        if (SelectedProduct is null)
        {
            ReselectRecipeTab(previousTab, previousUsage: null, previousIds: []);
        }
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

        // A composite's Products tab (or none) goes in before the tabs are rebuilt, and its lines before ReplaceUsages
        // settles what Save compares the recipe against, which they're part of.
        SetComposite(product.IsComposite);
        ComponentsTable.Load(detail.Components);

        // The packaging first, so the recipe's tabs, rebuilt next, can reselect the Packaging tab if it showed.
        Packaging.LoadLink(detail.PackagingLink);
        Packaging.LoadLines(detail.Lines);
        SetHasPackaging(detail.PackagingLink is not null || detail.Lines.Count > 0);
        ReplaceUsages(detail.Usages, detail.SubAssemblyLines, detail.AddIns);
        Costing.Load(detail.Costing);
        RefreshLinkableOptions();
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

    /// <summary>
    /// Makes the form a composite's, with its Products tab first, before its sections' tabs, or a product of
    /// sub-assemblies', without it. The tab strip's own pick as the tab goes is ignored.
    /// </summary>
    private void SetComposite(bool isComposite)
    {
        IsEditingComposite = isComposite;
        if (isComposite == RecipeTabs.Contains(ComponentsTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            if (isComposite)
            {
                RecipeTabs.Insert(0, ComponentsTab);
            }
            else
            {
                RecipeTabs.Remove(ComponentsTab);
            }
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>A composite's Products tab, or null for a product made of sub-assemblies: the tab to fall back on before Packaging.</summary>
    private RecipeTab? ComponentsTabIfShown => RecipeTabs.Contains(ComponentsTab) ? ComponentsTab : null;

    /// <summary>Where the sections' tabs start in RecipeTabs: after a composite's Products tab, which comes first.</summary>
    private int SectionTabOffset => RecipeTabs.Count > 0 && RecipeTabs[0] == ComponentsTab ? 1 : 0;

    /// <summary>New Sub-Assembly's tab, after a composite's Products and the sections', before Packaging; nothing if it's there.</summary>
    private void InsertAddSubAssemblyTab()
    {
        if (!RecipeTabs.Contains(AddSubAssemblyTab))
        {
            RecipeTabs.Insert(SectionTabOffset + UsageEdits.Count, AddSubAssemblyTab);
        }
    }

    /// <summary>What the Products table's product cell offers, for the product showing (a new one can hold any).</summary>
    private void RefreshComponentOptions() => ComponentsTable.RefreshOptions(_detailProductId ?? 0, Products);

    /// <summary>The Packaging tab comes and goes with the packaging, always last, after the add form's tab.</summary>
    private void SetHasPackaging(bool hasPackaging)
    {
        HasPackaging = hasPackaging;
        if (hasPackaging == RecipeTabs.Contains(PackagingTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            if (hasPackaging)
            {
                RecipeTabs.Add(PackagingTab);
            }
            else
            {
                RecipeTabs.Remove(PackagingTab);
            }
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>The Packaging tab while the product has packaging, else null: the tab to fall back on after the sub-assemblies'.</summary>
    private RecipeTab? PackagingTabIfShown => HasPackaging ? PackagingTab : null;

    /// <summary>
    /// Delete Sub-Assembly on the Packaging tab: the packaging leaves the recipe, lines or link, and its tab goes. The
    /// last sub-assembly's tab shows instead, if there is one. Save writes it; the packs keep their own lines.
    /// </summary>
    private void RemovePackaging()
    {
        SelectRecipeTab(RecipeTabs.OfType<SubAssemblyRecipeTab>().LastOrDefault() ?? ComponentsTabIfShown);
        Packaging.SelectedLineEdit = null;
        Packaging.LineEdits.Clear();
        Packaging.ShowLink(null);
        SetHasPackaging(false);
        RefreshLinkableOptions();
        RecomputeCosts();
    }

    /// <summary>The saved usages as sections, in recipe order; they're also what Save compares the recipe against.</summary>
    private void ReplaceUsages(
        IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlyList<UsageAddInLine> addIns)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);

        // Add-ins key on the usage, not the sub-assembly: they're this product's own, so two products
        // linking the same one have different sets.
        var addInsByUsage = addIns.ToLookup(a => a.UsageId);
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        List<ProductUsageEdit> sections =
        [
            .. usages
                .Select(u => ToUsageEdit(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], ingredientsById))
                .Order(ProductUsageEdit.RecipeOrder),
        ];

        // Another product's recipe opens on its first tab; the same product's, reloaded, keeps the tab it showed. Its
        // section is read now, before the tabs take on the new sections.
        var previousTab = _isRecipeClearedForAnother ? null : SelectedRecipeTab;
        var previousUsage = (previousTab as SubAssemblyRecipeTab)?.Usage;
        HashSet<int> previousIds = [.. UsageEdits.Select(u => u.SubAssemblyId)];
        _isRecipeClearedForAnother = false;

        _arrangingRecipeTabs = true;
        try
        {
            ReplaceUsageItems(sections);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        _savedRecipe = CurrentRecipe();
        ReselectRecipeTab(previousTab, previousUsage, previousIds);
    }

    /// <summary>
    /// Follows <paramref name="rows"/> for Save's sake: a row added, removed or replaced can be something to
    /// save. Watched through the collection's own PropertyChanged rather than its CollectionChanged, because an
    /// ObservableCollection only enforces its re-entrancy guard once more than one handler is listening - and
    /// these collections are changed from inside their own notifications, so a second CollectionChanged listener
    /// would turn a change the guard tolerates today into an exception. What's typed into a row reaches Save
    /// another way: the grid closes the cell's undo step, which re-reads whether there's anything to save.
    /// </summary>
    private void WatchForSave(INotifyPropertyChanged rows) => rows.PropertyChanged += (_, _) => History.RefreshDirty();

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    /// <summary>IUndoRecorder: goes up as another product, or a new one, takes the form's place.</summary>
    public int FormVersion { get; private set; }

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

    /// <summary>Settles whatever step is open, then puts the form back one step. False when there was nothing to undo.</summary>
    public bool TryUndo()
    {
        History.End();
        if (!History.TryUndo(out var restore, out var settled))
        {
            return false;
        }

        RestoreSnapshot(restore, settled);
        return true;
    }

    /// <summary>Steps forward again into what the last undo stepped off. False when there was nothing to redo.</summary>
    public bool TryRedo()
    {
        History.End();
        if (!History.TryRedo(out var restore, out var settled))
        {
            return false;
        }

        RestoreSnapshot(restore, settled);
        return true;
    }

    /// <summary>The form as it stands: everything an undo step puts back. See ProductFormSnapshot for what's left out.</summary>
    private ProductFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Description = EditingDescription,
        CurrentPrice = EditingCurrentPrice,
        QuantityRequired = EditingQuantityRequired,
        IncludesComponentPackaging = EditingIncludesComponentPackaging,
        CategoryId = EditingCategory?.Id,
        CloneName = EditingCloneName,
        Costing = Costing.Percentages,
        Usages = [.. UsageEdits],
        SelectedTab = CaptureTabChoice(),
        HasAddSubAssemblyTab = RecipeTabs.Contains(AddSubAssemblyTab),
        HasPackaging = HasPackaging,
        PackagingLink = Packaging.LinkTarget,
        IsPackagingMoving = Packaging.IsPackagingMoving,
        Lines = [.. Packaging.LineEdits.Select(CaptureLine)],
        Components = ComponentsTable.Capture(),
        IsUsageFormOpen = UsageForm.IsUsageFormOpen,
        IsAddingUsage = UsageForm.IsAddingUsage,
        UsageFormTitle = UsageForm.UsageFormTitle,
        UsageMode = UsageForm.EditingUsageMode,
        IsFormEditingLines = UsageForm.IsFormEditingLines,
        IsFormPickingSource = UsageForm.IsFormPickingSource,
        EditingUsageIndex = UsageForm.EditingUsage is { } editing && UsageEdits.IndexOf(editing) is var i and >= 0 ? i : null,
        UsageRoleId = UsageForm.EditingUsageRole?.Id,
        UsageName = UsageForm.EditingUsageName,
        UsageSourceKey = UsageForm.EditingUsageSource?.Key,
        FormLines = [.. UsageForm.FormLines.Select(CaptureIngredientLine)],
        FormAddIns = [.. UsageForm.FormAddIns.Select(CaptureIngredientLine)],
        FormPackagingLines = [.. UsageForm.FormPackagingLines.Select(CaptureLine)],
        FormFormulaBase = UsageForm.FormFormulaBase,
        UsageAuthoredField = UsageForm.AuthoredField,
        UsageWeightPerPiece = UsageForm.EditingUsageWeightPerPiece,
        UsageYield = UsageForm.EditingUsageYield,
    };

    private static IngredientLineState CaptureIngredientLine(IngredientLineRow row) =>
        new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent);

    private static PackagingLineState CaptureLine(PackagingLineRow row) => new(row.PackagingMaterialId, row.UnitId, row.QtyPerUnit);

    private RecipeTabChoice? CaptureTabChoice() => SelectedRecipeTab switch
    {
        SubAssemblyRecipeTab tab => new RecipeTabChoice.Usage(RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList().IndexOf(tab)),
        AddSubAssemblyRecipeTab => new RecipeTabChoice.AddSubAssembly(),
        PackagingRecipeTab => new RecipeTabChoice.Packaging(),
        ComponentsRecipeTab => new RecipeTabChoice.Components(),
        null => null,
    };

    /// <summary>
    /// Puts the whole form back as <paramref name="snapshot"/> had it. Order matters throughout, and is explained
    /// step by step below. Every figure the writes would work out again waits for the end (_updates' batch), so the
    /// figures are worked out once from the restored form rather than over and over from a half-restored one.
    /// </summary>
    /// <param name="settledThen">What Save measured against when <paramref name="snapshot"/> was taken, or null.</param>
    private void RestoreSnapshot(ProductFormSnapshot snapshot, ProductFormSnapshot? settledThen)
    {
        // A message about a form that no longer exists would outlive what it was about.
        ErrorMessage = null;

        ApplyRestore(() => _updates.Batch(() =>
        {
            // 1. The form goes first. EditingUsage has to be null before the sections are replaced: the tab mirror
            //    refills FormLines from a section that replaces the one being edited, which would undo this restore.
            CloseUsageForm();
            UsageForm.SelectedFormLine = null;
            UsageForm.SelectedFormAddIn = null;
            UsageForm.SelectedFormPackagingLine = null;
            Packaging.SelectedLineEdit = null;
            ComponentsTable.SelectedComponentEdit = null;

            // 2. The product's own fields. The category goes back by id, as the reloaded list's own copy.
            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingDescription = snapshot.Description;
            EditingCurrentPrice = snapshot.CurrentPrice;
            EditingQuantityRequired = snapshot.QuantityRequired;
            EditingIncludesComponentPackaging = snapshot.IncludesComponentPackaging;
            EditingCloneName = snapshot.CloneName;
            _editingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            this.RaisePropertyChanged(nameof(EditingCategory));

            // 3. Costing.
            Costing.Percentages = snapshot.Costing;

            // 4. Packaging. The link as saved is deliberately left alone: it's what Save compares against. Showing the
            //    link ends a Move to Sub-Assemblies, so the move goes back after it.
            Packaging.ShowLink(snapshot.PackagingLink);
            Packaging.IsPackagingMoving = snapshot.IsPackagingMoving;
            Packaging.LineEdits.Clear();
            foreach (var line in snapshot.Lines)
            {
                Packaging.LineEdits.Add(RestoreLine(line));
            }

            SetHasPackaging(snapshot.HasPackaging);

            // A composite's lines, each finding its product and the recipe cost read for it again (a cost not read yet is
            // read now, and prices the line when it arrives).
            ComponentsTable.Restore(snapshot.Components);

            // 5. The sections and their tabs, under _arrangingRecipeTabs so RecipeTabShown short-circuits: without
            //    it, moving the tab back would apply the form the tab is being moved off, which is the very change
            //    being undone.
            _arrangingRecipeTabs = true;
            try
            {
                ReplaceUsageItems(snapshot.Usages);

                if (snapshot.HasAddSubAssemblyTab)
                {
                    InsertAddSubAssemblyTab();
                }
                else
                {
                    RecipeTabs.Remove(AddSubAssemblyTab);
                }

                SelectedRecipeTab = snapshot.SelectedTab switch
                {
                    RecipeTabChoice.Usage(var index) => RecipeTabs.OfType<SubAssemblyRecipeTab>().ElementAtOrDefault(index),
                    RecipeTabChoice.AddSubAssembly => RecipeTabs.Contains(AddSubAssemblyTab) ? AddSubAssemblyTab : null,
                    RecipeTabChoice.Packaging => PackagingTabIfShown,
                    RecipeTabChoice.Components => ComponentsTabIfShown,
                    null => null,
                };
            }
            finally
            {
                _arrangingRecipeTabs = false;
            }

            // RecipeTabShown never ran, and it's the only other thing that writes these. Left stale, the next real
            // change of tab would think it was leaving a tab that's gone, and apply the wrong form on the way out.
            _shownRecipeTab = SelectedRecipeTab;
            if (SelectedRecipeTab is not null and not AddSubAssemblyRecipeTab)
            {
                _lastRecipeContentTab = SelectedRecipeTab;
            }

            // 6. The form itself, last. The option lists are rebuilt before the pickers are put back, as a load does,
            //    and RefreshLinkableOptions runs inside the guard: outside it, the source it can null would start a
            //    load that clears the very lines being restored.
            UsageForm.IsAddingUsage = snapshot.IsAddingUsage;
            UsageForm.UsageFormTitle = snapshot.UsageFormTitle;
            UsageForm.RestoreMode(snapshot.UsageMode);
            UsageForm.IsFormEditingLines = snapshot.IsFormEditingLines;
            UsageForm.IsFormPickingSource = snapshot.IsFormPickingSource;

            RefreshLinkableOptions();
            RefreshFormRoleOptions();

            UsageForm.RestorePicks(
                snapshot.UsageRoleId is { } roleId ? UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == roleId) : null,
                snapshot.UsageSourceKey is { } sourceKey ? UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == sourceKey) : null,
                snapshot.UsageName);

            UsageForm.FormFormulaBase = snapshot.FormFormulaBase;
            RestoreIngredientLines(UsageForm.FormLines, snapshot.FormLines, snapshot.FormFormulaBase);
            RestoreIngredientLines(UsageForm.FormAddIns, snapshot.FormAddIns, snapshot.FormFormulaBase);
            UsageForm.FormPackagingLines.Clear();
            foreach (var line in snapshot.FormPackagingLines)
            {
                UsageForm.FormPackagingLines.Add(RestoreLine(line));
            }

            UsageForm.RestorePieceSize(snapshot.UsageAuthoredField, snapshot.UsageWeightPerPiece, snapshot.UsageYield);

            UsageForm.EditingUsage = snapshot.EditingUsageIndex is { } usageIndex && usageIndex < UsageEdits.Count
                ? UsageEdits[usageIndex]
                : null;
            UsageForm.IsUsageFormOpen = snapshot.IsUsageFormOpen;
        }));

        // 8. One pass over the restored form, after the batch. FormLinesChanged is the screen's own "the lines
        //    changed, bring everything into line" entry point: it raises what the form's tables show, works the
        //    derived piece-size figure out again, and runs the single ApplyFormulaBase pass that gives every row
        //    back its BaseWeight and BatchWeight.
        UsageForm.RaisePackagingChanged();
        FormLinesChanged();
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

    private void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows, IReadOnlyList<IngredientLineState> states, decimal baseWeight)
    {
        rows.Clear();
        foreach (var state in states)
        {
            rows.Add(IngredientLineRow.Restore(
                id => Ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent));
        }
    }

    // The material goes on first: it brings the units the row can be in, and a key it doesn't offer falls back
    // to the base unit, which would drop a restored unit set the other way round.
    private PackagingLineRow RestoreLine(PackagingLineState state) =>
        new(FindPackagingMaterial)
        {
            PackagingMaterialId = state.PackagingMaterialId,
            UnitKey = state.UnitId ?? PackagingUnitChoice.BaseUnitKey,
            QtyPerUnit = state.QtyPerUnit,
        };

    /// <summary>
    /// Makes UsageEdits hold <paramref name="sections"/>, section for section and in place: each tab stays and shows its
    /// new section (MirrorUsageTabs), so the tab strip keeps the tabs it has and only adds or takes away the difference,
    /// rather than building a new TabItemExt, template and all, for every section of every product picked. Never Clear(),
    /// which the mirror reads as every tab going. Call it with _arrangingRecipeTabs set, so the strip's own pick as a tab
    /// goes is ignored. It leaves _savedRecipe alone, which is why an undo can use it and ReplaceUsages can't be reused.
    /// </summary>
    private void ReplaceUsageItems(IReadOnlyList<ProductUsageEdit> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (i < UsageEdits.Count)
            {
                UsageEdits[i] = sections[i];
            }
            else
            {
                UsageEdits.Add(sections[i]);
            }
        }

        while (UsageEdits.Count > sections.Count)
        {
            UsageEdits.RemoveAt(UsageEdits.Count - 1);
        }
    }

    /// <summary>A saved usage as its section, with the screen's own copies of its role and ingredients (ProductUsageEdit.FromSaved).</summary>
    private ProductUsageEdit ToUsageEdit(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        var subAssembly = usage.SubAssembly!;
        var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        return ProductUsageEdit.FromSaved(usage, lines, addIns, role, EditingName, ingredientsById);
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in this product's recipe: its name, less the product's own and
    /// the role it was saved under from here ("Ube" for "Cheese Roll Filling: Ube"), as SetRecipeAsync reads it.
    /// </summary>
    private string? SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) =>
        SubAssemblyRules.SectionLabel(subAssemblyName, EditingName, role?.Name ?? string.Empty);

    /// <summary>
    /// Takes up the defaults as LoadCommand read them. A new product still showing the ones it started with (the screen's
    /// first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed into keeps it.
    /// A new product started by New goes in the new stage whatever was typed, since its stage was never picked.
    /// </summary>
    private void RefreshCostingDefaults(CostingDefaults defaults)
    {
        var untouchedNewProduct = _editingId == 0 && Costing.ShowsDefaults;
        Costing.SetDefaults(defaults);
        _defaultNewProductStage = defaults.NewProductStage;
        if (_wastagePercent != defaults.WastagePercent || _vatPercent != defaults.VatPercent || _priceRoundingStep != defaults.PriceRoundingStep)
        {
            // Every product is priced with it, the one open included: its Costing tab follows at once.
            _wastagePercent = defaults.WastagePercent;
            _vatPercent = defaults.VatPercent;
            _priceRoundingStep = defaults.PriceRoundingStep;
            RecomputeCosts();
        }
        if (_editingId == 0 && _newProductInDefaultStage)
        {
            NewProductStage = _defaultNewProductStage;
        }
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
    /// Asks before Save changes the lines of a linked sub-assembly that other products use too, or a made ingredient is made
    /// from, naming them as they are now rather than as last loaded. True, without asking, when nothing else would change.
    /// </summary>
    private async Task<bool> ConfirmSharedLinesAsync()
    {
        var edited = UsageEdits.Where(u => u.AreLinkedLinesEdited).ToList();
        if (edited.Count == 0)
        {
            return true;
        }

        var usedBy = await _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None);
        var madeFrom = await _subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(CancellationToken.None);
        var shared = edited
            .Select(u => (u.LinkedName, Others: (IReadOnlyList<string>)
            [
                .. OtherProductsUsing(u.SubAssemblyId, usedBy),
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
    /// The recipe as Save writes it: the new sections with their lines, and the sub-assemblies it links; and a composite's
    /// products, each with how many go in (a line half filled in is refused: ProductComponentRow.CompleteLines).
    /// </summary>
    private RecipeDraft CurrentRecipe() =>
        new(
            [.. UsageEdits.Where(u => u.IsEditable).Select(u => new NewSubAssemblyDraft(
                u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue, u.Name)
            {
                // The product's own keeps its id, so Save rewrites it in place; a new section's is 0.
                Id = u.SubAssemblyId,
                Placement = u.ToMadeIngredient is { } made ? new SectionPlacement.MoveToInHouse(made)
                    : u.IsReleasing ? new SectionPlacement.MoveToSubAssemblies()
                    : u.IsOwned ? new SectionPlacement.ProductsOwn()
                    : new SectionPlacement.ByRole(),
            })],
            [.. UsageEdits.Where(u => u.IsLinked).Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
            {
                AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],

                // Only lines changed here are written, so a product's Save doesn't put back lines the Sub-Assemblies
                // screen has changed since.
                Lines = u.AreLinkedLinesEdited
                    ? [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))]
                    : null,
            })])
        {
            Components = IsEditingComposite ? [.. ProductComponentRow.CompleteLines(ComponentsTable.ComponentEdits).Select(c => (c.Product.Id, c.Quantity))] : [],
        };

    /// <summary>
    /// Whether two recipes would save the same: the same sub-assemblies, roles, lines, add-ins and piece
    /// sizes, and the same products at the same quantities, in the same order. A linked draft is compared field by
    /// field rather than as a record, as a new one already is: a record's own Equals compares its AddIns list by
    /// reference, which no two recipes built separately ever share, so Save would write the recipe every time.
    /// </summary>
    private static bool IsSameRecipe(RecipeDraft a, RecipeDraft b) =>
        a.Components.SequenceEqual(b.Components)
        && a.Linked.Count == b.Linked.Count
        && a.Linked.Zip(b.Linked).All(pair =>
            pair.First.SubAssemblyId == pair.Second.SubAssemblyId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.AddIns.SequenceEqual(pair.Second.AddIns)
            && (pair.First.Lines is null) == (pair.Second.Lines is null)
            && (pair.First.Lines ?? []).SequenceEqual(pair.Second.Lines ?? []))
        && a.New.Count == b.New.Count
        && a.New.Zip(b.New).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Placement == pair.Second.Placement

            && pair.First.RoleId == pair.Second.RoleId
            && pair.First.SectionName == pair.Second.SectionName
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));

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
    /// Puts <paramref name="row"/>'s version into the form as unsaved edits, in one undo step: its sections, packaging,
    /// products, costing percentages, quantity required, current price and whether a composite includes its products'
    /// packaging. The name, description, category and stage stay. A section of the product's own keeps its sub-assembly
    /// when the product still owns it; a linked one is linked again as it is now, since Save never writes a version's lines
    /// into a sub-assembly others may share; one deleted since comes back as a new section with the version's lines (its
    /// add-ins among them). Packaging links what it linked while that's still there, else takes the lines it showed as the
    /// product's own. Refused, with nothing changed, while a material, role or product it needs is gone.
    /// </summary>
    private async Task RestoreVersionAsync(ProductVersionRow row)
    {
        if (!AreDetailsReady || SelectedProduct is not { } product || VersionHistory.ProductId != product.Id)
        {
            return;
        }

        var content = row.Content;
        if (content.IsComposite != product.IsComposite)
        {
            throw new BusinessRuleException($"{row.Title} is of another kind of product, so it can't be restored here.");
        }

        // What it linked, as it is now: its linked sections' sub-assemblies and its packaging set, and their lines.
        IReadOnlyCollection<int> linkedIds =
        [
            .. content.Sections.Where(s => !s.IsOwn).Select(s => s.SubAssemblyId)
                .Concat(content.PackagingLink?.SubAssemblyId is { } linkedSetId ? [linkedSetId] : Enumerable.Empty<int>())
                .Distinct(),
        ];
        var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None));
        var linesRead = Task.Run(() => _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None));
        await Task.WhenAll(subAssembliesRead, linesRead);
        var subAssembliesNow = (await subAssembliesRead).ToDictionary(s => s.Id);
        var linesNow = (await linesRead).ToLookup(l => l.SubAssemblyId);

        PackagingLinkTarget? link = null;
        if (content.PackagingLink is { } versionLink)
        {
            if (versionLink.SubAssemblyId is { } setId && subAssembliesNow.TryGetValue(setId, out var set))
            {
                link = new PackagingLinkTarget.Set(setId, set.Name);
            }
            else if (versionLink.ProductId is { } linkedProductId && Products.FirstOrDefault(p => p.Id == linkedProductId) is { } linkedProduct)
            {
                link = new PackagingLinkTarget.OtherProduct(linkedProductId, linkedProduct.Name);
            }
        }

        // Everything it needs that's gone, named, before anything changes.
        var materialNames = content.Materials.ToDictionary(m => m.Id, m => m.Name);
        string MaterialName(int id) => materialNames.GetValueOrDefault(id) ?? $"Material {id}";
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var packagingById = PackagingMaterials.ToDictionary(m => m.Id);
        bool IsRelinked(VersionSection section) => !section.IsOwn && subAssembliesNow.ContainsKey(section.SubAssemblyId);
        List<string> missing = [];
        foreach (var section in content.Sections)
        {
            var relinked = IsRelinked(section);
            if (!relinked && Roles.All(r => r.Id != section.Role.Id))
            {
                missing.Add($"the {section.Role.Name} role");
            }

            missing.AddRange((relinked ? Enumerable.Empty<int>() : section.Lines.Select(l => l.IngredientId))
                .Concat(section.AddIns.Select(a => a.IngredientId))
                .Where(id => !ingredientsById.ContainsKey(id))
                .Select(MaterialName));
        }

        if (link is null)
        {
            missing.AddRange(content.PackagingLines.Where(l => !packagingById.ContainsKey(l.MaterialId)).Select(l => MaterialName(l.MaterialId)));
        }

        missing.AddRange(content.Components.Where(c => Products.All(p => p.Id != c.ProductId)).Select(c => c.ProductName));
        if (missing.Distinct().ToList() is { Count: > 0 } gone)
        {
            throw new BusinessRuleException(
                $"{row.Title} can't be restored: it needs {string.Join(", ", gone)}, which {(gone.Count == 1 ? "is" : "are")} no longer there.");
        }

        // The lines a link shows: those showing already when it's the link the form has, else as it has them now.
        IReadOnlyList<PackagingLineState> packagingLines;
        if (link is null)
        {
            packagingLines = [.. content.PackagingLines.Select(l => OwnPackagingLineOf(l, content, packagingById))];
        }
        else if (link.Key == Packaging.LinkTarget?.Key)
        {
            packagingLines = [.. Packaging.LineEdits.Select(CaptureLine)];
        }
        else
        {
            packagingLines = link switch
            {
                PackagingLinkTarget.Set(var setId, _) => [.. (await Task.Run(() => _subAssemblyRepository.GetPackagingLinesAsync(setId, CancellationToken.None)))
                    .Select(l => new PackagingLineState(l.PackagingMaterialId, l.UnitId, l.QtyPerSet))],
                PackagingLinkTarget.OtherProduct(var linkedProductId, _) => [.. (await Task.Run(() => _packagingLineRepository.GetEffectiveForProductAsync(linkedProductId, CancellationToken.None)))
                    .Select(l => PackagingLineRow.FromSaved(l))
                    .Select(r => new PackagingLineState(r.PackagingMaterialId, r.UnitId, r.QtyPerUnit))],
            };
        }

        // Another product picked while that was read: the version isn't this one's.
        if (SelectedProduct?.Id != product.Id || !AreDetailsReady)
        {
            return;
        }

        HashSet<int> ownedNow = [.. _savedRecipe.New.Select(n => n.Id).Where(id => id != 0)];
        List<string> changedSinceLinks = [];
        List<ProductUsageEdit> sections = [];
        foreach (var section in content.Sections)
        {
            if (IsRelinked(section))
            {
                var linked = subAssembliesNow[section.SubAssemblyId];
                var current = linesNow[linked.Id].ToList();
                if (!HasSameLines(current, section.Lines))
                {
                    changedSinceLinks.Add(linked.Name);
                }

                var linkedRole = Roles.FirstOrDefault(r => r.Id == linked.RoleId) ?? linked.Role;
                sections.Add(new ProductUsageEdit(
                    linked.Id,
                    linkedRole,
                    [.. current.Select(l => new SectionLine(
                        ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    section.AuthoredField,
                    section.AuthoredValue,
                    linked.Name,
                    [.. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams))],
                    SectionLabelOf(linked.Name, linkedRole),
                    linkedOwner: PricedSection.LinkedOwnerOf(linked)));
                continue;
            }

            // The product's own, or a linked one deleted since: the version's lines, and a linked one's add-ins with them,
            // since a section of its own has none.
            var keepsId = section.IsOwn && ownedNow.Contains(section.SubAssemblyId);
            sections.Add(new ProductUsageEdit(
                keepsId ? section.SubAssemblyId : 0,
                Roles.First(r => r.Id == section.Role.Id),
                [
                    .. section.Lines.Select(l => new SectionLine(ingredientsById[l.IngredientId], l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                    .. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams)),
                ],
                section.AuthoredField,
                section.AuthoredValue,
                linkedName: null,
                name: section.LabelIn(content.ProductName, content.Schema),
                isOwn: section.IsOwn && !keepsId));
        }

        sections.Sort(ProductUsageEdit.RecipeOrder);
        var costing = content.Costing ?? new VersionCosting(0m, 0m, 0m, 0m);
        AsUndoStep(() =>
        {
            // The form goes, as any restore closes it: what it held comes back with Undo.
            CloseUsageForm();
            RestoreSnapshot(
                CaptureSnapshot() with
                {
                    CurrentPrice = content.CurrentPrice,
                    QuantityRequired = content.QuantityRequired,
                    IncludesComponentPackaging = content.IncludesComponentPackaging,
                    Costing = (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent),
                    Usages = sections,
                    SelectedTab = sections.Count > 0 ? new RecipeTabChoice.Usage(0)
                        : product.IsComposite ? new RecipeTabChoice.Components()
                        : new RecipeTabChoice.Packaging(),
                    HasAddSubAssemblyTab = false,
                    HasPackaging = link is not null || packagingLines.Count > 0,
                    PackagingLink = link,
                    IsPackagingMoving = false,
                    Lines = packagingLines,
                    Components = [.. content.Components.Select(c => new ComponentLineState(c.ProductId, c.Quantity))],
                    IsUsageFormOpen = false,
                    IsAddingUsage = false,
                    EditingUsageIndex = null,
                    FormLines = [],
                    FormAddIns = [],
                    FormPackagingLines = [],
                },
                null);

            // The section tab showing opens its form, as showing it would: the restore puts tabs back without opening any,
            // and a section's tab with its form closed showed an empty form (no lines, a 0 g batch).
            if (SelectedRecipeTab is SubAssemblyRecipeTab { Usage: var shown })
            {
                OpenUsageForm(shown);
            }

            // The form closing and opening isn't the user's change, as it isn't when a tab change does it: only the recipe,
            // packaging, costing and price the version put back count for Save. Without this, a version the same as the
            // saved product (one whose only difference is a linked sub-assembly's lines, kept as they are now) left Save lit.
            RebaseSettledForm();
        });

        List<string> notes = [$"{row.Title} is in the form. Save to keep it, or Undo to put the form back."];
        if (changedSinceLinks.Count > 0)
        {
            notes.Add($"{string.Join(", ", changedSinceLinks)} {(changedSinceLinks.Count == 1 ? "has" : "have")} changed on Sub-Assemblies since, and {(changedSinceLinks.Count == 1 ? "is" : "are")} linked as {(changedSinceLinks.Count == 1 ? "it is" : "they are")} now.");
        }

        if (content.PackagingLink is { } wasLinked && link is null)
        {
            notes.Add($"{wasLinked.Name} is gone, so the packaging it showed comes back as the product's own.");
        }

        SuccessMessage = string.Join(" ", notes);
    }

    /// <summary>
    /// A version's packaging line as the product's own: in the unit it was in, or in base units when the material has lost
    /// that unit since.
    /// </summary>
    private static PackagingLineState OwnPackagingLineOf(
        VersionPackagingLine line, ProductVersionContent content, Dictionary<int, RawMaterial> packagingById)
    {
        if (line.UnitId is { } unitId && packagingById[line.MaterialId].Units.All(u => u.Id != unitId))
        {
            var baseUnitsPerUnit = content.Materials.FirstOrDefault(m => m.Id == line.MaterialId)?.Units.FirstOrDefault(u => u.Id == unitId)?.BaseUnitsPerUnit ?? 1m;
            return new PackagingLineState(line.MaterialId, null, UnitConversionRules.ToBaseQuantity(line.QtyPerUnit, baseUnitsPerUnit));
        }

        return new PackagingLineState(line.MaterialId, line.UnitId, line.QtyPerUnit);
    }

    /// <summary>Whether a sub-assembly's lines now are the ones a version saw: the same ingredients, grams and base ticks, in order.</summary>
    private static bool HasSameLines(List<SubAssemblyLine> now, IReadOnlyList<VersionLine> then) =>
        now.Count == then.Count
        && now.Zip(then).All(pair =>
            pair.First.IngredientId == pair.Second.IngredientId
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase);

    /// <summary>The add form, fresh for its mode: a new sub-assembly's name, role and lines, or the pick of another product's.</summary>
    private void StartUsageFormMode()
    {
        var mode = UsageForm.EditingUsageMode;
        UsageForm.IsFormEditingLines = mode != UsageFormMode.Link;
        UsageForm.IsFormPickingSource = mode != UsageFormMode.New;

        // The title is the one thing on the form that says which way in was picked, now that the picker that used to
        // say it is on Add Sub-Assembly's drop-down instead. Set here, not in StartAddingUsage, so it follows another
        // item picked while the form is open. A new one has none: its "Moves to Sub-Assemblies on Save" badge
        // (IsFormNewMovingToSubAssemblies) takes the title's place, and in a product-owned role nothing does.
        UsageForm.UsageFormTitle = mode switch
        {
            UsageFormMode.Link => "Link an existing sub-assembly",
            UsageFormMode.Copy => "Copy an existing sub-assembly",
            _ => string.Empty,
        };
        UsageForm.FormLines.Clear();
        UsageForm.FormAddIns.Clear();
        UsageForm.FormPackagingLines.Clear();
        UsageForm.SelectedFormLine = null;
        UsageForm.SelectedFormAddIn = null;
        UsageForm.SelectedFormPackagingLine = null;
        RefreshLinkableOptions();

        // The first role the recipe doesn't have yet, so sections can be added in role order without picking.
        var takenRoleIds = UsageEdits.Select(u => u.RoleId).ToHashSet();
        UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => !takenRoleIds.Contains(r.Id)) ?? UsageForm.FormRoleOptions.FirstOrDefault();
        UsageForm.EditingUsageName = null;
        UsageForm.EditingUsageSource = null;
        StartPieceSize();
    }

    /// <summary>
    /// The piece size the add form starts at in <paramref name="role"/>. A section made in a product-owned role
    /// (SubAssemblyRole.IsProductOwned), new or copied, is written for the one piece it goes into, so it starts at a
    /// yield of 1: its weight per piece is the whole batch, and follows the lines as they're typed. Any other starts
    /// with no size. So does a link, whose batch is the Sub-Assemblies screen's and shared.
    /// </summary>
    private (UsageAuthoredField Field, decimal Value) StartingPieceSize(SubAssemblyRole? role) =>
        UsageForm.EditingUsageMode != UsageFormMode.Link && role is { IsProductOwned: true }
            ? (UsageAuthoredField.Yield, 1m)
            : (UsageAuthoredField.WeightPerPiece, 0m);

    /// <summary>Shows the piece size the add form's role starts at.</summary>
    private void StartPieceSize()
    {
        var (field, value) = StartingPieceSize(UsageForm.EditingUsageRole);
        UsageForm.ShowPieceSize(field, value);
    }

    /// <summary>
    /// Whether the add form's piece size is still the one <paramref name="role"/> starts it at, with neither figure
    /// typed. Worked out from the form, not remembered, so it holds for a form an undo put back. No size is no size,
    /// whichever box it was left in.
    /// </summary>
    private bool IsPieceSizeAsStarted(SubAssemblyRole? role)
    {
        var (field, value) = StartingPieceSize(role);
        return UsageForm.AuthoredValue == value && (value == 0 || UsageForm.AuthoredField == field);
    }

    /// <summary>The badge's Edit: the picker, holding what the section could link instead, in the badge's place.</summary>
    private void StartChangingLink()
    {
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();
        UsageForm.ChangeLinkOptions.MergeFrom(
            [.. _linkable
                .Where(s => s.OwnerProductId is null && s.Role?.IsPackaging != true && !usedIds.Contains(s.Id))
                .Select(s => new LinkableSubAssemblyOption(s))],
            o => o.Key);

        if (UsageForm.ChangeLinkOptions.Count == 0)
        {
            ErrorMessage = "There's no other sub-assembly to link: every one on the Sub-Assemblies screen is in this recipe already.";
            return;
        }

        ErrorMessage = null;
        UsageForm.ChangeLinkTarget = null;
        UsageForm.IsChangingLink = true;
    }

    /// <summary>
    /// Links <paramref name="target"/> in place of the linked section the form shows, as one undo step, keeping what's
    /// this product's: the piece size as the form has it, and the add-ins, less any the new recipe
    /// already has (a toast names them). Lines changed here but not saved go with the old link. The section keeps its
    /// tab, which takes the new one's role and name; Save writes the new link.
    /// </summary>
    private async Task ChangeLinkAsync(LinkableSubAssemblyOption target, CancellationToken cancellationToken)
    {
        if (UsageForm.EditingUsage is not { IsLinked: true } linked || target.SubAssembly is not { } subAssembly)
        {
            return;
        }

        var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded: another section, or the change was cancelled.
        if (!UsageForm.IsChangingLink || !ReferenceEquals(UsageForm.EditingUsage, linked) || !ReferenceEquals(UsageForm.ChangeLinkTarget, target))
        {
            return;
        }

        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        IReadOnlyList<SectionLine> lines =
            [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
        var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        try
        {
            ErrorMessage = null;
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"{subAssembly.Name} has no ingredients yet. Add them on the Sub-Assemblies screen first.");
            }

            var lineIngredientIds = lines.Select(l => l.Ingredient.Id).ToHashSet();
            var addIns = IngredientLineRow.CompleteLines(UsageForm.FormAddIns, subAssembly.Name);
            var dropped = addIns.Where(a => lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => a.Ingredient.Name).ToList();
            var replacement = new ProductUsageEdit(
                subAssembly.Id,
                role,
                lines,
                UsageForm.AuthoredField,
                UsageForm.AuthoredValue,
                subAssembly.Name,
                [.. addIns.Where(a => !lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => new SectionLine(a.Ingredient, a.Grams))],
                SectionLabelOf(subAssembly.Name, role));
            EnsureSectionsDistinct(replacement, linked);

            AsUndoStep(() => ReplaceUsage(linked, replacement));
            if (dropped.Count > 0)
            {
                SuccessMessage = $"Now linked to {subAssembly.Name}. {string.Join(", ", dropped)} left the add-ins: {subAssembly.Name} already has {(dropped.Count == 1 ? "it" : "them")}.";
            }
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// After a sub-assembly was picked to link or copy: its lines, for the batch weight, and for a copy
    /// also its role, all editable from then on. Packaging picked instead fills the packaging table: a set's lines, or
    /// the product's packaging as it shows (through any link of its own), and a copy takes the Packaging role.
    /// </summary>
    private async Task LoadUsageSourceAsync(LinkableSubAssemblyOption? source, CancellationToken cancellationToken)
    {
        IReadOnlyList<SectionLine> lines = [];
        IReadOnlyList<PackagingLineRow> packagingRows = [];
        if (source?.PackagingTarget is { } packaging)
        {
            packagingRows = packaging switch
            {
                PackagingLinkTarget.Set(var setId, _) => [.. (await _subAssemblyRepository.GetPackagingLinesAsync(setId, cancellationToken))
                    .Select(l => new PackagingLineRow(FindPackagingMaterial(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId))],
                PackagingLinkTarget.OtherProduct(var productId, _) =>
                    [.. (await _packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken)).Select(PackagingLineRow.FromSaved)],
            };
        }
        else if (source?.SubAssembly is { } subAssembly)
        {
            var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
            var ingredientsById = Ingredients.ToDictionary(m => m.Id);
            lines = [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams))];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded (applied, cancelled, or another pick): they're stale.
        if (!UsageForm.IsUsageFormOpen || !UsageForm.IsAddingUsage || !ReferenceEquals(UsageForm.EditingUsageSource, source))
        {
            return;
        }

        ShowFormLines(lines);
        UsageForm.SelectedFormPackagingLine = null;
        UsageForm.FormPackagingLines.Clear();
        UsageForm.FormPackagingLines.AddRange(packagingRows);

        // A copy takes its source's role; packaging, the Packaging role. Not its name, which is another product's and
        // role's ("Cheese Roll Filling: Cheese"): Save names the copy after this product.
        if (UsageForm.EditingUsageMode == UsageFormMode.Copy && source is not null)
        {
            UsageForm.EditingUsageRole = source.IsPackaging
                ? UsageForm.FormRoleOptions.FirstOrDefault(r => r.IsPackaging) ?? UsageForm.EditingUsageRole
                : UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == source.SubAssembly!.RoleId) ?? UsageForm.EditingUsageRole;
            UsageForm.EditingUsageName = null;
        }

        FormLinesChanged();
    }

    /// <summary>
    /// Applies the form and returns the tab that shows what it applied: the sub-assembly's own, or the Packaging tab
    /// for packaging (IsFormPackaging).
    /// </summary>
    private RecipeTab ApplyForm() => UsageForm.IsFormPackaging ? ApplyPackagingForm() : TabFor(ApplyUsageForm());

    /// <summary>
    /// The add form's packaging, put in as the product's: a new or copied one's lines as its own (to type into on the
    /// Packaging tab), or a link, whose lines only show. Save writes it. A product has one packaging, so the form only
    /// offers it while there's none (RefreshFormRoleOptions, RefreshLinkableOptions).
    /// </summary>
    private PackagingRecipeTab ApplyPackagingForm()
    {
        var linking = UsageForm.EditingUsageMode == UsageFormMode.Link;
        var target = UsageForm.EditingUsageSource?.PackagingTarget;
        if (linking && target is null)
        {
            throw new BusinessRuleException("Pick the packaging to link, or cancel the form.");
        }

        var lines = PackagingLineRow.CompleteLines(UsageForm.FormPackagingLines);
        if (lines.Count == 0)
        {
            throw new BusinessRuleException(linking
                ? $"{target!.Name} is still loading its packaging, or has none. Try again in a moment, or pick another."
                : "Add the packaging materials one piece takes.");
        }

        Packaging.ShowLines(lines.Select(l => new PackagingLineRow(l.PackagingMaterial, l.QtyPerUnit)));
        Packaging.ShowLink(linking ? target : null);
        SetHasPackaging(true);
        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeCosts();
        return PackagingTab;
    }

    /// <summary>
    /// Move to In-House on one of the product's own: asks for the made ingredient's name, starting with the name the
    /// section is saved under, and its Made category, starting on In-House when there's one by that name. Then, in one undo
    /// step, it applies whatever is typed into the section's form, so what moves is what shows, and marks the section,
    /// which Save turns into the ingredient. With no Made category, it says where to add one. A name another ingredient
    /// has, or another section here is moving under, is refused at once; Save checks it against every raw material.
    /// </summary>
    private async Task MoveToInHouseAsync(ProductUsageEdit usage)
    {
        ErrorMessage = null;
        var categories = await MadeIngredientPrompt.MadeCategoriesAsync(_rawMaterialCategoryRepository);
        if (categories.Count == 0)
        {
            ErrorMessage = MadeIngredientPrompt.NoMadeCategoryMessage;
            return;
        }

        var wasEditing = ReferenceEquals(usage, UsageForm.EditingUsage);
        var initialName = SubAssemblyRules.StandaloneNameFor(EditingName.Trim(), usage.RoleName, wasEditing ? UsageForm.EditingUsageName : usage.Name);
        var answer = await PromptForMadeIngredient.Handle(MadeIngredientPrompt.RequestFor(
            "Move to In-House",
            initialName,
            categories,
            "Save puts this section on the Sub-Assemblies screen, keeping its name and lines, links this product to it there, and adds a made ingredient made from it, with no weight loss, so other recipes can use it as an ingredient."));
        if (answer is null)
        {
            return;
        }

        var name = answer.Text.Trim();
        if (Ingredients.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            || UsageEdits.Any(u => !ReferenceEquals(u, usage) && string.Equals(u.ToMadeIngredient?.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = MadeIngredientPrompt.NameTakenMessage(name);
            return;
        }

        var made = new MadeIngredientDraft(name, categories[answer.Choice].Id);
        AsUndoStep(() =>
        {
            var applied = usage;
            if (wasEditing)
            {
                try
                {
                    applied = ApplyUsageForm();
                }
                catch (BusinessRuleException ex)
                {
                    ErrorMessage = ex.Message;
                    return;
                }
            }

            var moving = applied.MovedToInHouse(made);
            ReplaceInRecipeOrder(applied, moving);
            RecomputeCosts();
            if (wasEditing)
            {
                OpenUsageForm(moving);
            }
        });
    }

    /// <summary>
    /// Puts the form's sub-assembly in the recipe and closes the form, without moving the tab showing: the caller
    /// decides which tab follows, since Apply shows the sub-assembly's own and a tab change shows the one left for.
    /// An edit that changes nothing is left alone, so merely looking at a section doesn't churn the recipe.
    /// </summary>
    private ProductUsageEdit ApplyUsageForm()
    {
        var authoredValue = UsageForm.AuthoredValue;
        ProductUsageEdit usage;

        if (UsageForm.EditingUsage is { IsLinked: true } linked)
        {
            // Its lines are typed into as a new section's are, and Save writes them where it's kept.
            var lines = IngredientLineRow.CompleteLines(UsageForm.FormLines, linked.DisplayName);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into {linked.DisplayName}, in grams.");
            }

            IReadOnlyList<SectionLine> lineEdits = [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
            usage = linked.WithLinkedForm(
                lineEdits, UsageForm.AuthoredField, authoredValue, CompleteAddIns(linked.DisplayName, lineEdits));
        }
        else if (UsageForm.IsFormEditingLines)
        {
            var role = UsageForm.EditingUsageRole
                ?? throw new BusinessRuleException("Pick the sub-assembly's role. It's what the sub-assembly is called.");

            var lines = IngredientLineRow.CompleteLines(UsageForm.FormLines, role.Name);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into the {role.Name}, in grams.");
            }

            // One of the product's own keeps its id, so Save rewrites it in place; a new one has none yet. One unlinked
            // and not saved yet stays the product's own, and one on its way to the Sub-Assemblies screen stays so.
            usage = new ProductUsageEdit(
                UsageForm.EditingUsage is { IsOwned: true } own ? own.SubAssemblyId : 0, role, [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], UsageForm.AuthoredField, authoredValue, linkedName: null, name: UsageForm.EditingUsageName, isOwn: UsageForm.EditingUsage?.IsOwned == true, isReleasing: UsageForm.EditingUsage?.IsReleasing == true, toMadeIngredient: UsageForm.EditingUsage?.ToMadeIngredient);
        }
        else
        {
            var source = UsageForm.EditingUsageSource?.SubAssembly
                ?? throw new BusinessRuleException("Pick the sub-assembly to link, or cancel the form.");
            var sourceName = source.Name;

            // The table's read-only lines are the ones LoadUsageSourceAsync loaded for this very pick; a pick
            // whose lines are still on their way has none yet, and linking it would make a batch of nothing.
            if (UsageForm.FormLines.Count == 0)
            {
                throw new BusinessRuleException($"{sourceName} is still loading its ingredients. Try again in a moment.");
            }

            IReadOnlyList<SectionLine> sourceLines =
                [.. UsageForm.FormLines.Where(l => l.Ingredient is not null).Select(l => new SectionLine(l.Ingredient!, l.Grams ?? 0m, l.IsFormulaBase, l.AuthoredBakersPercent))];

            var role = Roles.FirstOrDefault(r => r.Id == source.RoleId) ?? source.Role;
            usage = new ProductUsageEdit(
                source.Id,
                role,
                sourceLines,
                UsageForm.AuthoredField,
                authoredValue,
                source.Name,
                CompleteAddIns(sourceName, sourceLines),
                SectionLabelOf(source.Name, role));
        }

        if (UsageForm.EditingUsage is { } unchanged && usage.ShowsSameAs(unchanged))
        {
            CloseUsageForm();
            return unchanged;
        }

        // Sections in one role told apart by their names. A linked one being edited keeps its role and name, which are
        // the Sub-Assemblies screen's to change; and a section left as it was returned above, so it can still be looked
        // through, and Save says what's wrong when it's next changed.
        if (UsageForm.EditingUsage is not { IsLinked: true })
        {
            EnsureSectionsDistinct(usage, UsageForm.EditingUsage);
        }

        if (UsageForm.EditingUsage is { } editing)
        {
            ReplaceInRecipeOrder(editing, usage);
        }
        else
        {
            InsertInRecipeOrder(usage);
        }

        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeCosts();
        return usage;
    }

    /// <summary>
    /// Applies the form of a tab being left, so nothing typed into it is lost (the grids commit the same way when
    /// focus leaves them). False when a rule refused it: the message shows and the caller stays on the tab.
    /// </summary>
    private bool CommitUsageFormOnLeave()
    {
        if (!UsageForm.IsUsageFormOpen)
        {
            return true;
        }

        // An add form nothing was entered in has nothing to apply: it just closes, and its tab goes.
        if (IsUsageFormUntouched())
        {
            CloseUsageForm();
            return true;
        }

        // Applying a section is one undo step, wherever it's applied from: a tab left, or a Save. A refused one
        // changes only the message and whether the add-ins table is open, neither of which counts as a change, so
        // the step settles into nothing of its own accord. An apply closes the form, which isn't a change either:
        // the step is measured by what it did to the recipe, as Save's is (WithFormOf), so a tab left with nothing
        // typed into its form records nothing.
        History.Begin();
        try
        {
            ErrorMessage = null;
            ApplyForm();
            return true;
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            History.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// Applies a form left open, as Save does, so what's typed into it is in the recipe, and shows its section's tab with
    /// the form open on it again. One undo step, measured as leaving its tab measures one: by what it did to the recipe.
    /// An add form nothing was entered in is left as it is. A rule refusing the form throws, and the form stays as typed.
    /// </summary>
    private void ApplyOpenForm()
    {
        if (!UsageForm.IsUsageFormOpen || IsUsageFormUntouched())
        {
            return;
        }

        History.Begin();
        try
        {
            SelectRecipeTab(ApplyForm());
        }
        finally
        {
            History.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// The add-ins table as lines, refused if one is an ingredient <paramref name="baseLines"/> already has.
    /// The repository checks the same rule as it writes them; this is what puts the message on the screen
    /// the moment Apply is pressed, or the tab is left.
    /// </summary>
    private IReadOnlyList<SectionLine> CompleteAddIns(string subAssemblyName, IReadOnlyList<SectionLine> baseLines)
    {
        var addIns = IngredientLineRow.CompleteLines(UsageForm.FormAddIns, subAssemblyName);
        ProductRules.EnsureValidAddIns(
            [.. addIns.Select(a => (a.Ingredient.Id, a.Grams, a.Ingredient.Name))],
            subAssemblyName,
            [.. baseLines.Select(l => l.Ingredient.Id)]);

        return [.. addIns.Select(a => new SectionLine(a.Ingredient, a.Grams))];
    }

    /// <summary>
    /// Refuses <paramref name="usage"/> in place of <paramref name="replacing"/> when the recipe could no longer tell its
    /// sections apart: two in one role by the same name, or both without one (SubAssemblyRules.EnsureSectionsDistinct),
    /// or a name too long. SetRecipeAsync checks the same as it saves; this is what puts the message up the moment
    /// Apply is pressed, or the tab is left.
    /// </summary>
    private void EnsureSectionsDistinct(ProductUsageEdit usage, ProductUsageEdit? replacing)
    {
        List<ProductUsageEdit> sections = [.. UsageEdits.Where(u => !ReferenceEquals(u, replacing)), usage];
        SubAssemblyRules.EnsureSectionsDistinct([.. sections.Select(u => (u.RoleId, u.RoleName, u.Label))]);
        SubAssemblyRules.EnsureValidSectionName(usage.IsEditable ? usage.Name : null);
    }

    /// <summary>
    /// Tells each section whether the recipe has others in its role, so it's called by its name too ("Filling: Ube"),
    /// and its tab reads so. Run whenever the recipe's sections change.
    /// </summary>
    private void MarkRepeatedRoles()
    {
        var countsByRole = UsageEdits.CountBy(u => u.RoleId).ToDictionary();
        foreach (var usage in UsageEdits)
        {
            usage.IsRoleRepeated = countsByRole[usage.RoleId] > 1;
        }

        foreach (var tab in RecipeTabs.OfType<SubAssemblyRecipeTab>())
        {
            tab.RefreshHeader();
        }
    }

    private void CloseUsageForm()
    {
        UsageForm.IsUsageFormOpen = false;
        UsageForm.EditingUsage = null;
        RebaseSettledForm();
    }

    /// <summary>
    /// The sub-assembly form now holds another section's values, or none, without anything having been edited,
    /// so what Save measures itself against follows it: clicking through the recipe's tabs isn't a change to
    /// write. Only the form and the tabs move (ProductFormSnapshot.WithFormOf) - the recipe a tab left has
    /// already been applied into the sections by then, and those are measured as they were saved.
    ///
    /// Not while a restore is putting a form back: there the form's values are the user's, stepped back to.
    /// </summary>
    private void RebaseSettledForm()
    {
        if (!IsBusyApplying)
        {
            History.Rebase((settled, now) => settled.WithFormOf(now));
        }
    }

    /// <summary>The form on <paramref name="usage"/>, as it was last applied: the whole of its tab.</summary>
    private void OpenUsageForm(ProductUsageEdit usage)
    {
        UsageForm.EditingUsage = usage;
        UsageForm.IsAddingUsage = false;
        UsageForm.UsageFormTitle = usage.IsEditable ? $"Edit {usage.SectionName}" : $"Edit {usage.SectionName} ({usage.LinkedFromText})";
        UsageForm.IsFormEditingLines = usage.IsEditable;
        UsageForm.IsFormPickingSource = false;
        UsageForm.EditingUsageSource = null;
        RefreshFormRoleOptions();
        UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usage.RoleId) ?? usage.Role;
        UsageForm.EditingUsageName = usage.Name;

        // A linked one's lines show too, read-only: they're the Sub-Assemblies screen's. What is this product's is its piece
        // size and its add-ins, which go in the table under them, open if there are any.
        ShowFormLines(usage.Lines);
        ShowFormAddIns(usage.AddIns);
        UsageForm.SelectedFormLine = null;
        UsageForm.SelectedFormAddIn = null;
        UsageForm.ShowPieceSize(usage.AuthoredField, usage.AuthoredValue);
        UsageForm.IsUsageFormOpen = true;
        RebaseSettledForm();
    }

    /// <summary>
    /// A fresh form to add a sub-assembly, in place of any the form had open. It starts in New, and AddUsageCommand
    /// sets the way its drop-down item asked for straight after, which starts the form again in it.
    /// </summary>
    private void StartAddingUsage()
    {
        UsageForm.EditingUsage = null;
        UsageForm.IsAddingUsage = true;
        UsageForm.EditingUsageMode = UsageFormMode.New;
        UsageForm.IsUsageFormOpen = true;
        StartUsageFormMode();
        RebaseSettledForm();
    }

    /// <summary>
    /// The add form as it opened: nothing picked to link or copy, no line, and the piece size it started at. Its role
    /// alone isn't an entry.
    /// </summary>
    private bool IsUsageFormUntouched() =>
        UsageForm.IsAddingUsage
        && UsageForm.EditingUsageSource is null
        && string.IsNullOrWhiteSpace(UsageForm.EditingUsageName)
        && UsageForm.FormLines.All(l => l.IngredientId is null && l.Grams == 0)
        && UsageForm.FormAddIns.All(l => l.IngredientId is null && l.Grams == 0)
        && UsageForm.FormPackagingLines.All(l => l.PackagingMaterialId is null && l.QtyPerUnit == 0)
        && IsPieceSizeAsStarted(UsageForm.EditingUsageRole);

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.</summary>
    private void FormLinesEdited()
    {
        IngredientLineRow.Resolve(UsageForm.FormLines, Ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>An add-in picked, typed or deleted: it's priced, and the batch it joins follows.</summary>
    private void FormAddInsEdited()
    {
        IngredientLineRow.Resolve(UsageForm.FormAddIns, Ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>A packaging line picked, typed or deleted in the add form: it's priced, and the preview follows.</summary>
    private void FormPackagingEdited()
    {
        PackagingLineRow.Resolve(UsageForm.FormPackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
        UsageForm.Recompute();
    }

    /// <summary>Whether the form adds packaging follows its mode, role and source.</summary>
    private void FormAddsChanged()
    {
        UsageForm.RaisePackagingChanged();
        UsageForm.Recompute();
    }

    /// <summary>A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.</summary>
    private void PackagingLinesEdited()
    {
        PackagingLineRow.Resolve(Packaging.LineEdits, PackagingMaterials.ToDictionary(m => m.Id));
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

    /// <summary>The form's lines changed, or its source's were loaded: the batch weight, the other piece-size figure and the preview follow.</summary>
    private void FormLinesChanged()
    {
        UsageForm.RaiseLinesChanged();
        UsageForm.FollowPieceSize();
    }

    private void InsertInRecipeOrder(ProductUsageEdit usage)
    {
        var index = 0;
        while (index < UsageEdits.Count && ProductUsageEdit.RecipeOrder.Compare(UsageEdits[index], usage) <= 0)
        {
            index++;
        }

        UsageEdits.Insert(index, usage);
    }

    private void ReplaceUsage(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var wasEditing = ReferenceEquals(old, UsageForm.EditingUsage);
        if (wasEditing)
        {
            CloseUsageForm();
        }

        ReplaceInRecipeOrder(old, replacement);
        RefreshLinkableOptions();
        RecomputeCosts();

        // Its tab is its form, so the form opens again on what took its place.
        if (wasEditing)
        {
            OpenUsageForm(replacement);
        }
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in <paramref name="old"/>'s place, then moves it to where it sorts
    /// (a new name or role can move it). Replacing rather than removing keeps its tab, and the tab showing.
    /// </summary>
    private void ReplaceInRecipeOrder(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var index = UsageEdits.IndexOf(old);
        if (index < 0)
        {
            InsertInRecipeOrder(replacement);
            return;
        }

        UsageEdits[index] = replacement;
        var sortedIndex = UsageEdits.Count(u => !ReferenceEquals(u, replacement) && ProductUsageEdit.RecipeOrder.Compare(u, replacement) <= 0);
        if (sortedIndex != index)
        {
            UsageEdits.Move(index, sortedIndex);
        }
    }

    /// <summary>
    /// Keeps RecipeTabs' sub-assembly tabs in step with UsageEdits, index for index after a composite's Products tab
    /// (SectionTabOffset). A replaced section keeps
    /// its tab, and the form follows it when it was the one being edited (a refresh swaps in a copy with new
    /// costs), so neither a refresh nor an Apply moves the tab showing or loses the form's place.
    /// </summary>
    private void MirrorUsageTabs(NotifyCollectionChangedEventArgs e)
    {
        var offset = SectionTabOffset;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    RecipeTabs.Insert(offset + e.NewStartingIndex + i, new SubAssemblyRecipeTab((ProductUsageEdit)e.NewItems[i]!));
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++)
                {
                    RecipeTabs.RemoveAt(offset + e.OldStartingIndex);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
            {
                var replacement = (ProductUsageEdit)e.NewItems![0]!;
                var previous = (ProductUsageEdit)e.OldItems![0]!;
                ((SubAssemblyRecipeTab)RecipeTabs[offset + e.NewStartingIndex]).Usage = replacement;
                if (ReferenceEquals(previous, UsageForm.EditingUsage))
                {
                    if (replacement.IsEditable != previous.IsEditable)
                    {
                        // Its role was made product-owned in Settings, or no longer is: the form opens on it again, its
                        // lines now the product's to type into, or the Sub-Assemblies screen's to show.
                        OpenUsageForm(replacement);
                    }
                    else
                    {
                        UsageForm.EditingUsage = replacement;

                        // A linked one's lines as they now are, from the Sub-Assemblies screen or changed here and applied;
                        // lines typed into the form and not applied yet stay, repriced by RefreshFormLines.
                        if (replacement.IsLinked && IsFormShowing(previous.Lines))
                        {
                            ShowFormLines(replacement.Lines);
                        }
                    }
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
                RecipeTabs.Move(offset + e.OldStartingIndex, offset + e.NewStartingIndex);
                break;

            default:
                while (RecipeTabs.Count > offset && RecipeTabs[offset] is SubAssemblyRecipeTab)
                {
                    RecipeTabs.RemoveAt(offset);
                }

                break;
        }
    }

    private SubAssemblyRecipeTab TabFor(ProductUsageEdit usage) =>
        RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => ReferenceEquals(t.Usage, usage));

    /// <summary>
    /// Shows <paramref name="tab"/>, and does what showing it does even when it was already showing. The second
    /// RecipeTabShown comes once the tab strip has moved to the tab too, so it's the one that can take New
    /// Sub-Assembly's tab away.
    /// </summary>
    private void SelectRecipeTab(RecipeTab? tab)
    {
        _selectingRecipeTab = true;
        try
        {
            SelectedRecipeTab = tab;
        }
        finally
        {
            _selectingRecipeTab = false;
        }

        RecipeTabShown(tab);
    }

    /// <summary>
    /// A tab was picked in the strip, or SelectedRecipeTab set. A pick the screen makes itself (SelectRecipeTab,
    /// or the tabs rearranged) belongs to whatever step it's part of. The user's own runs between steps: WPF selects
    /// a tab as it takes the cursor, before the field it takes it from hears so (TabItem.OnPreviewGotKeyboardFocus),
    /// so that field's step is still open while the form changes over to the other tab's section, and closed after,
    /// it would record the change of section as an edit. So the step closes on the tab being left, keeping what
    /// was typed there, and another opens on the tab arrived at, for the field to close.
    /// </summary>
    private void RecipeTabPicked(RecipeTab? tab)
    {
        if (_selectingRecipeTab || _arrangingRecipeTabs || _revertingRecipeTab || IsBusyApplying || !History.IsStepOpen)
        {
            RecipeTabShown(tab);
            return;
        }

        History.End();
        try
        {
            RecipeTabShown(tab);
        }
        finally
        {
            History.Begin();
        }
    }

    /// <summary>
    /// A tab was picked, by the user or the screen. Leaving one applies the form it was showing, so nothing typed
    /// is lost; a rule refusing it keeps the tab it was on, with the message shown. A sub-assembly's tab then
    /// opens its form, which is all it shows, and New Sub-Assembly opens a form to add one (a form already adding
    /// one is left as it is). So a form still open is always one Save should apply. New Sub-Assembly's tab only
    /// lasts while it shows: once another does, its add has been applied or had nothing in it, and it goes.
    /// </summary>
    private void RecipeTabShown(RecipeTab? tab)
    {
        if (_arrangingRecipeTabs || _revertingRecipeTab)
        {
            return;
        }

        if (!ReferenceEquals(_shownRecipeTab, tab) && !CommitUsageFormOnLeave())
        {
            _revertingRecipeTab = true;
            try
            {
                SelectedRecipeTab = _shownRecipeTab;
            }
            finally
            {
                _revertingRecipeTab = false;
            }

            return;
        }

        _shownRecipeTab = tab;

        switch (tab)
        {
            case SubAssemblyRecipeTab subAssembly:
                _lastRecipeContentTab = tab;
                if (!ReferenceEquals(UsageForm.EditingUsage, subAssembly.Usage))
                {
                    OpenUsageForm(subAssembly.Usage);
                }

                break;

            case AddSubAssemblyRecipeTab:
                if (!UsageForm.IsUsageFormOpen || !UsageForm.IsAddingUsage)
                {
                    StartAddingUsage();
                }

                break;

            case PackagingRecipeTab or ComponentsRecipeTab:
                _lastRecipeContentTab = tab;
                break;

            case null:
                break;
        }

        // Not while SelectRecipeTab's pick is still reaching the tab strip, which would still be showing it.
        if (tab is not AddSubAssemblyRecipeTab && !_selectingRecipeTab)
        {
            RemoveAddSubAssemblyTab();
        }
    }

    /// <summary>Takes New Sub-Assembly's tab out of the strip, if it's there, with the strip's own pick ignored.</summary>
    private void RemoveAddSubAssemblyTab()
    {
        if (!RecipeTabs.Contains(AddSubAssemblyTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>
    /// After the sections were rebuilt (a product loaded, reloaded after Save, or cleared for a new one): the
    /// tab that showed if it's still there (the same sub-assembly's, a composite's Products, or Packaging), else the first
    /// tab: a composite's Products, else the first sub-assembly's, which is the first in role order, else Packaging. Another product's
    /// recipe has no tab that showed (<paramref name="previous"/> is null), so it opens on its first.
    /// </summary>
    /// <param name="previousIds">The sub-assemblies the recipe used before, which a new section saved since isn't among.</param>
    private void ReselectRecipeTab(RecipeTab? previous, ProductUsageEdit? previousUsage, HashSet<int> previousIds)
    {
        RecipeTab? tab = previous switch
        {
            PackagingRecipeTab => PackagingTabIfShown,
            ComponentsRecipeTab => ComponentsTabIfShown,
            _ => null,
        };
        if (previousUsage is not null)
        {
            var tabs = RecipeTabs.OfType<SubAssemblyRecipeTab>();

            // A linked sub-assembly is known by its id. A new section saved since is linked now, under an id the recipe
            // didn't have, in its role, and by the name it went by when it can be.
            tab = previousUsage.SubAssemblyId > 0
                ? tabs.FirstOrDefault(t => t.Usage.SubAssemblyId == previousUsage.SubAssemblyId)
                : tabs.Where(t => t.Usage.RoleId == previousUsage.RoleId && !previousIds.Contains(t.Usage.SubAssemblyId))
                    .OrderBy(t => t.Usage.Name == previousUsage.Name ? 0 : 1)
                    .FirstOrDefault();
        }

        // A product with neither has no tab to show: the card says how to start. A composite always has its Products.
        SelectRecipeTab(tab ?? ComponentsTabIfShown ?? RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault() ?? PackagingTabIfShown);
    }

    /// <summary>
    /// The sub-assemblies the form can link or copy. Linking offers only the Sub-Assemblies screen's (standalone) ones the
    /// recipe doesn't already link, never anything a product keeps; copying offers every other one, and other products'
    /// packaging. A pick that's no longer offered is cleared. The roles the form offers follow the recipe too
    /// (RefreshFormRoleOptions).
    /// </summary>
    private void RefreshLinkableOptions()
    {
        var linking = UsageForm.EditingUsageMode == UsageFormMode.Link;
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();

        // Packaging comes after the recipes, while the product has none: the packaging sets, then (to copy only) every
        // other product's packaging. A composite's own sections are only in product-owned roles, so it's offered those.
        UsageForm.LinkableSubAssemblies.MergeFrom(
            [
                .. _linkable
                    .Where(s => (!IsEditingComposite || s.Role?.IsProductOwned == true) && (linking
                        ? s.OwnerProductId is null && !usedIds.Contains(s.Id)
                        : s.OwnerProductId is null || s.OwnerProductId != _detailProductId))
                    .Select(s => new LinkableSubAssemblyOption(s)),
                .. HasPackaging ? [] : _packagingSets.Select(s => new LinkableSubAssemblyOption(s)),
                .. HasPackaging || linking ? [] : Products.Where(p => p.Id != _detailProductId).Select(p => new LinkableSubAssemblyOption(p)),
            ],
            o => o.Key);

        if (UsageForm.EditingUsageSource is { } picked && !UsageForm.LinkableSubAssemblies.Contains(picked))
        {
            UsageForm.ShowSource(null);
        }

        RefreshFormRoleOptions();
    }

    /// <summary>
    /// The roles the form offers (FormRoleOptions): every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names. The role picked stays picked while it's still offered: the picker can drop its pick as
    /// its list changes under it.
    /// </summary>
    private void RefreshFormRoleOptions()
    {
        var pickedRoleId = UsageForm.EditingUsageRole?.Id;
        // The Packaging role adds the product's packaging, which isn't a recipe section: offered only to add, and only
        // while there's none. A section already in the recipe can't turn into packaging. A composite's sections are only
        // in product-owned roles (CompositeProductRules.EnsureSectionRoles).
        UsageForm.FormRoleOptions.MergeFrom(
            [.. Roles.Where(r => r.IsPackaging ? UsageForm.IsAddingUsage && !HasPackaging : !IsEditingComposite || r.IsProductOwned)],
            r => r.Id);
        UsageForm.ShowRole(UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == pickedRoleId));
    }

    /// <summary>
    /// Points the recipe's sections, saved or not, at the refreshed ingredients and roles, and a linked
    /// section at its refreshed sub-assembly and lines, keeping each section's own piece size, so costs
    /// changed on other tabs reach the recipe and the cost summary. Only a section that shows something
    /// different is rebuilt. The packaging rows are pointed at their refreshed materials the same way.
    /// </summary>
    private void RefreshRecipeRows(IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);

        var changed = false;
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            ProductUsageEdit fresh;
            var saved = edit.SubAssemblyId > 0 ? linkedById.GetValueOrDefault(edit.SubAssemblyId) : null;
            var isOwnNow = saved is not null && saved.OwnerProductId is { } ownerId && ownerId == _detailProductId;
            if (saved is not null && edit.IsLinked && isOwnNow)
            {
                // Its role was made product-owned: the product owns it now, with any add-ins it had merged into its lines.
                var role = Roles.FirstOrDefault(r => r.Id == saved.RoleId) ?? saved.Role;
                fresh = new ProductUsageEdit(
                    saved.Id,
                    role,
                    [.. linkedLinesById[saved.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AuthoredField,
                    edit.AuthoredValue,
                    linkedName: null,
                    name: saved.SectionName);
            }
            else if (edit.IsLinked || (edit.IsOwned && saved is not null && !isOwnNow))
            {
                // A linked one, or one of its own whose role is no longer product-owned, which it links now.
                if (saved is not { } subAssembly)
                {
                    continue;
                }

                // Its lines, name and role come fresh from the Sub-Assemblies screen; its add-ins are this product's own,
                // so they only need their ingredients' new costs. Lines changed here and not saved yet stay, repriced.
                var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
                fresh = edit.WithLinked(
                    subAssembly.Name,
                    SectionLabelOf(subAssembly.Name, role),
                    role,
                    [.. linkedLinesById[subAssembly.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AreLinkedLinesEdited ? Repriced(edit.Lines, ingredientsById) : null,
                    Repriced(edit.AddIns, ingredientsById),
                    PricedSection.LinkedOwnerOf(subAssembly));
            }
            else
            {
                fresh = edit.With(Roles.FirstOrDefault(r => r.Id == edit.RoleId) ?? edit.Role, Repriced(edit.Lines, ingredientsById));
            }

            if (!fresh.ShowsSameAs(edit))
            {
                UsageEdits[i] = fresh;
                changed = true;
            }
        }

        // A renamed role or sub-assembly can move a section.
        if (changed)
        {
            var sorted = UsageEdits.Order(ProductUsageEdit.RecipeOrder).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                var from = UsageEdits.IndexOf(sorted[i]);
                if (from != i)
                {
                    UsageEdits.Move(from, i);
                }
            }
        }

        // The packaging lines at their materials' refreshed costs and units, in place, so a line being typed isn't lost.
        PackagingLineRow.Resolve(Packaging.LineEdits, PackagingMaterials.ToDictionary(m => m.Id));
    }

    /// <summary>
    /// The form's lines and add-ins at their ingredients' refreshed costs. It reprices in place rather than
    /// refilling the tables, so a refresh arriving while an add-in is being typed doesn't throw it away.
    /// </summary>
    private void RefreshFormLines()
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        IngredientLineRow.Resolve(UsageForm.FormLines, ingredientsById);
        IngredientLineRow.Resolve(UsageForm.FormAddIns, ingredientsById);
    }

    /// <summary>Puts <paramref name="lines"/> in the form's table, in place of whatever it held.</summary>
    private void ShowFormLines(IEnumerable<SectionLine> lines)
    {
        UsageForm.FormLines.Clear();
        UsageForm.FormLines.AddRange(lines.Select(l => new IngredientLineRow(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
    }

    /// <summary>Whether the form's lines table still shows <paramref name="lines"/>, with nothing typed into it since.</summary>
    private bool IsFormShowing(IReadOnlyList<SectionLine> lines) =>
        UsageForm.FormLines.Count == lines.Count
        && UsageForm.FormLines.Zip(lines).All(pair =>
            pair.First.IngredientId == pair.Second.Ingredient.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);

    /// <summary>The same for the add-ins table under it.</summary>
    private void ShowFormAddIns(IEnumerable<SectionLine> addIns)
    {
        UsageForm.FormAddIns.Clear();
        UsageForm.FormAddIns.AddRange(addIns.Select(a => new IngredientLineRow(a.Ingredient, a.Grams)));
    }

    /// <summary><paramref name="lines"/> with each ingredient swapped for its refreshed copy when that differs; unchanged lines are kept as they are.</summary>
    private static IReadOnlyList<SectionLine> Repriced(IReadOnlyList<SectionLine> lines, Dictionary<int, RawMaterial> ingredientsById) =>
        [.. lines.Select(l => ingredientsById.TryGetValue(l.Ingredient.Id, out var fresh) && !CollectionMerge.HasSameValues(fresh, l.Ingredient)
            ? new SectionLine(fresh, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)
            : l)];

    /// <summary>
    /// Unbuffered or Buffered, pressed: every figure moves to the other of the materials' costs. The lines tables' rows are priced
    /// again, the composite's products from what's already been read of them, and the sections from the costs they
    /// already hold, so nothing is read again, and nothing about the product changes.
    /// </summary>
    private void ToggleCostBasis()
    {
        CostBasis = CostBasis == CostBasis.Buffered ? CostBasis.Unbuffered : CostBasis.Buffered;
        this.RaisePropertyChanged(nameof(MaterialCostTooltip));
        foreach (var row in UsageForm.FormLines.Concat(UsageForm.FormAddIns))
        {
            row.CostBasis = CostBasis;
        }

        foreach (var row in UsageForm.FormPackagingLines.Concat(Packaging.LineEdits))
        {
            row.CostBasis = CostBasis;
        }

        ComponentsTable.Reprice();

        RecomputeCosts();
        UsageForm.Recompute();
        VersionHistory.Reprice(CostBasis);
    }

    /// <summary>The cost summary, from the recipe and packaging as they are now and the costing fields.</summary>
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
        decimal? componentPackaging = IsEditingComposite && EditingIncludesComponentPackaging
            ? lines.Sum(r => r.PackagingCostTotal)
            : null;
        Costing.Recompute(new ProductCostInputs
        {
            Parts = [.. components, .. UsageEdits.Select(u => (u.SectionName, u.CostPerPieceAt(CostBasis)))],
            QuantityRequired = EditingQuantityRequired,
            PackagingCosts = [.. Packaging.LineEdits.Select(l => l.CostPerUnit)],
            ComponentPackaging = componentPackaging,
            WastagePercent = EditingCategory?.WastagePercent ?? _wastagePercent,
            VatPercent = _vatPercent,
            PriceRoundingStep = _priceRoundingStep,
            CurrentPrice = EditingCurrentPrice,
            IsComposite = IsEditingComposite,
            HasOwnSections = IsEditingComposite && UsageEdits.Count > 0,
        });

        List<string> belowCost =
        [
            .. UsageEdits.SelectMany(u => u.Lines.Concat(u.AddIns)).Select(l => l.Ingredient)
                .Concat(Packaging.LineEdits.Select(l => l.PackagingMaterial).OfType<RawMaterial>())
                .Where(RawMaterialRules.IsBufferedBelowCost)
                .Select(m => m.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];
        BelowCostText = belowCost.Count switch
        {
            0 => null,
            1 => $"Prices have risen past the buffered cost of {belowCost[0]}, so this product is priced under what it costs. "
                + "Re-apply its target buffer on Raw Materials, or in Reports' Cost Buffers.",
            _ => $"Prices have risen past the buffered costs of {string.Join(", ", belowCost[..^1])} and {belowCost[^1]}, so this "
                + "product is priced under what they cost. Re-apply their target buffers on Raw Materials, or in Reports' Cost Buffers.",
        };
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

        // With the History tab showing, the pages are the version picked there, as the Scaling tab's are the recipe scaled.
        if (SelectedDetailsTab == HistoryTab)
        {
            return VersionHistory.SelectedRow is { } row && SelectedProduct is { } product
                ? VersionPrintout(row, product)
                : default(PreparedPrintout);
        }

        try
        {
            ApplyOpenForm();
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
    /// packaging and its costing, with a note when that isn't what's saved. With the Scaling tab showing, the pages are
    /// that tab instead, the recipe scaled. Its rows are already worked out: the tab is worked out again on every change while it shows.
    /// </summary>
    private ProductPrintout CurrentPrintout() => new(
        string.IsNullOrWhiteSpace(EditingName) ? (IsEditingComposite ? "New composite product" : "New product") : EditingName.Trim(),
        EditingCategory?.Name ?? "Uncategorized",
        ProductRules.StageTitle(SelectedProduct?.Stage ?? NewProductStage),
        CostUnitCaption,
        LastUpdatedText,
        _detailProductId is null
            ? ProductPrintout.NotSavedNote
            : History.IsDirty ? ProductPrintout.UnsavedChangesNote : null,
        DateTime.Now,
        [.. UsageEdits.Select(u => PrintedSection.From(u.ToPriced(), CostBasis))],
        HasPackaging
            ? PrintedPackaging.From(Packaging.LineEdits.Select(l => l.ToPriced()), Packaging.IsPackagingLinked ? Packaging.PackagingLinkedToText : null)
            : null,
        Costing.Costs,
        IsEditingComposite ? PrintedComponent.From(ComponentsTable.ComponentEdits) : null)
    {
        CostBasis = CostBasis,
        Scaling = SelectedDetailsTab == ScalingTab
            ? new PrintedScaling(
                this.Scaling.Units,
                EditingQuantityRequired > 1 ? RecipeScalingRules.PiecesFor(this.Scaling.Units, EditingQuantityRequired) : null,
                this.Scaling.Rows,
                this.Scaling.TotalCost)
            : null,
    };

    /// <summary>
    /// A version from the History tab on paper: its sections, packaging and costing as that Save left them, priced
    /// at its costs then, as the table's Then columns are, at the screen's cost basis. Titled with the version, dated when
    /// it was saved, and marked in the footer as from the history; the category and stage are the product's now, which a
    /// version doesn't keep.
    /// </summary>
    private ProductPrintout VersionPrintout(ProductVersionRow row, Product product)
    {
        var (versionProduct, detail) = ProductVersionSnapshot.ToDetail(product.Id, row.Content);
        return SavedProductReader.Build(versionProduct, detail, CostBasis).ToPrintout(DateTime.Now) with
        {
            ProductName = $"{row.Content.ProductName} ({row.Title})",
            CategoryName = product.Category?.Name ?? ProductRules.UncategorizedTitle,
            StageTitle = ProductRules.StageTitle(product.Stage),
            LastUpdatedText = row.SavedText,
            SaveNote = ProductPrintout.VersionNote,
        };
    }

    // Segoe Fluent Icons glyphs for the list's menu, the ones the screen's buttons use where there's one.
    private const string CompositeGlyph = "";
    private const string AddGlyph = "";
    private const string NewFolderGlyph = "";
    private const string MoveGlyph = "";
    private const string PromoteGlyph = "";
    private const string SendBackGlyph = "";
    private const string RenameGlyph = "";
    private const string CopyGlyph = "";
    private const string LinkGlyph = "";
    private const string DeleteGlyph = "";
    private const string RestoreGlyph = "";
    private const string PrintGlyph = "";
    private const string CostingGlyph = "";

    /// <summary>
    /// The menu a right-click opens on the list (ProductListCard): on <paramref name="node"/>'s row, which the View picks
    /// first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null. A
    /// product's menu covers every product picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(ProductTreeNode? node) => node switch
    {
        ProductLeafNode { IsDeleted: true } leaf => DeletedProductMenu(leaf),
        ProductFolderNode { IsDeletedFolder: true } => [],
        ProductLeafNode leaf => ProductsMenu(SelectedTreeProducts is var picked && picked.Contains(leaf) ? picked : [leaf]),
        ProductFolderNode { IsStage: true } stage => StageMenu(stage),
        ProductFolderNode folder => FolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [ProductTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or ProductTreeNode ? TreeMenuFor(node as ProductTreeNode) : [];

    /// <summary>Only products can be dragged, one or several: the folders are fixed, and a deleted product only comes back by Restore.</summary>
    bool IListTreeScreen.CanDrag(IEnumerable<object> nodes) => nodes.All(node => node is ProductLeafNode { IsDeleted: false });

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    IObservable<object?> IListTreeScreen.Drop(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        return moves.Count == 0
            ? Observable.Return<object?>(null)
            : MoveProductsCommand.Execute(moves).Select(_ => (object?)ProductTree.Find(moves[0].ProductId));
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (ProductTree.MovesTo): none unless every one is a record.</summary>
    private static IReadOnlyList<ProductMove> DropMoves(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { Count: > 0 } dragging && dragging.All(node => node is ProductLeafNode)
            ? ProductTree.MovesTo(target as ProductTreeNode, dragging.Cast<ProductLeafNode>().Select(leaf => leaf.Product))
            : [];

    private List<TreeMenuItem> ProductsMenu(IReadOnlyList<ProductLeafNode> leaves)
    {
        var products = leaves.Select(l => l.Product).ToList();
        var isOne = products.Count == 1;
        var toProduction = ProductTree.MovesTo(ProductStage.Production, null, keepCategory: true, products);
        var toResearch = ProductTree.MovesTo(ProductStage.ResearchAndDevelopment, null, keepCategory: true, products);

        List<TreeMenuItem> items = [];
        if (toProduction.Count > 0)
        {
            items.Add(new($"Promote {HowMany(toProduction)}to Production", MoveProductsCommand, toProduction) { Glyph = PromoteGlyph });
        }

        if (toResearch.Count > 0)
        {
            items.Add(new($"Send {HowMany(toResearch)}Back to Research and Development", MoveProductsCommand, toResearch) { Glyph = SendBackGlyph });
        }

        items.Add(new($"Move {(isOne ? string.Empty : $"{products.Count} Products ")}to")
        {
            Glyph = MoveGlyph,
            Items = [MoveToStage(ProductStage.Production), MoveToStage(ProductStage.ResearchAndDevelopment)],
        });
        items.Add(new($"Apply Costing Defaults{(isOne ? string.Empty : $" to {products.Count} Products")}…", ApplyCostingDefaultsCommand, products)
        {
            Glyph = CostingGlyph,
        });

        if (isOne)
        {
            var leaf = leaves[0];
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("Rename…", RenameProductCommand, leaf) { Gesture = "F2", Glyph = RenameGlyph },
                new("Clone…", CloneProductCommand, leaf) { Glyph = CopyGlyph },
                new("Create Variant…", CreateVariantOfCommand, leaf) { Glyph = LinkGlyph },
                TreeMenuItem.Separator,
                new("Delete", DeleteProductCommand, leaf) { Gesture = "Del", Glyph = DeleteGlyph },
            ]);
        }

        return items;

        // "3 Products " while several are picked, counting only those that would move; nothing for one.
        string HowMany(IReadOnlyList<ProductMove> moves) =>
            isOne ? string.Empty : $"{moves.Count} {(moves.Count == 1 ? "Product" : "Products")} ";

        // A stage's folders, each moving every product picked that isn't in it already, and greyed when none would move.
        TreeMenuItem MoveToStage(ProductStage stage)
        {
            List<TreeMenuItem> folders =
            [
                .. Categories.Select(c => MoveToFolder(c.Name, c.Id)),
                MoveToFolder(ProductRules.UncategorizedTitle, null),
            ];
            return new(ProductRules.StageTitle(stage)) { Items = folders, IsEnabled = folders.Any(f => f.IsEnabled) };

            TreeMenuItem MoveToFolder(string title, int? categoryId)
            {
                var moves = ProductTree.MovesTo(stage, categoryId, keepCategory: false, products);
                return new(title, MoveProductsCommand, moves) { IsEnabled = moves.Count > 0 };
            }
        }
    }

    /// <summary>A product in the Deleted folder: it can only come back, or go for good.</summary>
    private List<TreeMenuItem> DeletedProductMenu(ProductLeafNode leaf) =>
    [
        new("Restore", RestoreProductCommand, leaf) { Glyph = RestoreGlyph },
        TreeMenuItem.Separator,
        new("Delete Permanently", DeletePermanentlyCommand, leaf) { Gesture = "Del", Glyph = DeleteGlyph },
    ];

    private List<TreeMenuItem> StageMenu(ProductFolderNode stage) =>
    [
        new("New Product Here", NewProductInCommand, stage) { Glyph = AddGlyph },
        new("New Composite Product Here", NewCompositeProductInCommand, stage) { Glyph = CompositeGlyph },
        new("New Category…", NewCategoryCommand, stage) { Glyph = NewFolderGlyph },
        TreeMenuItem.Separator,
        new("Print Stage", PrintFolderCommand, stage) { Glyph = PrintGlyph },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand, stage),
        new("Collapse All", CollapseAllCommand, stage),
    ];

    private List<TreeMenuItem> FolderMenu(ProductFolderNode folder)
    {
        List<TreeMenuItem> items =
        [
            new("New Product Here", NewProductInCommand, folder) { Glyph = AddGlyph },
            new("New Composite Product Here", NewCompositeProductInCommand, folder) { Glyph = CompositeGlyph },
        ];

        // What the folder shows: with a search on, its matches.
        var toProduction = ProductTree.MovesTo(ProductStage.Production, null, keepCategory: true, folder.Products.Select(l => l.Product));
        if (toProduction.Count > 0)
        {
            items.Add(new("Promote All to Production", MoveProductsCommand, toProduction) { Glyph = PromoteGlyph });
        }

        items.Add(new("Print Folder", PrintFolderCommand, folder) { Glyph = PrintGlyph });

        if (folder.IsCategory)
        {
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("New Category…", NewCategoryCommand, folder) { Glyph = NewFolderGlyph },
                new("Rename Category…", RenameCategoryCommand, folder) { Gesture = "F2", Glyph = RenameGlyph },
                new("Delete Category", DeleteCategoryCommand, folder) { Gesture = "Del", Glyph = DeleteGlyph },
            ]);
        }

        return items;
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Product", NewProductInCommand) { Glyph = AddGlyph },
        new("New Composite Product", NewCompositeProductInCommand) { Glyph = CompositeGlyph },
        new("New Category…", NewCategoryCommand) { Glyph = NewFolderGlyph },
        TreeMenuItem.Separator,
        new("Print All Products", PrintAllCommand) { Glyph = PrintGlyph },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Rename: asks for the new name, and saves it at once. On the product the form shows, the name box takes
    /// it, and the form's undo steps take it as given, as a move gives them its category: whatever else is typed there
    /// stays to be saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameProductAsync(ProductLeafNode leaf)
    {
        var product = leaf.Product;
        var name = (await PromptForName.Handle(new TextPromptRequest("Rename Product", "Name", product.Name)))?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, product.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _productRepository.RenameAsync(product.Id, name, CancellationToken.None);

            // What's named after it follows: its own sub-assemblies, as other products' pickers list them.
            await LoadCommand.Execute();
            if (_editingId == product.Id)
            {
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
        });
    }

    /// <summary>
    /// The list's New Category: asks for a name and adds it, then picks its folder in the stage it was asked in. A name
    /// that's a category already picks that one's folder instead.
    /// </summary>
    private async Task NewCategoryAsync(ProductFolderNode? from)
    {
        var name = (await PromptForName.Handle(new TextPromptRequest(
            "New Category", "Name", string.Empty, "A category is a folder in both Production and Research and Development.")))?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var category = Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? await SaveNewCategoryAsync(name);
            if (category is not null
                && from is not null
                && ProductTree.CategoryFolder(from.Stage, category.Id) is { } folder
                && ProductTree.StageFolder(from.Stage).Children.Contains(folder))
            {
                SelectedTreeNode = folder;
            }
        });
    }

    /// <summary>The list's Rename Category: renames the category, which is its folder in both stages.</summary>
    private async Task RenameCategoryAsync(ProductFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        var name = (await PromptForName.Handle(new TextPromptRequest(
            "Rename Category", "Name", category.Name, $"{category.Name} is a folder in both Production and Research and Development: it's renamed in both.")))?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, category.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var renamed = new ProductCategory { Id = category.Id, Name = name };
            var validation = _categoryValidator.Validate(renamed);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            await _categoryRepository.UpdateAsync(renamed, CancellationToken.None);
            await ReloadCategoriesAsync();
        });
    }

    /// <summary>
    /// The list's Delete Category: once it's confirmed, deletes the category, whose products are left uncategorized, in
    /// whichever stage they're in.
    /// </summary>
    private Task DeleteCategoryAsync(ProductFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return Task.CompletedTask;
        }

        var count = Products.Count(p => p.CategoryId == category.Id);
        var products = count switch
        {
            0 => "it holds no products",
            1 => "its one product is left uncategorized",
            _ => $"its {count} products are left uncategorized",
        };

        return RunWithConfirmationAsync(
            $"Delete the '{category.Name}' category? Its folder goes from both Production and Research and Development, and {products}. This can't be undone.",
            async () =>
            {
                await _categoryRepository.DeleteAsync(category.Id, CancellationToken.None);

                // Its products lost it in the database: every list is read again, and the form's undo steps forget it, so
                // none puts back a category there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.CategoryId == category.Id ? snapshot with { CategoryId = null } : snapshot);
            });
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
            ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
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

    public void BeginCategoryEdit()
    {
        if (!_isCommittingCategory)
        {
            _categoryBeforeEdit = EditingCategory;
        }
    }

    /// <summary>
    /// ICategoryPickerScreen, as on Raw Materials: a name that isn't a category yet is offered as a new one, saved and
    /// picked, so a product can go in a new category without a trip to Settings. Save saves the product's choice. A
    /// cancelled prompt puts back the category the picker had; a name that turns out to be listed is picked instead.
    /// </summary>
    public async Task CommitCategoryTextAsync()
    {
        var typed = CategorySearchText.Trim();
        if (_isCommittingCategory || typed.Length == 0 || IsNamed(EditingCategory, typed))
        {
            return;
        }

        _isCommittingCategory = true;
        try
        {
            if (Categories.FirstOrDefault(c => IsNamed(c, typed)) is { } listed)
            {
                AsUndoStep(() => EditingCategory = listed);
                return;
            }

            var name = (await PromptForNewCategory.Handle(typed))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
            }
            else if (Categories.FirstOrDefault(c => IsNamed(c, name)) is { } named)
            {
                AsUndoStep(() => EditingCategory = named);
            }
            else
            {
                await AddCategoryAsync(name);
            }

            // The picker shows the category it now holds rather than what was typed: the new one, or the one put back.
            CategorySearchText = EditingCategory?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Nothing awaits this: it's the picker's, from the keyboard or the focus leaving.
            ShowFailure(ex);
        }
        finally
        {
            _isCommittingCategory = false;
        }

        static bool IsNamed(ProductCategory? category, string name) =>
            string.Equals(category?.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Saves a category and picks it. A refused name puts back the category the picker had.</summary>
    private async Task AddCategoryAsync(string name)
    {
        var added = false;
        await RunSafelyAsync(async () =>
        {
            if (await SaveNewCategoryAsync(name) is not { } category)
            {
                return;
            }

            AsUndoStep(() => EditingCategory = category);
            SuccessMessage = $"{category.Name} was added.";
            added = true;
        });

        if (!added)
        {
            AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
        }
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
