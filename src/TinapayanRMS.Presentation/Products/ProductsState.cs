using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// The Products view: every product in its stage and the user's folders (<see cref="List"/>) beside a strip of tabs, each a
/// product's form of its own (<see cref="ProductForm"/>, through <see cref="RecordViewState{TForm}.Tabs"/>), so several can
/// be open at once, each with its edits and its undo. A product picked in the list opens in the preview tab, which the next
/// pick replaces while it holds nothing unsaved; New, New composite product and a product started from a sub-assembly start
/// one there too, and a clone or a variant opens in a tab of its own. The view keeps what every form reads (the products,
/// the deleted ones, the folders, the categories, the roles and materials, what can be linked, the packaging sets and
/// Settings' costing defaults) and the load that brings them, which keeps each form, and what's typed into it, as it is; and
/// the list's actions on products as records (delete, restore, clone, the costing defaults, print). It opens on a blank new
/// product in the preview tab, as the screen always has.
/// </summary>
public sealed class ProductsState : RecordViewState<ProductForm>, IProductsListHost
{
    /// <summary>How a clone's first version names where it came from: "Cloned from Pandesal, version 4".</summary>
    public const string ClonedFrom = "Cloned from";

    /// <summary>How a variant's first version names where it came from: "Variant of Pandesal, version 4".</summary>
    public const string VariantOf = "Variant of";

    private readonly IProductRepository _productRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IRawMaterialCategoryRepository _rawMaterialCategoryRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IProductVersionRepository _versionRepository;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly IValidator<ProductCategory> _categoryValidator;
    private readonly ILogger _logger;

    // Reads the products opened here, as Reports and Print read theirs: every form's.
    private readonly SavedProductReader _savedProducts;

    // What a load reads, apart from its merge into the view and the forms (LoadAsync).
    private readonly ProductsLoadReader _loadReader;

    // Save, Clone, Create Variant and the costing defaults, each written with the versions it records: every form's.
    private readonly VersionedSaves _versionedSaves;

    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categories;

    /// <summary>The products each load brings, whose markups and sections the list's flags are worked out from.</summary>
    private readonly Subject<IReadOnlyList<Product>> _flagChecks = new();

    // What the forms and the list ask of other screens, passed on from whichever asks (Wire).
    private readonly Subject<PrintScope> _printRequests = new();
    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>
    /// A sub-assembly <see cref="StartNewProductFrom"/> was asked to link, for the load to start the new product with once
    /// its lists are fresh. New drops it.
    /// </summary>
    private int? _subAssemblyToStartFrom;

    public ProductsState(
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
        _productRepository = productRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _productCloner = productCloner;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _rawMaterialCategoryRepository = rawMaterialCategoryRepository;
        _categoryRepository = categoryRepository;
        _versionRepository = versionRepository;
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;
        _categoryValidator = categoryValidator;
        _logger = logger;
        _savedProducts = new SavedProductReader(
            packagingLinkRepository, usageRepository, packagingLineRepository, costingParametersRepository, subAssemblyLineRepository, productRepository,
            costingDefaultsRepository, unitOfWork);
        _loadReader = new ProductsLoadReader(
            productRepository, subAssemblyRoleRepository, categoryRepository, folderRepository, rawMaterialRepository, subAssemblyRepository,
            subAssemblyLineRepository, costingDefaultsRepository, packagingLinkRepository, packagingLineRepository, _savedProducts);
        _versionedSaves = new VersionedSaves(
            unitOfWork,
            productRepository,
            versionRepository,
            costingParametersRepository,
            _savedProducts,
            new ProductVersionRecorder(_savedProducts, productRepository, versionRepository));

        // The lists the forms and the list read, bound first: each load merges into them rather than replacing them. The
        // categories before the list's own subscription to them, so the category filter's choices read them changed.
        _productCache.Connect()
            .SortAndBind(out _products, SortExpressionComparer<Product>.Ascending(p => p.Name))
            .Subscribe();
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe();

        List = new ProductsList(this, _productCache, _categoryCache, Categories, productRepository, folderRepository, viewStateStore);

        // The list's flags for a price short of its target markup and a section with no piece size, after each load: a newer
        // load cancels one still working. Each runs as the form showing does, as the load that asks for it did (Runner), so
        // its work counts with that form's, and a failure shows there.
        LoadLatest(_flagChecks, FlagProductsAsync, () => Runner);

        // Runs each time the page opens (ScreenBase) and after the forms' own saves. It merges rather than replaces, so each
        // form keeps its selection, pickers and unsaved edits while picking up what changed elsewhere: a repriced
        // ingredient, or a linked sub-assembly its owner changed. It runs as the form showing does (Runner).
        LoadCommand = new ScreenCommand(() => Runner.RunLoadAsync(LoadAsync));

        // Through the list, which adds the new product's row for its name to be typed into (NewInList), and starts it
        // through the view (StartNew).
        NewCommand = ScreenCommand.Of(() => List.NewInList());
        NewCompositeCommand = ScreenCommand.Of(() => List.NewInList(isComposite: true));

        // The view opens on a blank new product, as the screen always has: the first one picked takes its place.
        Show(AddTab(isPreview: true));
    }

    /// <summary>The list: every product in its stage and the user's folders, searched, filtered, its menus, moves and folders.</summary>
    public ProductsList List { get; }

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
    /// moved to the other since (ProductForm.TakeFolders), as Save would file it there.
    /// </summary>
    public IReadOnlyList<ProductFolder> Folders
    {
        get => _folders;
        private set
        {
            foreach (var tab in Tabs.All)
            {
                tab.Form.TakeFolders(value);
            }

            RaiseAndSetIfChanged(ref _folders, value);
        }
    }

    /// <summary>What the forms' Category pickers list.</summary>
    public ReadOnlyObservableCollection<ProductCategory> Categories => _categories;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only, for a sub-assembly's lines: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>
    /// Every sub-assembly that could be linked or copied, as last loaded; a section form's LinkableSubAssemblies filters it.
    /// A load that reads the same keeps the list it had, so the forms' choices built from it stay (<see cref="LinkableVersion"/>).
    /// </summary>
    internal IReadOnlyList<SubAssembly> Linkable { get; private set; } = [];

    /// <summary>
    /// Counts the loads that changed <see cref="Linkable"/> or the roles, which each form's section choices are built from:
    /// a form builds them again only when this has moved (ProductRecipe.RefreshLinkableOptionsAfterLoad), not on every
    /// load for every tab.
    /// </summary>
    internal int LinkableVersion { get; private set; }

    /// <summary>The packaging sets, as last loaded, which a form's Packaging tab links.</summary>
    internal IReadOnlyList<SubAssembly> PackagingSets { get; private set; } = [];

    /// <summary>Settings' costing defaults, as last loaded; null until the first load.</summary>
    internal CostingDefaults? CostingDefaults { get; private set; }

    /// <summary>Reads everything the list and the forms show again, and merges it into the list and every open form.</summary>
    public override ScreenCommand LoadCommand { get; }

    /// <summary>
    /// New, from a form's header, the list or the Vim keys' <c>:new</c>: a blank new product in the preview tab, so a tab
    /// holding changes is left as it is, and one holding none is used for it.
    /// </summary>
    public override ScreenCommand NewCommand { get; }

    /// <summary>New's New composite product: a new product made of other products, in the preview tab.</summary>
    public ScreenCommand NewCompositeCommand { get; }

    /// <summary>
    /// What a Print asks to print: a form's header, This product (the form showing); a folder's menu, the folder; the list's,
    /// all products. ScreenLinks opens the Print dialog with that choice (PrintDialogState.Pick).
    /// </summary>
    public IObservable<PrintScope> PrintRequests => _printRequests;

    /// <summary>The sub-assemblies a form's linked badges ask to open, by id. ScreenLinks opens each on Sub-Assemblies.</summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    protected override RecordList ViewList => List;

    protected override ProductForm NewForm() => new(
        this, _productRepository, _packagingLineRepository, _subAssemblyRepository, _subAssemblyLineRepository, _rawMaterialCategoryRepository,
        _categoryRepository, _versionRepository, _savedProducts, _versionedSaves, _productValidator, _costingParametersValidator, _categoryValidator,
        _logger);

    protected override IDisposable Wire(ProductForm form) => new CompositeDisposable(
        form.PrintRequests.Subscribe(_printRequests.OnNext),
        form.SubAssemblyOpenRequests.Subscribe(_subAssemblyOpenRequests.OnNext));

    /// <summary>
    /// Starts a new product that links a standalone sub-assembly, for Sub-Assemblies' New Product. It waits for the next
    /// load, which showing the view runs, so the sub-assembly is found as it's saved now: it may have been made or renamed
    /// there since this view last loaded. It starts in the preview tab, as New does.
    /// </summary>
    public void StartNewProductFrom(int subAssemblyId)
    {
        RecordToOpen = null;
        _subAssemblyToStartFrom = subAssemblyId;
    }

    /// <summary>
    /// Flags each product in the list whose current price earns less than its target markup, and each with a recipe section
    /// that has no piece size: every product priced as saved, as Reports prices them (SavedProductReader.ReadAllAsync, a
    /// dozen or so queries however many there are), off the UI thread. A product's flags follow its Save at the next load.
    /// </summary>
    private async Task FlagProductsAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        var saved = await Task.Run(() => _savedProducts.ReadAllAsync(products, CostBasis.Buffered, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var below = saved.Where(s => s.Costs.IsBelowTargetMarkup).Select(s => s.Product.Id).ToHashSet();
        var unsized = saved.Where(s => s.SectionsWithoutPieceSize.Count > 0).Select(s => s.Product.Id).ToHashSet();
        foreach (var product in products)
        {
            if (List.ProductTree.Find(product.Id) is { } leaf)
            {
                leaf.IsBelowTargetMarkup = below.Contains(product.Id);
                leaf.HasSectionWithoutPieceSize = unsized.Contains(product.Id);
            }
        }
    }

    /// <summary>Everything the list and the forms show read again and merged, every form and what's typed into it kept.</summary>
    private async Task LoadAsync()
    {
        // What each form needs read from elsewhere: its linked sections, its packaging link, a composite's products.
        var loads = Tabs.All.Select(tab => (tab.Form, Load: tab.Form.BeginLoad())).ToList();

        // Everything at once, on the thread pool (ProductsLoadReader), touching no screen state.
        var read = await _loadReader.ReadAsync(
            [.. loads.Select(l => l.Load.DetailProductId).OfType<int>().Distinct()],
            [.. loads.SelectMany(l => l.Load.LinkedIds).Distinct()],
            [.. loads.SelectMany(l => l.Load.ComponentIds).Distinct()]);

        // A form whose tab closed meanwhile takes nothing; one opened meanwhile read its own as it opened.
        loads = [.. loads.Where(l => Tabs.All.Any(tab => tab.Form == l.Form))];

        // Each form's linked packaging, read again while it shows the link as saved, all at once.
        var packagingReads = loads
            .Select(l => (l.Load, ProductId: l.Form.LinkedPackagingToRead(l.Load, read)))
            .Where(r => r.ProductId is not null)
            .Select(r => (r.Load, Lines: Task.Run(() => _loadReader.ReadLinkedPackagingAsync(r.ProductId!.Value))))
            .ToList();
        await Task.WhenAll(packagingReads.Select(r => r.Lines));
        foreach (var (load, lines) in packagingReads)
        {
            load.LinkedPackagingLines = await lines;
        }

        foreach (var (form, load) in loads)
        {
            form.BeforeMerge(load, read);
        }

        // What the forms' section choices are built from, compared once here before the roles are merged, rather than by
        // every form as it builds its choices.
        var linkableChanged = !HaveSameValues(Linkable, read.Linkable) || !HaveSameValues(Roles, read.Roles);

        // Within every form's refresh at once, so nothing the merge writes, into the lists or the forms, is taken for an edit.
        var forms = loads.Select(l => l.Form).ToList();
        RefreshingEach(forms, () =>
        {
            ApplyRefresh(() =>
            {
                Roles.MergeFrom(read.Roles, r => r.Id);
                _categoryCache.MergeFrom(read.Categories);
                Ingredients.MergeFrom([.. read.RawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. read.RawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);

                // The Deleted folder and the folders first, so the tree, refreshed as the products merge, files a product
                // deleted, restored or moved since in its new place in one go. The tree is refreshed again for a change to
                // those alone.
                DeletedProducts = read.DeletedProducts;
                Folders = read.Folders;
                _productCache.MergeFrom(read.Products);
                List.RefreshTree();
                _flagChecks.OnNext(read.Products);
                if (linkableChanged)
                {
                    Linkable = read.Linkable;
                    LinkableVersion++;
                }

                PackagingSets = read.PackagingSets;
                CostingDefaults = read.CostingDefaults;
            });

            foreach (var (form, load) in loads)
            {
                form.TakeLoad(load, read);
            }
        });

        foreach (var (form, load) in loads)
        {
            await form.EndLoadAsync(load);
        }

        OpenRecordWaiting();

        if (_subAssemblyToStartFrom is { } subAssemblyId)
        {
            _subAssemblyToStartFrom = null;
            StartNewProductLinking(subAssemblyId);
        }
    }

    /// <summary>Whether a list read again holds the same rows, in the same order, with the same values (CollectionMerge.HasSameValues).</summary>
    private static bool HaveSameValues<T>(IReadOnlyList<T> current, IReadOnlyList<T> fresh)
    {
        if (current.Count != fresh.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!CollectionMerge.HasSameValues(current[i], fresh[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A form's own Save, or its Revert, read its product back: the list takes the fresh copy at once.</summary>
    internal void TakeSaved(Product saved) => _productCache.AddOrUpdate(saved);

    /// <summary>
    /// The categories read again (a new one added from a form's Category picker): merged in, every form's picker kept on the
    /// one it holds, within each form's refresh, since a picker whose item gets replaced can clear its own selection.
    /// </summary>
    internal void TakeCategories(IReadOnlyList<ProductCategory> categories)
    {
        var forms = Tabs.All.Select(tab => tab.Form).ToList();
        var held = forms.Select(form => form.EditingCategory?.Id).ToList();
        RefreshingEach(forms, () =>
        {
            ApplyRefresh(() => _categoryCache.MergeFrom(categories));
            for (var i = 0; i < forms.Count; i++)
            {
                forms[i].TakeCategory(held[i]);
            }
        });
    }

    /// <summary>A product picked in the list: its tab, or the preview tab.</summary>
    private void OpenFromList(Product product, bool keep = false) => OpenInTab(product.Id, keep, form => form.Pick(product));

    /// <summary>
    /// IRecordOpener: the product in the view's address (Back, Forward, a link from another screen, Find), shown as a pick in
    /// the list shows it, a search or filter hiding its row cleared. A deleted one has its row picked in the Deleted folder,
    /// opened, and opens no tab. A list that doesn't hold it yet opens it at the end of the next load, which showing the view
    /// runs.
    /// </summary>
    public override void OpenRecord(int id)
    {
        RecordToOpen = null;
        if (ShowDeleted(id))
        {
            return;
        }

        if (Products.FirstOrDefault(p => p.Id == id) is not { } product)
        {
            RecordToOpen = id;
            return;
        }

        if (!List.VisibleProducts.Contains(product))
        {
            List.ClearListFilters();
        }

        OpenFromList(product);
    }

    /// <summary>The Vim command line's <c>:e name</c>: opens the product the name means best, as an address would.</summary>
    public override bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(Products, product => product.Name, text) is not { } product)
        {
            return false;
        }

        OpenRecord(product.Id);
        return true;
    }

    /// <summary>
    /// A deleted product (a Dependents tile names it as deleted): its row picked in the Deleted folder, opened, a filter
    /// hiding it cleared. False when <paramref name="id"/> isn't a deleted one.
    /// </summary>
    internal bool ShowDeleted(int id)
    {
        if (DeletedProducts.All(p => p.Id != id))
        {
            return false;
        }

        if (List.ProductTree.Find(id) is null)
        {
            List.ClearListFilters();
        }

        List.ProductTree.DeletedFolder.IsExpanded = true;
        List.SelectedTreeNode = List.ProductTree.Find(id);
        return true;
    }

    /// <summary>
    /// New, New composite product, or a product started from a sub-assembly: a fresh form in the preview tab, which
    /// <paramref name="start"/> fills. A product still waiting for the load to open it, or a sub-assembly waiting for it to
    /// start a product from, no longer is: New is the later word.
    /// </summary>
    private void NewDraft(Action<ProductForm> start)
    {
        _subAssemblyToStartFrom = null;
        OpenDraft(start);
    }

    /// <summary>Sub-Assemblies' New Product, once the load has read the sub-assembly as it's saved now.</summary>
    private void StartNewProductLinking(int subAssemblyId)
    {
        if (Linkable.FirstOrDefault(s => s.Id == subAssemblyId && s.OwnerProductId is null && s.Role?.IsPackaging != true) is not { } subAssembly)
        {
            ((IScreenPartHost)this).ShowError("That sub-assembly is no longer on the Sub-Assemblies screen.");
            return;
        }

        NewDraft(form => form.StartNewProductLinking(subAssembly));
    }

    /// <summary>
    /// Moves <paramref name="product"/> to the Deleted folder once it's confirmed: a form's Delete, for its own, or the
    /// list's, for any, asked as <paramref name="asker"/>'s question (the view's own with none). The tab holding it closes.
    /// </summary>
    internal Task DeleteAsync(Product product, ProductForm? asker) =>
        DeleteRecordAsync(
            product.Id,
            asker,
            $"Delete '{product.Name}'? It moves to the Deleted folder at the bottom of the list, where it can be restored as it is.",
            () => _productRepository.DeleteAsync(product.Id, CancellationToken.None));

    /// <summary>Takes <paramref name="product"/> out of the Deleted folder, back to its own, and opens it, as a click on it would.</summary>
    private Task RestoreAsync(Product product) => ((IScreenPartHost)this).RunSafelyAsync(async () =>
    {
        await _productRepository.RestoreAsync(product.Id, CancellationToken.None);
        await LoadCommand.ExecuteAsync();
        OpenRecord(product.Id);
    });

    /// <summary>Deletes <paramref name="product"/>, from the Deleted folder, for good once it's confirmed.</summary>
    private Task DeletePermanentlyAsync(Product product) =>
        ConfirmedAsync(
            Form,
            product.IsComposite
                ? $"Delete '{product.Name}' permanently? The products it holds stay. This can't be undone."
                : $"Delete '{product.Name}' permanently? Its own sub-assemblies go with it; the ones it links stay on the Sub-Assemblies screen. This can't be undone.",
            async () =>
            {
                await _productRepository.DeletePermanentlyAsync(product.Id, CancellationToken.None);
                await LoadCommand.ExecuteAsync();
            });

    /// <summary>
    /// Makes a product named <paramref name="name"/> from <paramref name="sourceProductId"/> as saved (a clone, or a variant
    /// with <paramref name="isVariant"/>) with its first version, noting where it came from, both or neither
    /// (VersionedSaves.MakeProductFromAsync), and opens it in a tab of its own. A tab holding the product with changes not
    /// saved is shown and asked about first, as what's made is the product as saved: saved, they're in it; not, they go.
    /// </summary>
    internal async Task MakeFromAsync(int sourceProductId, string name, bool isVariant, ProductForm? asker)
    {
        if (Tabs.Holding(sourceProductId) is { Form.HasUnsavedChanges: true } holding)
        {
            Show(holding);
            if (!await holding.Form.ResolveUnsavedChangesAsync(revert: true))
            {
                return;
            }
        }

        await PartOf(asker).RunSafelyAsync(async () =>
        {
            var newId = await Task.Run(() => _versionedSaves.MakeProductFromAsync(
                sourceProductId,
                name,
                isVariant ? _productCloner.CreateVariantAsync : _productCloner.CloneAsync,
                isVariant ? VariantOf : ClonedFrom,
                DateTime.Now,
                CancellationToken.None));

            await LoadCommand.ExecuteAsync();
            if (Products.FirstOrDefault(p => p.Id == newId) is { } made)
            {
                OpenFromList(made, keep: true);
            }
        });
    }

    /// <summary>
    /// Applies Settings' costing defaults (labor, overhead, administrative and markup) to <paramref name="products"/>, once
    /// it's confirmed. A tab holding one of them with unsaved changes is shown and has them saved or thrown away first, and
    /// each tab holding one shows its new costing after.
    /// </summary>
    private async Task ApplyCostingDefaultsAsync(IReadOnlyList<Product> products)
    {
        if (products.Count == 0)
        {
            return;
        }

        var ids = products.Select(p => p.Id).ToHashSet();
        foreach (var tab in Tabs.All.Where(tab => tab.Form.DetailProductId is { } id && ids.Contains(id) && tab.Form.HasUnsavedChanges).ToList())
        {
            Show(tab);
            if (!await tab.Form.ResolveUnsavedChangesAsync(revert: true))
            {
                return;
            }
        }

        var part = (IScreenPartHost)this;
        await part.RunSafelyAsync(async () =>
        {
            var defaults = await Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var question = $"Set the costing of {ProductNames(products)} to the costing defaults: labor {defaults.LaborPercent:0.##}%, overhead "
                + $"{defaults.OverheadPercent:0.##}%, administrative {defaults.AdministrativePercent:0.##}% and markup {defaults.MarginPercent:0.##}%?"
                + "\n\nA version is recorded of each one this changes.";
            if (!await Runner.ConfirmDelete.Handle(question))
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
            var changed = await Task.Run(() => _versionedSaves.ApplyCostingAsync([.. ids], costing, DateTime.Now, CancellationToken.None));

            // The window's own save: each form showing one takes its new time, and its new costing below, as after its own Save.
            List<ProductForm> showing = [.. Tabs.All.Select(tab => tab.Form).Where(form => form.DetailProductId is { } id && ids.Contains(id))];
            foreach (var form in showing)
            {
                form.FollowOwnSaveOf(ids);
            }

            await LoadCommand.ExecuteAsync();
            foreach (var form in showing)
            {
                await form.ShowAppliedCostingAsync();
            }

            part.ShowSuccess(changed.Count == 0
                ? $"{ProductNames(products)} already {(products.Count == 1 ? "has" : "have")} the costing defaults."
                : $"The costing defaults were applied to {ProductNames(changed)}, with a version of each.");
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A change still settling when the app closes is written now, rather than lost.
            List.Dispose();
        }

        base.Dispose(disposing);

        if (disposing)
        {
            _productCache.Dispose();
            _categoryCache.Dispose();
            _flagChecks.Dispose();
            _printRequests.Dispose();
            _subAssemblyOpenRequests.Dispose();
        }
    }

    // What the list asks of the view (IProductsListHost) on top of what any record view's list asks (RecordViewState): the
    // form showing, the lists the view loaded, and what the list's actions do to the forms holding the products they act on.
    Product? IProductsListHost.SelectedProduct => Form?.SelectedProduct;

    IReadOnlyList<ProductFolder> IProductsListHost.Folders
    {
        get => Folders;
        set => Folders = value;
    }

    bool IProductsListHost.IsEditingComposite => Form?.IsEditingComposite == true;

    NewProductPlace? IProductsListHost.NewProductPlace => Form?.NewProductPlace;

    Task IProductsListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void IProductsListHost.Pick(Product product) => OpenFromList(product);

    void IProductsListHost.KeepOpen(Product product) => OpenFromList(product, keep: true);

    void IProductsListHost.StartNew(ProductStage? stage, int? folderId, bool isComposite) =>
        NewDraft(form => form.StartNew(stage, folderId, isComposite));

    Task IProductsListHost.SaveNewAsync(string name) => Form?.SaveNewAsync(name) ?? Task.CompletedTask;

    void IProductsListHost.NameNew(string name) => Form?.NameNew(name);

    void IProductsListHost.ClearNewName() => Form?.ClearNewName();

    void IProductsListHost.Renamed(Product product, string name) => Tabs.Holding(product.Id)?.Form.TakeRename(product, name);

    void IProductsListHost.ShowMoved(IReadOnlyList<Product> moved)
    {
        foreach (var tab in Tabs.All)
        {
            var form = tab.Form;
            form.RefreshWith(() => form.ShowMoved(moved));
        }
    }

    bool IProductsListHost.ShowsUnchanged(IReadOnlyCollection<int> ids) =>
        Tabs.All.Any(tab => tab.Form.SelectedProduct is { } product && ids.Contains(product.Id) && !tab.Form.HasUnsavedChanges);

    void IProductsListHost.FolderDeleted(int folderId, int? parentId)
    {
        foreach (var tab in Tabs.All)
        {
            tab.Form.NewProductPlace.FolderDeleted(folderId, parentId);
        }
    }

    Task IProductsListHost.CloneAsync(Product product, string name) => MakeFromAsync(product.Id, name, isVariant: false, Form);

    Task IProductsListHost.CreateVariantAsync(Product product, string name) => MakeFromAsync(product.Id, name, isVariant: true, Form);

    Task IProductsListHost.DeleteAsync(Product product) => DeleteAsync(product, Form);

    Task IProductsListHost.RestoreAsync(Product product) => RestoreAsync(product);

    Task IProductsListHost.DeletePermanentlyAsync(Product product) => DeletePermanentlyAsync(product);

    Task IProductsListHost.ApplyCostingDefaultsAsync(IReadOnlyList<Product> products) => ApplyCostingDefaultsAsync(products);

    void IProductsListHost.Print(PrintScope scope) => _printRequests.OnNext(scope);
}
