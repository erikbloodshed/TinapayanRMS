using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// One tab of the Products view (<see cref="ProductsState"/>): a product's form, a new one's, or a new composite's. A
/// product's recipe (the sub-assemblies it links and new ones not saved yet, each at its own piece size, and its packaging),
/// its costing, its packaging link, and cloning it or making a variant of it. The name, description, recipe, packaging and
/// costing are edited in memory, and Save writes them together (IProductEditor.SaveAsync). A composite product's recipe
/// is the products it holds instead, in its Products tab (<see cref="ProductComponents.ComponentEdits"/>), each bringing its
/// recipe cost. Its parts each hold a piece of it: the recipe's sections and tabs (<see cref="Recipe"/>), the section form
/// (<see cref="UsageForm"/>), the Packaging tab (<see cref="Packaging"/>), a composite's Products table
/// (<see cref="ComponentsTable"/>), the Costing tab (<see cref="Costing"/>) and the History tab (<see cref="VersionHistory"/>).
/// The form keeps its own fields, which product it shows, Save, the costs, and the undo history over them all; the view keeps
/// the list, the products, categories, roles and materials its pickers offer, and the load that brings them (<see cref="View"/>).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Baseline of 2026-11-20 (ADR 0004): moves to a use case and a draft as its screen is migrated (ADR 0002).")]
public sealed class ProductForm : RecordScreenState<ProductFormSnapshot>, IDisposable, IRecordScreen, IRevertScreen, ITabForm,
    IProductDraftHost, IProductRecipeHost, IProductPackagingHost, IProductVersionHistoryHost, IProductUsageFormHost, IProductComponentsHost
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, Clone, then History.</summary>
    public const int RecipeTab = 0;

    /// <summary>Recipe 0, Costing 1, then Clone.</summary>
    public const int LinksTab = 2;

    /// <summary>After Clone: the product's versions (ProductVersionHistory). A saved product's only, as Clone is.</summary>
    public const int HistoryTab = 3;

    private readonly ProductsState _view;

    // What the form reads and writes, and the Category picker adds. The view's.
    private readonly IProductQueries _queries;
    private readonly IProductEditor _editor;
    private readonly IProductCategoryEditor _categoryEditor;

    /// <summary>The form's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>
    /// Each product picked, by the user or for them (Open, a clone made, Save's reload), nothing picked included: what
    /// loads a product into the form. Fed by <see cref="SelectedProduct"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<Product?> _picks = new(null);

    /// <summary>The Category picker's naming of a new category (BeginCategoryEdit, CommitCategoryTextAsync).</summary>
    private readonly CategoryPicker<ProductCategory> _categoryPicker;

    private readonly Subject<PrintScope> _printRequests = new();

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>A product whose versions the History tab should read: pushed as the tab shows, and after a Save.</summary>
    private readonly Subject<int> _versionReads = new();

    /// <summary>The product whose recipe, packaging, costing and links the form holds, or null for a new product.</summary>
    private int? _detailProductId;

    /// <summary>
    /// The detail Save has read back already, for the load its selection starts to show at once instead of reading it
    /// again (ShowSavedProduct).
    /// </summary>
    private (int ProductId, ProductDetail Detail)? _preloadedDetail;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Baseline of 2026-11-20 (ADR 0004): moves to a use case and a draft as its screen is migrated (ADR 0002).")]
    internal ProductForm(
        ProductsState view,
        IProductQueries queries,
        IProductEditor editor,
        IProductCategoryEditor categoryEditor,
        ILogger logger)
        : base(logger)
    {
        _view = view;

        // The product as it's edited: its own fields and its parts' drafts, the costing first among them. Its fields are the
        // form's to the page, under the same names (DraftChanged).
        Draft = new ProductDraft(this, new ProductLists(Ingredients, Roles, PackagingMaterials, Products));
        Draft.PropertyChanged += DraftChanged;
        _categoryPicker = new CategoryPicker<ProductCategory>(
            () => EditingCategory, category => AsUndoStep(() => EditingCategory = category), () => Categories, c => c.Name,
            () => CategorySearchText, text => CategorySearchText = text, async typed => await PromptForNewCategory.Handle(typed),
            SaveNewCategoryAsync, RunSafelyAsync, message => SuccessMessage = message, ex => ShowFailure(ex));
        _queries = queries;
        _editor = editor;
        _categoryEditor = categoryEditor;

        // The lists the parts read are the view's, which each load merges into.
        Packaging = new ProductPackaging(this, Draft.Packaging, queries);
        UsageForm = new ProductUsageForm(this, Ingredients);
        ComponentsTable = new ProductComponents(this, Draft.Components, queries);
        VersionHistory = new ProductVersionHistory(this, Products, Ingredients, PackagingMaterials, Roles, queries, editor);

        // The part that holds the recipe. It subscribes to what it holds alone (the recipe's tabs and which of its tables
        // shows), as it's made; what reaches from one part to another, or to the form, is subscribed below, in the order it
        // always ran.
        Recipe = new ProductRecipe(this, UsageForm, Draft.Recipe, queries);

        // What follows the form's fields as they change is called by their setters (SelectionChanged and the rest), and what
        // follows a part's by the part's events, subscribed below. What follows here runs in this order: several run as
        // they're made, WirePicks' included, so each comes after what it reads.
        WireDetailsTabs();
        WirePicks();
        WireUsageForm();
        WireCosts();
        WireUnsavedChanges();

        // A form opened after the view's first load takes what it read: Settings' costing defaults, which every product is
        // priced with, the folders a new product is filed in, and the packaging sets.
        TakeViewsLastLoad();

        // The commands. Making one runs nothing, so they can come in any order.
        DeleteCommand = new ScreenCommand(() => _view.DeleteAsync(SelectedProduct!, this), () => SelectedProduct is not null);

        // A clone copies the product as saved, and a variant links it as saved: unsaved edits stay with this one.
        bool CanMakeFromSelected() => SelectedProduct is not null && !string.IsNullOrWhiteSpace(EditingCloneName);
        CloneCommand = new ScreenCommand(() => _view.MakeFromAsync(SelectedProduct!.Id, EditingCloneName, isVariant: false, this), CanMakeFromSelected);
        CreateVariantCommand = new ScreenCommand(() => _view.MakeFromAsync(SelectedProduct!.Id, EditingCloneName, isVariant: true, this), CanMakeFromSelected);

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
        // message, and the Print dialog doesn't open. It stays enabled while a picked product's details are on their way,
        // and does nothing if clicked then: disabled for that moment, it flickered on every pick. (Save is off then
        // anyway, with nothing unsaved, so it doesn't flicker.)
        PrintCommand = new ScreenCommand(
            () => RunSafelyAsync(() =>
            {
                if (IsReadyToPrint("print"))
                {
                    _printRequests.OnNext(new PrintScope.ThisProduct());
                }
            }));

        // The same pages as Print, straight to a file.
        ExportPdfCommand = new ScreenCommand(
            () => RunSafelyAsync(async () =>
            {
                if (IsReadyToPrint("export") && PreparePrintout() is ProductPrintout printout
                    && await ExportPdf.Handle(ProductPrintSet.Of(printout)) is { } path)
                {
                    SuccessMessage = $"Saved {Path.GetFileName(path)} in {Path.GetDirectoryName(path)}.";
                }
            }));

        ExportCostingCommand = new ScreenCommand(
            () => RunSafelyAsync(async () =>
            {
                if (IsReadyToPrint("export") && PreparePrintout() is ProductPrintout printout
                    && await ExportCosting.Handle(printout) is { } path)
                {
                    SuccessMessage = $"Saved {Path.GetFileName(path)} in {Path.GetDirectoryName(path)}.";
                }
            }));

        ToggleCostBasisCommand = ScreenCommand.Of(ToggleCostBasis);
    }

    /// <summary>
    /// Whether the product's pages can be asked for now: its details have arrived, and either the version to
    /// <paramref name="verb"/> is picked on the History tab, whose pages aren't the form's (PreparePrintout), or a form left
    /// open has been applied. A rule refusing that form throws, for the command to show.
    /// </summary>
    private bool IsReadyToPrint(string verb)
    {
        if (!AreDetailsReady)
        {
            return false;
        }

        if (SelectedDetailsTab != HistoryTab)
        {
            Recipe.ApplyOpenForm();
            return true;
        }

        if (VersionHistory.SelectedRow is null)
        {
            ErrorMessage = $"Pick a version in the History tab's table to {verb} it.";
            return false;
        }

        return true;
    }

    /// <summary>The view this form is a tab of: its list, where the form's product is picked, and the load.</summary>
    public ProductsState View => _view;

    private int _editingId;
    /// <summary>The product the form holds: 0 for a new one not saved yet.</summary>
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
    /// ITabForm: the product the tab is for, as its tab and its address know it: the one picked, from the pick on, not only
    /// once its details are read (<see cref="EditingId"/>), or a link opening another in the preview tab gave the tab the one
    /// before's address meanwhile.
    /// </summary>
    public int RecordId => SelectedProduct?.Id ?? EditingId;

    /// <summary>ITabForm: the name on the tab, the header's.</summary>
    public string TabTitle => HeaderTitle;

    /// <summary>The product whose details the form holds, which the view's load reads what it shows from elsewhere for; null for a new one.</summary>
    internal int? DetailProductId => _detailProductId;

    /// <summary>The recipe: its sections and their tabs, the Packaging tab and a composite's Products tab, and the section form's flow.</summary>
    public ProductRecipe Recipe { get; }

    /// <summary>The product as it's edited: its own fields, its parts' drafts and the costing (<see cref="ProductDraft"/>).</summary>
    public ProductDraft Draft { get; }

    /// <summary>The Costing tab: the costing percentages, each metric's amount, and the cost summary (Costs). The draft's.</summary>
    public ProductCosting Costing => Draft.Costing;

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

    /// <summary>Every product in use (none of the deleted), for the packaging link and clone pickers. The view's.</summary>
    public ReadOnlyObservableCollection<Product> Products => _view.Products;

    /// <summary>The products in the list's Deleted folder, the view's: no picker offers one.</summary>
    public IReadOnlyList<Product> DeletedProducts => _view.DeletedProducts;

    /// <summary>
    /// The Products list's folders, the view's, as last read. A new product started in one of them goes to its stage with it,
    /// should it have moved to the other since (NewProductPlace.FoldersRead, <see cref="TakeFolders"/>), as Save would file
    /// it there; the header names its place as it is now (NewProductTag).
    /// </summary>
    public IReadOnlyList<ProductFolder> Folders => _view.Folders;

    /// <summary>Where Save files a new product: its stage and folder, as New, New Product Here and Settings decide them.</summary>
    public NewProductPlace NewProductPlace { get; } = new();

    /// <summary>What the Category picker lists, the view's. A name that isn't one of them is added by typing it (CommitCategoryTextAsync).</summary>
    public ReadOnlyObservableCollection<ProductCategory> Categories => _view.Categories;

    /// <summary>The roles, the view's.</summary>
    public ObservableCollection<SubAssemblyRole> Roles => _view.Roles;

    /// <summary>The ingredients only, for a sub-assembly's lines, the view's: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients => _view.Ingredients;

    /// <summary>The packaging materials only, the view's: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials => _view.PackagingMaterials;

    /// <summary>The view read the folders again: a new product's place follows a folder moved to the other stage, and the header's tag its path.</summary>
    internal void TakeFolders(IReadOnlyList<ProductFolder> folders)
    {
        NewProductPlace.FoldersRead(folders);
        RaisePropertyChanged(nameof(Folders));
    }

    /// <summary>What the view's last load read, for a form opened after it: Settings' defaults, the folders and the packaging sets.</summary>
    private void TakeViewsLastLoad()
    {
        if (_view.CostingDefaults is { } defaults)
        {
            Costing.SetDefaults(defaults);
            NewProductPlace.TakeDefault(defaults.NewProductStage, formIsNew: true);
        }

        NewProductPlace.FoldersRead(_view.Folders);
        Packaging.TakePackagingSets(_view.PackagingSets);
    }

    /// <summary>
    /// The product showing is a composite: made of other products (its Products tab), its own packaging, and sections only
    /// in product-owned roles (a topping, a filling), with no Quantity Required of its own (the draft's).
    /// </summary>
    public bool IsEditingComposite
    {
        get => Draft.IsEditingComposite;
        private set => Draft.IsEditingComposite = value;
    }

    /// <summary>The product showing is made of sub-assemblies, as every product that isn't a composite is.</summary>
    public bool IsEditingSingle => !IsEditingComposite;

    private Product? _selectedProduct;
    /// <summary>
    /// The product the form is about. Only <see cref="Pick"/> loads it into the form (_picks): a load points it at its fresh
    /// copy, leaving the form and whatever's typed into it alone.
    /// </summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedProduct, value))
            {
                RaisePropertyChanged(nameof(RecordId));
                SelectionChanged();
            }
        }
    }

    /// <summary>
    /// The user picks <paramref name="product"/> (a list's row, Open), or nothing, as New does: it's loaded into the form,
    /// unless it's the one showing.
    /// </summary>
    public void Pick(Product? product)
    {
        if (product != SelectedProduct)
        {
            SelectedProduct = product;
            _picks.OnNext(product);
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
        // The view's list follows the product showing while this form shows, and one opened ends the new product's row.
        _view.FormShowsAnother(this, SelectedProduct is not null);
        RaisePropertyChanged(nameof(TabTitle));
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

    /// <summary>The name typed, which the header and the tab show (the draft's).</summary>
    public string EditingName
    {
        get => Draft.EditingName;
        set => Draft.EditingName = value;
    }

    public string EditingDescription
    {
        get => Draft.EditingDescription;
        set => Draft.EditingDescription = value;
    }

    /// <summary>How many finished pieces make one sellable unit (the draft's).</summary>
    public int EditingQuantityRequired
    {
        get => Draft.EditingQuantityRequired;
        set => Draft.EditingQuantityRequired = value;
    }

    /// <summary>What the shop charges for one sellable unit now, with VAT; null when not entered (the draft's).</summary>
    public decimal? EditingCurrentPrice
    {
        get => Draft.EditingCurrentPrice;
        set => Draft.EditingCurrentPrice = value;
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

    /// <summary>The product's category, or null for none (the draft's). It can set the Wastage % the product is priced with.</summary>
    public ProductCategory? EditingCategory
    {
        get => Draft.EditingCategory;
        set => Draft.EditingCategory = value;
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

    public string EditingCloneName
    {
        get => Draft.EditingCloneName;
        set => Draft.EditingCloneName = value;
    }

    /// <summary>
    /// A field of the draft's changed: the form's own of that name, which the page binds, raised; the tab's title follows the
    /// name and the kind.
    /// </summary>
    private void DraftChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(ProductDraft.EditingName) or nameof(ProductDraft.IsEditingComposite))
        {
            RaisePropertyChanged(nameof(TabTitle));
        }
    }

    /// <summary>The view's load: its lists read again, and every open form kept as it is (ProductsState.LoadCommand).</summary>
    public ScreenCommand LoadCommand => _view.LoadCommand;

    /// <summary>The view's New: a new product, in the preview tab or a tab of its own (ProductsState.NewCommand).</summary>
    public ScreenCommand NewCommand => _view.NewCommand;

    /// <summary>The view's New composite product: a new product made of other products, with its own packaging and toppings or fillings (ProductsState.NewCompositeCommand).</summary>
    public ScreenCommand NewCompositeCommand => _view.NewCompositeCommand;

    public ScreenCommand SaveCommand { get; }

    /// <summary>
    /// Revert: reads the product back as it was last saved, throwing away everything staged since — the details, the
    /// recipe's sections and their lines, the packaging, the packs and the costing. The sub-assembly form has no
    /// discard of its own, so this is what abandons a section being added or edited, along with the rest.
    /// </summary>
    public ScreenCommand RevertCommand { get; }

    /// <summary>
    /// Print: asks for the Print dialog (<see cref="PrintRequests"/>), which shows the product's recipe and costing as the
    /// screen shows them, unsaved edits included (<see cref="PreparePrintout"/>). A sub-assembly form left open is applied first, as Save applies it, so a rule
    /// refusing it shows its message here and the tab doesn't open.
    /// </summary>
    public ScreenCommand PrintCommand { get; }

    /// <summary>
    /// Export to PDF: saves the pages Print would show for the product (<see cref="PreparePrintout"/>) as a PDF file, through
    /// <see cref="ExportPdf"/>, and says where it went.
    /// </summary>
    public ScreenCommand ExportPdfCommand { get; }

    /// <summary>Input: the pages to save. Output: where they were saved, or null when the user cancelled. The screen answers it (PdfExport).</summary>
    public Interaction<ProductPrintSet, string?> ExportPdf { get; } = new();

    /// <summary>
    /// The Costing tab's Export to Excel: the product's costing and recipe as the screen shows them, unsaved edits and the
    /// cost basis included, to a workbook through <see cref="ExportCosting"/>, and says where it went. The same checks as
    /// Print first.
    /// </summary>
    public ScreenCommand ExportCostingCommand { get; }

    /// <summary>Input: the product as it would print. Output: where the workbook was saved, or null when cancelled. The screen answers it (CostingWorkbook).</summary>
    public Interaction<ProductPrintout, string?> ExportCosting { get; } = new();

    /// <summary>Unbuffered: switches every cost on the screen between the materials' buffered costs and their true ones (ProductCosting.CostBasis).</summary>
    public ScreenCommand ToggleCostBasisCommand { get; }

    /// <summary>
    /// What a Print here asks to print: the header's, This product. The view passes it on with its list's (ProductsState.PrintRequests),
    /// ScreenLinks opens the Print dialog with that choice (PrintDialogState.Pick), and the dialog asks the form showing's
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

    /// <summary>Create Variant on the Clone tab: a new product that links this one's recipe and packaging (IProductEditor.MakeFromAsync).</summary>
    public ScreenCommand CreateVariantCommand { get; }

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with.
    /// Output: the name to add it as, or null if it was cancelled. The page answers it with a dialog (Prompts).
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

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
        // A pick loads the product into the form, in these three steps, in this order. Only a pick does (Pick, through
        // _picks): a load points the selection at its fresh copy, so nothing here has to tell the two apart.
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
        Packaging.Draft.LineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(PackagingLinesEdited));

        // The same for a bulk packaging line, and the products a bulk pack holds.
        Packaging.Draft.BulkQuantityChanged += () => _updates.Run(PackagingLinesEdited);
        Packaging.Draft.BulkEdits.ToObservableChangeSet()
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
        ComponentsTable.Draft.ComponentEdits.ToObservableChangeSet()
            .AutoRefreshOnObservable(r => r.WhenAnyPropertyChanged(
                nameof(ProductComponentRow.ComponentProductId),
                nameof(ProductComponentRow.Quantity),
                nameof(ProductComponentRow.RecipeCost),
                nameof(ProductComponentRow.PackagingCost)))
            .Subscribe(_ => _updates.Run(ComponentsEdited));

        // A composite's products' packaging joining its own or leaving it. The costing percentages say so themselves, and
        // the product's price, quantity and category through their setters (ProductDraft).
        ComponentsTable.Draft.IncludesPackagingChanged += InvalidateCosts;

        // Broadly besides: any change to a packaging or a composite's table is something the costs may be worked out from.
        RowChanges.OnAnyChange(Packaging.Draft.LineEdits, InvalidateCosts);
        RowChanges.OnAnyChange(Packaging.Draft.BulkEdits, InvalidateCosts);
        RowChanges.OnAnyChange(ComponentsTable.Draft.ComponentEdits, InvalidateCosts);
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
        Packaging.Draft.Changed += History.RefreshDirty;
        ComponentsTable.Draft.Changed += History.RefreshDirty;
        UsageForm.PropertyChanged += (_, _) => History.RefreshDirty();
        WatchForSave(Recipe.UsageEdits);

        // The tables typed into, on their rows themselves: a quantity or grams typed while the cell has the cursor. Not
        // through the figures they move (the costs, the section form's), which raise nothing until they've been read.
        // Marking the dirty check stale is cheap, so a pass through every row costs one comparison, when Save is drawn.
        RowChanges.OnAnyChange(UsageForm.FormLines, History.RefreshDirty);
        RowChanges.OnAnyChange(UsageForm.FormAddIns, History.RefreshDirty);
        ((INotifyPropertyChanged)Recipe.RecipeTabs).PropertyChanged += (_, _) => Recipe.RebaseSettledForm();
    }

    /// <summary>
    /// What the form showed as the view's load set out, and what the load found for it: the view reads, for every open form
    /// at once, the sub-assemblies its sections link, its packaging link, and what a composite's products bring.
    /// </summary>
    internal sealed class Load
    {
        public int? DetailProductId { get; init; }

        /// <summary>
        /// The sub-assemblies the saved sections are. A linked section reads a sub-assembly kept on the Sub-Assemblies screen,
        /// which can change there (its lines, name or role), so those are fetched again; the product's own and new sections
        /// only need their ingredients' new costs. Every saved one is fetched: a role made product-owned, or no longer, in
        /// Settings turns a linked one into the product's own, or back.
        /// </summary>
        public IReadOnlyList<int> LinkedIds { get; init; } = [];

        /// <summary>
        /// A composite's products, which can have changed elsewhere since they were read (a recipe, an ingredient's price, a
        /// quantity required), so what each brings is read again; its lines may hold unsaved edits and stay as they are.
        /// </summary>
        public IReadOnlyList<int> ComponentIds { get; init; } = [];

        /// <summary>The packaging link as saved, while the form still shows it as saved; null otherwise.</summary>
        public PackagingLinkTarget? SavedLink { get; set; }

        /// <summary>The linked packaging's lines as they're saved now, read again while the form shows the link as saved.</summary>
        public IReadOnlyList<PackagingLine>? LinkedPackagingLines { get; set; }

        /// <summary>A role made product-owned, or no longer, since the product was loaded, with nothing typed: it's read again.</summary>
        public bool ReloadDetails { get; set; }

        /// <summary>The product showing, saved since the form took it, with nothing typed: it's read into the form again.</summary>
        public bool SavedSince { get; set; }

        /// <summary>The form held nothing to save as the load set out, so whatever the load brings leaves it so.</summary>
        public bool WasClean { get; set; }

        // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
        public int? SelectedId { get; set; }

        public int? CategoryId { get; set; }

        public int? UsageRoleId { get; set; }

        public int? UsageSourceKey { get; set; }
    }

    /// <summary>The view's load sets out: what the form needs read from elsewhere.</summary>
    internal Load BeginLoad() => new()
    {
        DetailProductId = _detailProductId,
        LinkedIds = [.. Recipe.UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId).Distinct()],
        ComponentIds = [.. ComponentsTable.Draft.ProductIds],
    };

    /// <summary>
    /// The lists are read: the product whose linked packaging the view reads again for the form, if any. Linked packaging is
    /// kept elsewhere (another product, or a set on the Sub-Assemblies screen) and can have changed there, so it's fetched
    /// again, but only while the form still shows the link as saved. The product's own lines aren't, since they may hold
    /// unsaved edits, and nor is a link picked but not saved.
    /// </summary>
    internal int? LinkedPackagingToRead(Load load, ProductsLoad read)
    {
        if (load.DetailProductId is not { } productId || !read.PackagingLinks.TryGetValue(productId, out var packagingLink))
        {
            return null;
        }

        var savedLink = PackagingLinkTarget.From(packagingLink);
        if (!Packaging.Draft.ShowsSavedLink(savedLink))
        {
            return null;
        }

        load.SavedLink = savedLink;
        return packagingLink.ProductId;
    }

    /// <summary>Just before the merge: what the form shows, by id, and whether its product is read again after.</summary>
    internal void BeforeMerge(Load load, ProductsLoad read)
    {
        // A role made product-owned in Settings, or no longer, since the product was loaded: a section it linked is its own
        // now, or one of its own is linked now. With nothing typed into the form, the product is read again, so what Save
        // compares against is how it's saved now; with something typed, RefreshRows turns round in place a section with none
        // of it, which the next Save writes as it shows, and leaves one changed here as it is, which Save refuses (ADR 0007).
        var detailProductId = load.DetailProductId;
        var ownershipChanged = Recipe.UsageEdits.Any(u => read.LinkedSubAssemblies.FirstOrDefault(s => s.Id == u.SubAssemblyId) is { } saved
            && (saved.OwnerProductId is not null && saved.OwnerProductId == detailProductId) != u.IsOwned);
        load.WasClean = !History.IsDirty;
        load.ReloadDetails = ownershipChanged && load.WasClean && detailProductId is not null;

        load.SelectedId = SelectedProduct?.Id;
        load.CategoryId = EditingCategory?.Id;
        load.UsageRoleId = UsageForm.EditingUsageRole?.Id;
        load.UsageSourceKey = UsageForm.EditingUsageSource?.Key;
    }

    /// <summary>
    /// The view's load merges into the form, its lists merged already: the pickers, the recipe, the packaging and a
    /// composite's lines pointed at their fresh copies, the form and what's typed left as they are. Run within the form's
    /// own refresh, so nothing it writes is the user's.
    /// </summary>
    internal void TakeLoad(Load load, ProductsLoad read)
    {
        Packaging.TakePackagingSets(read.PackagingSets);
        RefreshCostingDefaults(read.CostingDefaults);

        // The category back on its fresh copy, and a composite's lines at the fresh products (a rename shows) at their fresh
        // recipe costs (ProductDraft.Rebase). Only a composite's Products table offers products: any other's are built if it
        // becomes one (StartNew, a pick).
        Draft.Rebase(load.CategoryId, Categories, read.ComponentCosts, read.ComponentLinks);
        if (IsEditingComposite)
        {
            RefreshComponentOptions();
        }

        SelectedProduct = Products.FirstOrDefault(p => p.Id == load.SelectedId);
        if (SelectedProduct is not null)
        {
            ShowUpdatedAt(SelectedProduct.UpdatedAt);
        }

        // Every load, so a list's own save is followed by the load it asked for and no later one.
        load.SavedSince = FollowSavedVersion(
            SelectedProduct is { } showing && showing.Id == EditingId && EditingId == load.DetailProductId ? showing.UpdatedAt : SavedVersion);

        // What's linked as it's kept now (ProductDraft.RebaseLinked): the linked packaging, read again for this product only
        // while it showed the link as saved, and the recipe's sections.
        var (linkedSectionsChanged, linkedPackagingNow) = Draft.RebaseLinked(
            load.DetailProductId == _detailProductId && load is { SavedLink: { } savedLink, LinkedPackagingLines: { } lines } ? (savedLink, lines) : null,
            read.LinkedSubAssemblies, read.LinkedLines, Recipe.CaptureForm());

        // What came in from where it's kept is put into every undo and redo step too, as an outside save is (RewriteAll):
        // an undo of something typed before the load would otherwise put back a linked sub-assembly's lines, or a linked
        // packaging's, as they were before they were saved on the Sub-Assemblies screen.
        if (linkedSectionsChanged || linkedPackagingNow is not null)
        {
            History.RewriteAll(snapshot => snapshot with
            {
                Recipe = linkedSectionsChanged
                    ? Recipe.Draft.StepAsSavedNow(snapshot.Recipe, read.LinkedSubAssemblies, read.LinkedLines)
                    : snapshot.Recipe,
                Packaging = linkedPackagingNow is { } now && snapshot.Packaging.Link?.Key == now.Link?.Key && !snapshot.Packaging.IsMoving
                    ? snapshot.Packaging with { Link = now.Link, Lines = now.Lines }
                    : snapshot.Packaging,
            });
        }

        // The packaging lines at their materials' refreshed costs and units, in place, so a line being typed isn't lost.
        Packaging.Draft.ResolveRows();
        UsageForm.ResolveRows();

        // The section form's choices, built again only if the view's lists they come from changed.
        Recipe.RefreshLinkableOptionsAfterLoad();

        // An add form opened before the roles were loaded takes the first role it can have, as it would have.
        UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == load.UsageRoleId)
            ?? (load.UsageRoleId is null && UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage ? UsageForm.FormRoleOptions.FirstOrDefault() : null);
        UsageForm.EditingUsageSource = UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == load.UsageSourceKey);

        // Its rows raise their percentages only when the base moved (IngredientLineRow), so a load that changed nothing in
        // the form costs little here.
        UsageForm.Recompute();
        InvalidateCosts();
    }

    /// <summary>The view's load is merged: what follows it on the form.</summary>
    internal async Task EndLoadAsync(Load load)
    {
        // What the load brought from where it's kept (a linked section's lines and name, a linked packaging's lines, the name
        // of what it links) is no edit: a form with nothing to save as the load set out has nothing to save after it, or a
        // set or sub-assembly saved on the Sub-Assemblies screen had each product showing it offer Save, and closing the
        // window ask about it. The merge never touches what's typed, so a form with something typed keeps it to save.
        if (load.WasClean && History.IsDirty)
        {
            History.MarkClean();
        }

        if ((load.ReloadDetails || load.SavedSince) && SelectedProduct is { } reloaded && reloaded.Id == load.DetailProductId)
        {
            if (load.SavedSince)
            {
                History.Clear();
                LoadIntoForm(reloaded);
            }

            await LoadProductDetailAsync(reloaded, CancellationToken.None);
        }

        // After the merge, which only points the selection at its fresh copy: a product asked for before the list held it
        // has to load its form.
        OpenRecordAskedFor();

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

        EditingId = product.Id;
        SavedVersion = product.UpdatedAt;
        Draft.ShowFields(product, Categories);
        ShowUpdatedAt(product.UpdatedAt);
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
    /// A new product named after <paramref name="subAssembly"/>, with the Link form open on it, as Add Sub-Assembly's Link an
    /// existing one would have it: its lines load, and the piece size is left to type. Like New, it has nothing to undo; it's
    /// measured against a blank product, so Save is offered straight away. Sub-Assemblies' New Product, through the view.
    /// </summary>
    internal void StartNewProductLinking(SubAssembly subAssembly)
    {
        ForgetRecordToOpen();
        StartNewProduct();
        EditingName = subAssembly.Name;
        Recipe.OpenAddSubAssemblyTab();
        UsageForm.PickMode(UsageFormMode.Link);
        UsageForm.PickSource(UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.SubAssembly?.Id == subAssembly.Id));
        History.RefreshDirty();
    }

    protected override int? FindRecordByName(string text) => RecordNameMatch.Best(Products, product => product.Name, text)?.Id;

    /// <summary>Any product but the one showing, and not a deleted one: that's only picked in the Deleted folder, and replaces nothing.</summary>
    protected override bool ReplacesShowing(int id) => id != SelectedProduct?.Id && DeletedProducts.All(p => p.Id != id);

    protected override bool TrySelectRecord(int id)
    {
        // A deleted one (a Dependents tile names it as deleted) has its row picked in the Deleted folder, opened; the form stays.
        if (_view.ShowDeleted(id))
        {
            return true;
        }

        if (Products.FirstOrDefault(p => p.Id == id) is not { } product)
        {
            return false;
        }

        var list = _view.List;
        if (!list.VisibleProducts.Contains(product))
        {
            list.ClearListFilters();
        }

        Pick(product);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: the view opens it, in its tab or the preview tab.</summary>
    bool IRecordScreen.OpenByName(string text) => _view.OpenByName(text);

    /// <summary>
    /// New, from the list or the empty view, in this form. A product still waiting for the load to open it no longer is: it
    /// would load over the new form, and a name typed into it since would then be saved as that product's.
    /// </summary>
    internal void StartNew(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        ForgetRecordToOpen();
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
        Pick(null);
        FormVersion++;
        EditingId = 0;
        SavedVersion = null;
        Draft.ClearFields();
        ShowUpdatedAt(null);

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
    private void RefreshComponentOptions() => ComponentsTable.Draft.RefreshOptions(_detailProductId ?? 0, Products);

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
        ComponentsTable.Draft.Clear();
        Costing.Load(null);
        Recipe.RefreshLinkableOptions();
        InvalidateCosts();

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
        // after each read to start the next. The reads touch no screen state; everything below them does.
        // Save's reload has read it already: then this runs through without awaiting, in the pass that
        // picked the product, so the form isn't drawn closed (ResetDetails) before it opens again.
        ProductDetail detail;
        try
        {
            detail = _preloadedDetail is { } preloaded && preloaded.ProductId == product.Id
                ? preloaded.Detail
                : await Task.Run(() => _queries.ReadDetailAsync(product.Id, cancellationToken), cancellationToken);
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
        // settles what Save compares it against, which they're part of; the packaging before the recipe's tabs, rebuilt
        // next, so they can reselect the Packaging tab if it showed (ProductDraft.ShowParts).
        SetComposite(product.IsComposite);
        Draft.ShowParts(detail, product.BulkPackagingQuantity);
        Recipe.ShowSaved(detail.Usages, detail.SubAssemblyLines, detail.AddIns);
        Costing.Load(detail.Costing);
        Recipe.RefreshLinkableOptions();
        RefreshComponentOptions();
        InvalidateCosts();
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

        // Filed where New put it (only a new product's place is written), as last saved when the form took it: a save
        // since, from another PC, refuses this one (SavedVersionRules).
        var product = Draft.ToProduct(_editingId, NewProductPlace.Stage, NewProductPlace.FolderId, SavedVersionToSend);

        // Refused before anything's asked: the product's fields, then its costing's percentages.
        _editor.EnsureValid(product);
        var costing = Costing.ToParameters();
        _editor.EnsureValid(costing);

        var recipe = Recipe.Draft.CurrentDraft();
        if (!await ConfirmSharedLinesAsync())
        {
            return;
        }

        // Written with its versions, all or nothing (VersionedSaves): a saved product with none yet first gets how it
        // stood until now, so there's one to go back to; then the product as saved; and the products sharing its
        // sections' lines or linking its packaging each get one if this Save changes them. Only linked lines edited
        // here and the packaging reach those, so a Save of neither reads none: a price-only Save would
        // otherwise spend half a second reading every product sharing its dough, twice.
        var packagingLink = Packaging.Draft.ChangedLink();
        var packagingLines = Packaging.Draft.ChangedLines();
        var bulkToSave = Packaging.Draft.ChangedBulk();
        var changesSharing = Recipe.UsageEdits.Any(u => u.AreLinkedLinesEdited) || packagingLines is not null || packagingLink is not null;
        var request = new ProductSaveRequest(
            product, costing, Costing.IsChanged, Recipe.Draft.IsAsSaved(recipe) ? null : recipe, packagingLink, packagingLines, bulkToSave, changesSharing);
        var saved = await Task.Run(() => _editor.SaveAsync(request, DateTime.Now, CancellationToken.None));

        // What's saved is the new starting point: stepping back past it would show changes already written.
        // Save's own reload keeps the product selected, so ClearDetails doesn't run and wouldn't clear this.
        History.Clear();

        // Saved at this time now, as read back: the reload below finds it so, and doesn't read the form again for it.
        SavedVersion = saved.Product.UpdatedAt;

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

        var users = await _queries.ReadSectionUsersAsync(CancellationToken.None);
        var shared = edited
            .Select(u => (u.LinkedName, Others: (IReadOnlyList<string>)
            [
                .. OtherProductsUsing(u.SubAssemblyId, users.LinkedBy),
                .. (users.MadeFrom.GetValueOrDefault(u.SubAssemblyId) ?? []).Select(m => m.Name),
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
        _view.TakeSaved(saved);
        _detailProductId = saved.Id;
        _preloadedDetail = (saved.Id, detail);
        try
        {
            Pick(saved);
        }
        finally
        {
            _preloadedDetail = null;
        }
    }

    /// <summary>A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.</summary>
    private void PackagingLinesEdited()
    {
        Packaging.Draft.ResolveRows();
        Packaging.Draft.RaiseCostsChanged();
        InvalidateCosts();
    }

    /// <summary>
    /// A composite's line picked, typed or deleted in its Products table: it's priced once its product's recipe cost has
    /// been read (a new pick's is read then), and the composite's costs follow.
    /// </summary>
    private void ComponentsEdited()
    {
        ComponentsTable.Draft.ResolveRows();
        ComponentsTable.ReadMissingCosts();
        InvalidateCosts();
    }

    /// <summary>
    /// Settings' costing defaults were applied to the product the form shows, saved by the view: it reads its new costing,
    /// as after its own Save.
    /// </summary>
    internal async Task ShowAppliedCostingAsync()
    {
        if (_detailProductId is { } openId
            && await Task.Run(() => _queries.ReadAsync(openId, CancellationToken.None)) is { } open)
        {
            var detail = await Task.Run(() => _queries.ReadDetailAsync(openId, CancellationToken.None));
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
    }

    /// <summary>
    /// Takes up the defaults as LoadCommand read them. A new product still showing the ones it started with (the screen's
    /// first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed into keeps it.
    /// A new product started by New goes in the new stage whatever was typed, since its stage was never picked.
    /// </summary>
    private void RefreshCostingDefaults(CostingDefaults defaults)
    {
        // Every product is priced with the shop's wastage, VAT and price rounding, the one open included: its Costing tab
        // follows them; and a new product still showing the defaults it started with takes the new ones (TakeDefaults).
        NewProductPlace.TakeDefault(defaults.NewProductStage, formIsNew: EditingId == 0);
        if (Costing.TakeDefaults(defaults, isNewProduct: EditingId == 0))
        {
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
        Packaging.Draft.Reprice();
        ComponentsTable.Draft.Reprice();

        InvalidateCosts();
        UsageForm.Recompute();
        VersionHistory.Reprice(Costing.CostBasis);
    }

    /// <summary>
    /// Something the costs are worked out from changed (the recipe, the packaging, a composite's products, the product's
    /// price, quantity or category): the Costing tab and the header work them out again as they're next read.
    /// </summary>
    private void InvalidateCosts() => Costing.InputsChanged();

    /// <summary>
    /// The product the screen has open, as the Print dialog shows it: asked for each time the dialog opens, so it follows
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
    /// packaging and its costing, with a note when that isn't what's saved (ProductDraft.ToPrintout).
    /// </summary>
    private ProductPrintout CurrentPrintout() => Draft.ToPrintout(
        ProductRules.StageTitle(SelectedProduct?.Stage ?? NewProductPlace.Stage),
        LastUpdatedText,
        _detailProductId is null ? ProductPrintout.NotSavedNote : History.IsDirty ? ProductPrintout.UnsavedChangesNote : null);

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
        try
        {
            _categoryEditor.EnsureValid(name);
        }
        catch (BusinessRuleException refused)
        {
            ErrorMessage = refused.Message;
            return null;
        }

        // Saved with its id, which the copy read back is found by.
        var category = await _categoryEditor.AddAsync(name, CancellationToken.None);
        await ReloadCategoriesAsync();
        return Categories.FirstOrDefault(c => c.Id == category.Id);
    }

    /// <summary>
    /// Reads the categories again, into the view and every form's Category picker, each kept on the one it holds (the
    /// view's TakeCategories).
    /// </summary>
    private async Task ReloadCategoriesAsync()
    {
        var categories = await _queries.ReadCategoriesAsync(CancellationToken.None);
        _view.TakeCategories(categories);
    }

    /// <summary>The Category picker back on <paramref name="categoryId"/>'s fresh copy, after the view merged the categories.</summary>
    internal void TakeCategory(int? categoryId) => EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);

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

    protected override Task ReadListsAgainAsync() => LoadCommand.ExecuteAsync();

    protected override int ShowingRecordId => EditingId;

    /// <summary>
    /// Puts the form back as the product was last saved: Revert, and Don't Save on closing its tab with the question's
    /// revert. It reads the picked product's detail again, which replaces the recipe, packaging, packs and costing
    /// wholesale, so anything staged but unsaved goes: a section added or edited in the form included, since the form has
    /// no discard of its own. A new product was never saved, so it goes back to being a blank one.
    /// </summary>
    protected override async Task RevertToSavedAsync()
    {
        if (SelectedProduct is not { } product)
        {
            // A new composite named in the list keeps its row until it's saved: put back blank, it has none.
            if (_view.Form == this)
            {
                _view.List.EndNewRow();
            }

            StartNewProduct(NewProductPlace.ChosenStage, NewProductPlace.FolderId, IsEditingComposite);
            return;
        }

        // As it's saved now, not as the list last read it: another PC may have saved it since, which is what Revert shows after
        // a Save refused for it (SavedVersionRules), and whose time the form then takes, so its next Save goes through.
        if (await Task.Run(() => _queries.ReadAsync(product.Id, CancellationToken.None)) is { } saved && saved.UpdatedAt != product.UpdatedAt)
        {
            ApplyRefresh(() =>
            {
                _view.TakeSaved(saved);
                SelectedProduct = Products.FirstOrDefault(p => p.Id == saved.Id);
            });
            product = saved;
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
    protected override ProductFormSnapshot CaptureSnapshot() => Draft.Capture(EditingId, Recipe.Capture());

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

            // 2 to 4. The product (ProductDraft.Restore): its own fields, the category by id as the reloaded list's own copy,
            //    the costing, the packaging with its link as saved left alone (what Save compares against), and a
            //    composite's lines.
            EditingId = snapshot.EditingId;
            Draft.Restore(snapshot, Categories);

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
        InvalidateCosts();

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

    /// <summary>
    /// The name typed into a new product's row: the form, as New left it, is saved under it, as Save saves it, with its
    /// first version, and opened, and the row gives way to the product's own (ProductsList.EndNewRow, as it opens). Refused
    /// (a name taken), the row stays, and why shows.
    /// </summary>
    internal async Task SaveNewAsync(string name)
    {
        // Written, not typed: there's no undo step for it, and nothing unsaved to ask about if the save is refused.
        EditingName = name;
        await RunSafelyAsync(SaveAsync);
        if (EditingId == 0 && ErrorMessage is null)
        {
            // Save turned it down without a reason of its own (the details weren't ready yet).
            ErrorMessage = $"{name} couldn't be saved yet. Press Enter to try again.";
        }
    }

    /// <summary>The name typed into a new composite's row in the list goes into the form, as typed there: one undo step.</summary>
    internal void NameNew(string name) => AsUndoStep(() => EditingName = name);

    /// <summary>The new product's row went with Esc: the name the form was given goes too, as no edit of the user's.</summary>
    internal void ClearNewName() => EditingName = string.Empty;

    /// <summary><paramref name="product"/>, as it was, renamed in the list, saved: the form and its undo steps take the name as given.</summary>
    internal void TakeRename(Product product, string name)
    {
        if (EditingId != product.Id)
        {
            return;
        }

        // The Clone tab's name follows too, while it's still the one it started with.
        var oldCloneName = $"{product.Name} Copy";
        var newCloneName = $"{name} Copy";
        EditingName = name;
        if (EditingCloneName == oldCloneName)
        {
            EditingCloneName = newCloneName;
        }
        History.RewriteAll(snapshot => snapshot with
        {
            Name = name,
            CloneName = snapshot.CloneName == oldCloneName ? newCloneName : snapshot.CloneName,
        });
    }

    /// <summary>
    /// The products a move in the list took, read again, within the form's refresh: the form's product is re-pointed at its
    /// fresh copy. The window's own save, checked as it was written, with no load after it: the form takes the new time and
    /// what's typed stays. With nothing typed, the list loads the view, which puts what's saved on the form (FollowSavedVersion).
    /// </summary>
    internal void ShowMoved(IReadOnlyList<Product> moved)
    {
        if (SelectedProduct is { } selected && moved.FirstOrDefault(p => p.Id == selected.Id) is { } fresh)
        {
            SelectedProduct = fresh;
        }

        if (moved.FirstOrDefault(p => p.Id == EditingId) is { } onForm)
        {
            if (History.IsDirty)
            {
                SavedVersion = onForm.UpdatedAt;
            }

            ShowUpdatedAt(onForm.UpdatedAt);
        }
    }

    // What the recipe asks of the screen (IProductRecipeHost): what can be linked, the product showing and its costs.
    IReadOnlyList<SubAssembly> IProductRecipeHost.Linkable => _view.Linkable;

    int IProductRecipeHost.LinkableVersion => _view.LinkableVersion;

    int? IProductRecipeDraftHost.DetailProductId => _detailProductId;

    int? IProductDraftHost.DetailProductId => _detailProductId;

    void IProductRecipeHost.InvalidateCosts() => InvalidateCosts();

    void IProductRecipeHost.OpenProduct(int productId) => _view.OpenRecord(productId);

    void IProductRecipeHost.OpenSubAssembly(int subAssemblyId) => _subAssemblyOpenRequests.OnNext(subAssemblyId);

    // What the Packaging tab asks of the screen (IProductPackagingHost).

    IEnumerable<Product> IProductPackagingHost.OtherProducts => Products.Where(p => p.Id != _detailProductId);

    void IProductPackagingHost.InvalidateCosts() => InvalidateCosts();

    // What the section form asks of the screen (IProductUsageFormHost); its cost basis is the Costing part's.
    CostBasis IProductUsageFormHost.CostBasis => Costing.CostBasis;

    IReadOnlyList<SubAssembly> IProductUsageFormHost.Linkable => _view.Linkable;

    IEnumerable<int> IProductUsageFormHost.SectionSubAssemblyIds => Recipe.UsageEdits.Select(u => u.SubAssemblyId);

    // What the History tab asks of the screen (IProductVersionHistoryHost): the product showing, the costs it prices at, and
    // the form a restored version goes into. Its Packaging tab is the screen's own.
    Product? IProductVersionHistoryHost.ProductShowing => AreDetailsReady ? SelectedProduct : null;

    CostBasis IProductVersionHistoryHost.CostBasis => Costing.CostBasis;

    IReadOnlySet<int> IProductVersionHistoryHost.OwnedSectionIds => Recipe.Draft.SavedOwnSectionIds;

    string? IProductVersionHistoryHost.SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) => Recipe.Draft.SectionLabelOf(subAssemblyName, role);

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
        _subAssemblyOpenRequests.Dispose();
        _printRequests.Dispose();
        _versionReads.Dispose();
        _picks.Dispose();
        UsageForm.Dispose();
        Packaging.Dispose();
        ComponentsTable.Dispose();
        Draft.Dispose();
    }
}
