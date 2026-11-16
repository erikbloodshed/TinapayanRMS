using System.Collections.ObjectModel;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// What the Print tab prints: the product the Products screen has open, a folder of the Products list (a stage, or one of
/// the user's folders, with the folders in it), or every product.
/// </summary>
/// <param name="Label">What the tab's Print choice reads: "This product", "Production › Breads", "All products".</param>
public closed record PrintScope(string Label)
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
        AllProducts => _ => true,
    };

    // By the folders alone: a product is always in its folder's stage, so a folder moved to the other stage since it was
    // picked has its products with it there, where the stage it was picked in would find none.
    private static Func<Product, bool> InFolders(IReadOnlySet<int> folderIds) =>
        product => product.FolderId is { } folderId && folderIds.Contains(folderId);

    /// <summary>
    /// Its label, which a screen reader reads for the dropdown's choice. Sealed, so each case keeps it rather than
    /// a record's own "Folder { Label = … }".
    /// </summary>
    public sealed override string ToString() => Label;

    /// <summary>The product the Products screen has open, unsaved edits included.</summary>
    public sealed record ThisProduct() : PrintScope("This product");

    /// <summary>
    /// A folder of the list: "Production" for a stage's, "Production › Breads › Holiday" for one of the user's
    /// (<paramref name="FolderId"/>; null for a stage), which prints with every folder in it (Holds). By its id, so the same
    /// folder picked again is the same choice.
    /// </summary>
    public sealed record Folder(string Label, ProductStage Stage, int? FolderId, bool IsStage) : PrintScope(Label)
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
                ? this with { Label = ProductFolderRules.PathTitle(folders, now.Stage, id), Stage = now.Stage }
                : this;

        public override string? Title => Label;
    }

    public sealed record AllProducts() : PrintScope("All products")
    {
        public override string? Title => "All Products";
    }
}

/// <summary>
/// The main window's Print tab: what it prints (<see cref="SelectedScope"/>) and the pages for it (<see cref="PrintSet"/>),
/// prepared each time the tab is shown (<see cref="LoadCommand"/>, which the main window runs as it runs every tab's) and
/// each time another choice is made. This product is the Products screen's product as it shows it, unsaved edits
/// included; a folder or all products are read as they're saved (SavedProductReader), each starting a page of its own
/// after a contents page.
/// </summary>
public sealed class PrintTabViewModel : ViewModelBase
{
    private readonly ProductsViewModel _products;
    private readonly IProductRepository _productRepository;
    private readonly IProductFolderRepository _folderRepository;
    private readonly SavedProductReader _savedProducts;

    // The preparation running; a newer one cancels it.
    private CancellationTokenSource? _preparing;

    // Set while a Print request picks the choice, so the choice alone doesn't prepare the pages: showing the tab does.
    private bool _isPicking;

    public PrintTabViewModel(
        ProductsViewModel products,
        IProductRepository productRepository,
        IProductFolderRepository folderRepository,
        SavedProductReader savedProducts,
        ILogger logger)
        : base(logger)
    {
        _products = products;
        _productRepository = productRepository;
        _folderRepository = folderRepository;
        _savedProducts = savedProducts;

        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(PrepareAsync));
    }

    /// <summary>The choices the tab offers: This product, the folder a menu asked for (while there is one), All products.</summary>
    public ObservableCollection<PrintScope> Scopes { get; } = [new PrintScope.ThisProduct(), new PrintScope.AllProducts()];

    private PrintScope _selectedScope = new PrintScope.ThisProduct();
    /// <summary>The choice showing. The dropdown clears its pick while the folder it showed goes, and that's ignored.</summary>
    public PrintScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            // Another choice picked prepares its pages; one the tab picks itself (_isPicking) is prepared by whatever picked it.
            if (value is not null && RaiseAndSetIfChanged(ref _selectedScope, value) && !_isPicking)
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

    /// <summary>Prepares the pages for the choice showing.</summary>
    public ScreenCommand LoadCommand { get; }

    /// <summary>
    /// A Print on Products: the header's for This product, a folder's or the list's for a set. A folder takes the place of
    /// any folder asked for before, beside This product and All products. Showing the tab then prepares the pages.
    /// </summary>
    public void Pick(PrintScope scope)
    {
        if (scope is PrintScope.Folder && !Scopes.Contains(scope))
        {
            foreach (var folder in Scopes.OfType<PrintScope.Folder>().ToList())
            {
                Scopes.Remove(folder);
            }

            Scopes.Insert(1, scope);
        }

        _isPicking = true;
        try
        {
            SelectedScope = scope;
        }
        finally
        {
            _isPicking = false;
        }
    }

    /// <summary>
    /// <paramref name="scope"/>, and the choice showing it, named as the folder is now (PrintScope.Folder.AsNow): renamed
    /// or moved since it was picked, the choice and the pages' title say where it is, not where it was.
    /// </summary>
    private PrintScope NamedAsNow(PrintScope scope, IReadOnlyList<ProductFolder> folders)
    {
        if (scope is not PrintScope.Folder picked || picked.AsNow(folders) is var now && now == picked)
        {
            return scope;
        }

        if (Scopes.IndexOf(picked) is var index and >= 0)
        {
            Scopes[index] = now;
        }

        // Picked as it already is, so it prepares nothing again.
        _isPicking = true;
        try
        {
            SelectedScope = now;
        }
        finally
        {
            _isPicking = false;
        }

        return now;
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
            if (SelectedScope is PrintScope.ThisProduct)
            {
                PrepareThisProduct();
            }
            else
            {
                await PrepareSetAsync(SelectedScope, preparing.Token);
            }
        }
        catch (OperationCanceledException) when (preparing.IsCancellationRequested)
        {
            // A newer choice, or the tab shown again, is preparing its own.
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
        switch (_products.PreparePrintout())
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

    private async Task PrepareSetAsync(PrintScope scope, CancellationToken cancellationToken)
    {
        Progress = "Preparing…";

        // The products and their folders as they're saved now, read together, not the folders as the Products screen last
        // read them: products another computer filed in a folder it made in the one picked are in the set, in their place.
        var productsRead = _productRepository.GetAllAsync(cancellationToken);
        var foldersRead = _folderRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(productsRead, foldersRead);
        var folders = await foldersRead;
        scope = NamedAsNow(scope, folders);
        var products = ProductFolderRules.InListOrder((await productsRead).Where(scope.Holds(folders)), folders);
        var printedAt = DateTime.Now;

        // At the costs the Products screen shows: buffered, unless it's been switched to unbuffered. Read together, in the
        // same dozen or so queries however many products the set holds.
        Progress = products.Count == 1 ? "Preparing 1 product…" : $"Preparing {products.Count} products…";
        var saved = await _savedProducts.ReadAllAsync(products, _products.Costing.CostBasis, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        List<ProductPrintout> printouts = [.. saved.Select(s => s.ToPrintout(printedAt))];

        // The Products screen's product prints as it's saved; its edits there don't reach a set.
        Note = _products.History.IsDirty && _products.SelectedProduct is { } open && products.Any(p => p.Id == open.Id)
            ? $"{open.Name} has unsaved changes on Products; it's printed as saved."
            : null;
        PrintSet = new ProductPrintSet(scope.Title, printouts, printedAt);
    }
}
