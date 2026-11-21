using Npgsql;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Printing;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// What a Print on the Products screen asks the Print dialog to print: the product it has open, a folder of its list (a
/// stage, or one of the user's folders, with the folders in it), the products picked in the list, or every product. The
/// dialog ticks a set's products in its Print list (PrintDialogState.Pick).
/// </summary>
public closed record PrintScope
{
    /// <summary>A set's title over its contents, and in the job's name; null for This product.</summary>
    public virtual string? Title => null;

    /// <summary>
    /// Which products are in the set, with the <paramref name="folders"/> as they're saved now: any product for All
    /// products; for a stage's folder, every product in that stage; for one of the user's folders, every product in it and
    /// in the folders in it, however deep, a folder made in it since it was picked included, wherever it's gone since.
    /// </summary>
    public Func<Product, bool> Holds(IEnumerable<ProductFolder> folders) => this switch
    {
        ThisProduct => _ => false,
        Folder { IsStage: true } stage => product => product.Stage == stage.Stage,
        Folder { FolderId: { } id } => InFolders(ProductFolderRules.SubtreeIds(folders, id)),
        Folder => _ => false,
        Products picked => product => picked.ProductIds.Contains(product.Id),
        AllProducts => _ => true,
    };

    // By the folders alone: a product is always in its folder's stage, so a folder moved to the other stage since it was
    // picked has its products with it there, where the stage it was picked in would find none.
    private static Func<Product, bool> InFolders(IReadOnlySet<int> folderIds) =>
        product => product.FolderId is { } folderId && folderIds.Contains(folderId);

    /// <summary>The product the Products screen has open, unsaved edits included: the Print list with nothing ticked.</summary>
    public sealed record ThisProduct : PrintScope;

    /// <summary>
    /// A folder of the list: "Production" for a stage's, "Production › Breads › Holiday" for one of the user's
    /// (<paramref name="FolderId"/>; null for a stage), which prints with every folder in it (Holds).
    /// </summary>
    public sealed record Folder(string Path, ProductStage Stage, int? FolderId, bool IsStage) : PrintScope
    {
        /// <summary>The folder <paramref name="folder"/> shows.</summary>
        public static Folder Of(ProductFolderNode folder) =>
            new(ProductTree.PathTitle(folder), folder.Stage, folder.FolderId, folder.IsStage);

        /// <summary>
        /// The same folder, named and staged as <paramref name="folders"/> have it now: "Production › Breads" once Breads
        /// has gone to Production. Itself for a stage's folder, or one that's gone.
        /// </summary>
        public Folder AsNow(IReadOnlyList<ProductFolder> folders) =>
            FolderId is { } id && folders.FirstOrDefault(f => f.Id == id) is { } now
                ? this with { Path = ProductFolderRules.PathTitle(folders, now.Stage, id), Stage = now.Stage }
                : this;

        public override string? Title => Path;
    }

    /// <summary>Products picked in the Products list (Ctrl or Shift, then Print on their menu), by their ids.</summary>
    public sealed record Products(IReadOnlyList<int> ProductIds) : PrintScope
    {
        /// <summary>The choice for <paramref name="products"/>.</summary>
        public static Products Of(IReadOnlyList<Product> products) => new([.. products.Select(p => p.Id)]);

        public override string? Title => "Selected Products";
    }

    public sealed record AllProducts : PrintScope
    {
        public override string? Title => "All Products";
    }
}

/// <summary>A product the Print dialog's Print list offers to tick, with its category (null for none).</summary>
public sealed record PrintableProduct(int Id, string Name, int? CategoryId);

/// <summary>
/// A product category the Print dialog's Categories list offers, which holds a product in use; <see cref="None"/> stands for
/// the products with no category.
/// </summary>
public sealed record PrintableCategory(int Id, string Name)
{
    /// <summary>The Categories list's id for the products with no category: no saved category's id is 0.</summary>
    public const int None = 0;
}

/// <summary>
/// The Print dialog's products: what it prints and the pages for it (<see cref="PrintSet"/>), prepared each time the dialog
/// opens on them (<see cref="LoadCommand"/>, which the dialog runs as a screen runs its load) and each time the products
/// or categories ticked change. With no product ticked it prints the product of the Products tab showing, as it shows it,
/// unsaved edits included; the products ticked are read as they're saved (SavedProductReader), each starting a page of its own
/// after a contents page, or on their own with no contents page for one.
/// </summary>
public sealed class PrintDialogState : ScreenState
{
    private readonly ProductsState _products;
    private readonly IPrintQueries _queries;
    private readonly PrintPage _page;

    // The preparation running; a newer one cancels it.
    private CancellationTokenSource? _preparing;

    // A Print on Products not yet turned into the products it ticks: the next preparation does that, with the products and
    // folders as they're saved then.
    private PrintScope? _request;

    // The set a Print on Products ticked, and the products it ticked: the pages carry its title (a folder's path, All
    // Products) while those are the products ticked.
    private (PrintScope Scope, int[] ProductIds)? _ticked;

    // Every product in use, in the Products list's order, before the categories ticked narrow it.
    private IReadOnlyList<PrintableProduct> _everyProduct = [];

    public PrintDialogState(ProductsState products, IPrintQueries queries, PrintPage page, ILogger logger)
        : base(logger)
    {
        _page = page;
        _products = products;
        _queries = queries;

        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(PrepareAsync));
    }

    private IReadOnlyList<PrintableProduct> _productChoices = [];
    /// <summary>
    /// The products the Print list offers, a flat list in the Products list's order, as they're saved: every one in use, or
    /// those in the categories ticked. Read each time the dialog prepares its pages, and replaced only when something in it
    /// changed, so the list stays as it is while products are ticked in it.
    /// </summary>
    public IReadOnlyList<PrintableProduct> ProductChoices
    {
        get => _productChoices;
        private set => RaiseAndSetIfChanged(ref _productChoices, value);
    }

    private IReadOnlyList<PrintableCategory> _categoryChoices = [];
    /// <summary>The categories the Categories list offers: those holding a product in use, by name, then No category if any has none.</summary>
    public IReadOnlyList<PrintableCategory> CategoryChoices
    {
        get => _categoryChoices;
        private set => RaiseAndSetIfChanged(ref _categoryChoices, value);
    }

    private int[] _chosenProductIds = [];
    /// <summary>
    /// The products ticked in the Print list, which print as saved; none prints the product Products has open. A Print on
    /// Products ticks its products here, in place of those ticked before.
    /// </summary>
    public int[] ChosenProductIds
    {
        get => _chosenProductIds;
        set
        {
            if (SetChosenProducts(value ?? []))
            {
                _ = LoadCommand.ExecuteAsync();
            }
        }
    }

    private int[] _chosenCategoryIds = [];
    /// <summary>
    /// The categories ticked in the Categories list (<see cref="PrintableCategory.None"/> for the products with none): the
    /// Print list offers only their products, and a product ticked in another is unticked. None ticked offers every product.
    /// </summary>
    public int[] ChosenCategoryIds
    {
        get => _chosenCategoryIds;
        set
        {
            value ??= [];
            if (value.Order().SequenceEqual(_chosenCategoryIds.Order()))
            {
                return;
            }

            _chosenCategoryIds = value;
            RaisePropertyChanged();
            // A set's title may follow the categories ticked (SetTitle), so it's prepared again whenever products are ticked.
            if (ShowProductChoices() || _chosenProductIds.Length > 0)
            {
                _ = LoadCommand.ExecuteAsync();
            }
        }
    }

    private ProductPrintSet? _printSet;
    /// <summary>The pages as last prepared; null until the first are, and kept when a preparation brings none.</summary>
    public ProductPrintSet? PrintSet
    {
        get => _printSet;
        private set => RaiseAndSetIfChanged(ref _printSet, value);
    }

    private string? _progress;
    /// <summary>While a set is read: "Preparing 30 products…".</summary>
    public string? Progress
    {
        get => _progress;
        private set => RaiseAndSetIfChanged(ref _progress, value);
    }

    private string? _note;
    /// <summary>What the pages leave out: an open form on Products that can't be applied yet, or unsaved edits a set prints without.</summary>
    public string? Note
    {
        get => _note;
        private set => RaiseAndSetIfChanged(ref _note, value);
    }

    /// <summary>Prepares the pages for the products ticked, or the product Products has open with none.</summary>
    public ScreenCommand LoadCommand { get; }

    /// <summary>
    /// A Print on Products: the header's for This product unticks every product; a folder's, the products picked or the
    /// list's tick their products in place of those ticked before, with every category offered, once the next preparation
    /// has read them. Opening the dialog then prepares the pages.
    /// </summary>
    public void Pick(PrintScope scope)
    {
        _ticked = null;
        if (scope is PrintScope.ThisProduct)
        {
            _request = null;
            SetChosenProducts([]);
            return;
        }

        _request = scope;
        if (_chosenCategoryIds.Length > 0)
        {
            _chosenCategoryIds = [];
            RaisePropertyChanged(nameof(ChosenCategoryIds));
        }
    }

    /// <summary>
    /// Ticks <paramref name="ids"/> in the Print list's order, so its box names them as the list shows them ("Asado Roll
    /// 60g +2 more"); any not listed go last. False when they're the products ticked already.
    /// </summary>
    private bool SetChosenProducts(IEnumerable<int> ids)
    {
        var placeOf = _everyProduct.Select((choice, at) => (choice.Id, at)).ToDictionary(p => p.Id, p => p.at);
        int[] listed = [.. ids.Distinct().OrderBy(id => placeOf.GetValueOrDefault(id, int.MaxValue))];
        if (listed.SequenceEqual(_chosenProductIds))
        {
            return false;
        }

        _chosenProductIds = listed;
        RaisePropertyChanged(nameof(ChosenProductIds));
        return true;
    }

    /// <summary>
    /// Offers the products in the categories ticked, or every one with none ticked, and unticks any product it no longer
    /// offers. True when it unticked one.
    /// </summary>
    private bool ShowProductChoices()
    {
        var categories = _chosenCategoryIds.ToHashSet();
        List<PrintableProduct> choices = categories.Count == 0
            ? [.. _everyProduct]
            : [.. _everyProduct.Where(p => categories.Contains(p.CategoryId ?? PrintableCategory.None))];
        if (!choices.SequenceEqual(ProductChoices))
        {
            ProductChoices = choices;
        }

        var offered = choices.Select(c => c.Id).ToHashSet();
        return _chosenProductIds.Any(id => !offered.Contains(id)) && SetChosenProducts(_chosenProductIds.Where(offered.Contains));
    }

    private async Task PrepareAsync()
    {
        var preparing = new CancellationTokenSource();
        var previous = _preparing;
        _preparing = preparing;
        if (previous is not null)
        {
            await previous.CancelAsync();
        }

        try
        {
            // The page the pages are laid out on, as Settings → Printing has it now (one row).
            await _page.RefreshAsync(preparing.Token);

            // This product first, as the Products screen shows it, needing nothing else read.
            var isThisProduct = _chosenProductIds.Length == 0 && _request is null;
            if (isThisProduct)
            {
                PrepareThisProduct();
            }
            else
            {
                Progress = "Preparing…";
            }

            // The products, their folders and categories as they're saved now, read together, for the lists and a set alike,
            // not the folders as the Products screen last read them: products another computer filed in a folder it made in
            // the one picked are in the set, in their place.
            var (products, folders, categories) = await _queries.ReadListsAsync(preparing.Token);
            ShowChoices(products, folders, categories);

            // A Print on Products ticks its products now, as they're saved.
            if (_request is { } request)
            {
                _request = null;
                SetChosenProducts(products.Where(request.Holds(folders)).Select(p => p.Id));
                _ticked = (request, _chosenProductIds);
            }

            // The products ticked may have gone since, and with them the set, back to This product.
            if (_chosenProductIds.Length == 0)
            {
                if (!isThisProduct)
                {
                    PrepareThisProduct();
                }
            }
            else
            {
                await PrepareSetAsync(products, folders, preparing.Token);
            }
        }
        catch (OperationCanceledException) when (preparing.IsCancellationRequested)
        {
            // A newer choice, or the dialog opened again, is preparing its own.
        }
        catch (NpgsqlException) when (preparing.IsCancellationRequested)
        {
            // The same, cancelled in the middle of a read, should Npgsql report it as its own exception rather than a cancellation.
        }
        finally
        {
            if (ReferenceEquals(_preparing, preparing))
            {
                _preparing = null;
                Progress = null;
            }

            preparing.Dispose();
        }
    }

    private void PrepareThisProduct()
    {
        switch (_products.Form?.PreparePrintout() ?? default)
        {
            case ProductPrintout printout:
                Note = null;
                PrintSet = ProductPrintSet.Of(printout);
                break;

            case PrintProblem(var problem):
                Note = $"These pages are the product as it was: the open sub-assembly form on Products can't be applied yet. {problem}";
                break;

            case null:
                Note = null;
                break;
        }
    }

    /// <summary>
    /// The lists as <paramref name="products"/> and <paramref name="categories"/> are saved now: every product in the
    /// Products list's order, the categories holding one, and those ticked less any gone since (deleted on Products, or on
    /// another computer).
    /// </summary>
    private void ShowChoices(IReadOnlyList<Product> products, IReadOnlyList<ProductFolder> folders, IReadOnlyList<ProductCategory> categories)
    {
        _everyProduct = [.. ProductFolderRules.InListOrder(products, folders).Select(p => new PrintableProduct(p.Id, p.Name, p.CategoryId))];

        var inUse = _everyProduct.Select(p => p.CategoryId ?? PrintableCategory.None).ToHashSet();
        List<PrintableCategory> categoryChoices =
        [
            .. categories.Where(c => inUse.Contains(c.Id)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => new PrintableCategory(c.Id, c.Name)),
            .. inUse.Contains(PrintableCategory.None) ? [new PrintableCategory(PrintableCategory.None, "No category")] : Array.Empty<PrintableCategory>(),
        ];
        if (!categoryChoices.SequenceEqual(CategoryChoices))
        {
            CategoryChoices = categoryChoices;
        }

        if (_chosenCategoryIds.Any(id => !inUse.Contains(id)))
        {
            _chosenCategoryIds = [.. _chosenCategoryIds.Where(inUse.Contains)];
            RaisePropertyChanged(nameof(ChosenCategoryIds));
        }

        ShowProductChoices();

        // Ticked before the list was read, they take its order now.
        SetChosenProducts(_chosenProductIds);
    }

    private async Task PrepareSetAsync(
        IReadOnlyList<Product> allProducts, IReadOnlyList<ProductFolder> folders, CancellationToken cancellationToken)
    {
        var ticked = _chosenProductIds.ToHashSet();
        var products = ProductFolderRules.InListOrder(allProducts.Where(p => ticked.Contains(p.Id)), folders);
        var printedAt = DateTime.Now;

        // At the costs the Products screen shows: buffered, unless it's been switched to unbuffered. Read together, in the
        // same dozen or so queries however many products the set holds.
        Progress = products.Count == 1 ? "Preparing 1 product…" : $"Preparing {products.Count} products…";
        var saved = await _queries.PriceAsSavedAsync(products, _products.Form?.Costing.CostBasis ?? CostBasis.Buffered, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        List<ProductPrintout> printouts = [.. saved.Select(s => s.ToPrintout(printedAt))];

        // A product open on Products prints as it's saved; its edits there don't reach a set.
        Note = _products.Tabs.All
            .Select(tab => tab.Form)
            .FirstOrDefault(form => form.HasUnsavedChanges && form.SelectedProduct is { } open && products.Any(p => p.Id == open.Id))?.SelectedProduct is { } edited
            ? $"{edited.Name} has unsaved changes on Products; it's printed as saved."
            : null;
        PrintSet = new ProductPrintSet(SetTitle(products.Count, folders), printouts, printedAt);
    }

    /// <summary>
    /// The title over a set's contents: the folder's path, as it's named now, or All Products, while the products a Print
    /// on Products ticked are those ticked, a folder's however few; none for one product otherwise, which prints on its
    /// own with no contents page; the categories' names, in the order they were ticked, as their list's box names them,
    /// while every product in them is ticked ("Cakes, Breads"), or All
    /// Products while every product is; Selected Products otherwise.
    /// </summary>
    private string? SetTitle(int count, IReadOnlyList<ProductFolder> folders)
    {
        if (_ticked is var (scope, ids) && ids.Order().SequenceEqual(_chosenProductIds.Order()) && scope is not PrintScope.Products)
        {
            return scope is PrintScope.Folder folder ? folder.AsNow(folders).Title : scope.Title;
        }

        if (count == 1)
        {
            return null;
        }

        if (_chosenProductIds.Length == ProductChoices.Count)
        {
            return _chosenCategoryIds.Length == 0
                ? new PrintScope.AllProducts().Title
                : string.Join(", ", _chosenCategoryIds.Select(id => CategoryChoices.FirstOrDefault(c => c.Id == id)?.Name).OfType<string>());
        }

        return "Selected Products";
    }
}
