using System.Collections.ObjectModel;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// What the Print tab prints: the product the Products screen has open, a folder of the Products list (a stage, or one of
/// the user's folders, with the folders in it), the products picked in the list, or every product.
/// </summary>
/// <param name="Label">What the tab's Print choice reads: "This product", "Production › Breads", "Selected products (3)", "All products".</param>
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
        Products picked => product => picked.ProductIds.Contains(product.Id),
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

    /// <summary>
    /// Products chosen one by one, by their ids, as they're saved: those ticked in the Print tab's product list, or picked in
    /// the Products list (Ctrl or Shift, then Print on their menu). "Selected products (3)", or the product's name for one,
    /// which prints on its own, with no contents page. They print in the list's order, whatever order they were chosen in.
    /// The same products chosen again are the same choice.
    /// </summary>
    public sealed record Products(string Label, IReadOnlyList<int> ProductIds) : PrintScope(Label)
    {
        /// <summary>The choice for <paramref name="products"/>.</summary>
        public static Products Of(IReadOnlyList<Product> products) => Of([.. products.Select(p => (p.Id, p.Name))]);

        /// <summary>The choice for <paramref name="products"/>, by id and name; a name not known yet is empty.</summary>
        public static Products Of(IReadOnlyList<(int Id, string Name)> products) => new(
            products is [var one] && one.Name.Length > 0 ? one.Name : $"Selected products ({products.Count})",
            [.. products.Select(p => p.Id).Order()]);

        public override string? Title => ProductIds.Count == 1 ? null : "Selected Products";

        // By the ids they hold, not the list object holding them, which a record would compare.
        public bool Equals(Products? other) => other is not null && Label == other.Label && ProductIds.SequenceEqual(other.ProductIds);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Label);
            foreach (var id in ProductIds)
            {
                hash.Add(id);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record AllProducts() : PrintScope("All products")
    {
        public override string? Title => "All Products";
    }
}

/// <summary>
/// A product the Print tab's product list offers to tick: its name, and where the Products list files it
/// ("Production › Breads", or "Production" at the top of the stage), which the list groups it under.
/// </summary>
public sealed record PrintableProduct(int Id, string Name, string Place);

/// <summary>
/// The main window's Print tab: what it prints (<see cref="SelectedScope"/>) and the pages for it (<see cref="PrintSet"/>),
/// prepared each time the tab is shown (<see cref="LoadCommand"/>, which the main window runs as it runs every tab's) and
/// each time another choice is made. This product is the Products screen's product as it shows it, unsaved edits
/// included; a folder, the products picked in the list or all products are read as they're saved (SavedProductReader),
/// each starting a page of its own after a contents page.
/// </summary>
public sealed class PrintTabState : ScreenState
{
    private readonly ProductsState _products;
    private readonly IProductRepository _productRepository;
    private readonly IProductFolderRepository _folderRepository;
    private readonly SavedProductReader _savedProducts;

    // The preparation running; a newer one cancels it.
    private CancellationTokenSource? _preparing;

    // Set while a Print request picks the choice, so the choice alone doesn't prepare the pages: showing the tab does.
    private bool _isPicking;

    public PrintTabState(
        ProductsState products,
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

    /// <summary>
    /// The choices the tab offers: This product, the folder a menu asked for last (while there is one), the products ticked
    /// in the product list or picked in the Products list (while there are some), All products.
    /// </summary>
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

    private IReadOnlyList<PrintableProduct> _productChoices = [];
    /// <summary>
    /// The products the tab's product list offers, every one in use, in the Products list's order, as they're saved: read
    /// each time the tab prepares its pages, and replaced only when something in it changed, so the list stays as it is
    /// while products are ticked in it.
    /// </summary>
    public IReadOnlyList<PrintableProduct> ProductChoices
    {
        get => _productChoices;
        private set => RaiseAndSetIfChanged(ref _productChoices, value);
    }

    private int[] _chosenProductIds = [];
    /// <summary>
    /// The products ticked in the product list. Ticking some makes them the choice showing ("Selected products (3)"), and
    /// prepares their pages; unticking the last takes the choice back to This product. Products picked in the Products
    /// list and printed from their menu are ticked here too.
    /// </summary>
    public int[] ChosenProductIds
    {
        get => _chosenProductIds;
        set
        {
            value ??= [];
            if (value.Order().SequenceEqual(_chosenProductIds.Order()))
            {
                return;
            }

            _chosenProductIds = value;
            RaisePropertyChanged();
            ShowChosen(ChosenScope(), show: true, quiet: false);
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
    /// A Print on Products: the header's for This product, a folder's, the products picked or the list's for a set. A folder
    /// takes the place of any folder asked for before; products picked are ticked in the product list, in place of those
    /// ticked before. Showing the tab then prepares the pages.
    /// </summary>
    public void Pick(PrintScope scope)
    {
        switch (scope)
        {
            case PrintScope.Products picked:
                _chosenProductIds = AsListed(picked.ProductIds);
                RaisePropertyChanged(nameof(ChosenProductIds));
                ShowChosen(picked, show: true, quiet: true);
                return;

            case PrintScope.Folder when !Scopes.Contains(scope):
                foreach (var folder in Scopes.OfType<PrintScope.Folder>().ToList())
                {
                    Scopes.Remove(folder);
                }

                Scopes.Insert(1, scope);
                break;
        }

        Choose(scope, quiet: true);
    }

    /// <summary>
    /// <paramref name="ids"/> in the product list's order, so the list's box names them as the list shows them ("Asado Roll
    /// 60g +2 more"); any not listed yet go last.
    /// </summary>
    private int[] AsListed(IEnumerable<int> ids)
    {
        var placeOf = ProductChoices.Select((choice, at) => (choice.Id, at)).ToDictionary(p => p.Id, p => p.at);
        return [.. ids.OrderBy(id => placeOf.GetValueOrDefault(id, int.MaxValue))];
    }

    /// <summary>The choice for the products ticked, named from the product list; null with none ticked.</summary>
    private PrintScope.Products? ChosenScope() => _chosenProductIds.Length == 0
        ? null
        : PrintScope.Products.Of([.. _chosenProductIds.Select(id => (id, ProductChoices.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty))]);

    /// <summary>
    /// Offers <paramref name="chosen"/> in place of the products chosen before, before All products, and shows it when
    /// <paramref name="show"/> or when they were showing; with none chosen, This product shows if they were showing.
    /// <paramref name="quiet"/>: the choice alone prepares nothing, as a Print request's doesn't, or a preparation already
    /// running goes on with it.
    /// </summary>
    private void ShowChosen(PrintScope.Products? chosen, bool show, bool quiet)
    {
        var before = Scopes.OfType<PrintScope.Products>().FirstOrDefault();
        if (chosen is not null && !Scopes.Contains(chosen))
        {
            Scopes.Insert(Scopes.Count - 1, chosen);
        }

        // Shown before the old choice goes, so the picker never shows nothing.
        if (chosen is not null && (show || SelectedScope is PrintScope.Products))
        {
            Choose(chosen, quiet);
        }
        else if (chosen is null && SelectedScope is PrintScope.Products)
        {
            Choose(Scopes[0], quiet);
        }

        if (before is not null && before != chosen)
        {
            Scopes.Remove(before);
        }
    }

    /// <summary>Shows <paramref name="scope"/>, preparing its pages unless <paramref name="quiet"/>.</summary>
    private void Choose(PrintScope scope, bool quiet)
    {
        _isPicking = quiet;
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
        Choose(now, quiet: true);
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
            // This product first, as the Products screen shows it, needing nothing read.
            var isThisProduct = SelectedScope is PrintScope.ThisProduct;
            if (isThisProduct)
            {
                PrepareThisProduct();
            }
            else
            {
                Progress = "Preparing…";
            }

            // The products and their folders as they're saved now, read together, for the product list and a set alike, not
            // the folders as the Products screen last read them: products another computer filed in a folder it made in the
            // one picked are in the set, in their place.
            var productsRead = _productRepository.GetAllAsync(preparing.Token);
            var foldersRead = _folderRepository.GetAllAsync(preparing.Token);
            await Task.WhenAll(productsRead, foldersRead);
            var (products, folders) = (await productsRead, await foldersRead);
            ShowChoices(products, folders);

            // The products ticked may have gone since, and the choice with them, back to This product.
            if (!isThisProduct && SelectedScope is PrintScope.ThisProduct)
            {
                PrepareThisProduct();
            }
            else if (!isThisProduct)
            {
                await PrepareSetAsync(SelectedScope, products, folders, preparing.Token);
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

    /// <summary>
    /// The product list as <paramref name="products"/> are saved now, in the Products list's order, each under its place in
    /// it; and the products ticked, less any gone since (deleted on Products, or on another computer), named as they are now.
    /// </summary>
    private void ShowChoices(IReadOnlyList<Product> products, IReadOnlyList<ProductFolder> folders)
    {
        List<PrintableProduct> choices =
        [
            .. ProductFolderRules.InListOrder(products, folders).Select(p => new PrintableProduct(
                p.Id,
                p.Name,
                p.FolderId is { } folderId ? ProductFolderRules.PathTitle(folders, p.Stage, folderId) : ProductRules.StageTitle(p.Stage))),
        ];
        if (!choices.SequenceEqual(ProductChoices))
        {
            ProductChoices = choices;
        }

        var listed = choices.Select(c => c.Id).ToHashSet();
        if (_chosenProductIds.Any(id => !listed.Contains(id)))
        {
            _chosenProductIds = [.. _chosenProductIds.Where(listed.Contains)];
            RaisePropertyChanged(nameof(ChosenProductIds));
        }

        // Ticked from the Products list before the product list was read, they take its order now.
        if (AsListed(_chosenProductIds) is var listedOrder && !listedOrder.SequenceEqual(_chosenProductIds))
        {
            _chosenProductIds = listedOrder;
            RaisePropertyChanged(nameof(ChosenProductIds));
        }

        if (ChosenScope() is var chosen && chosen != Scopes.OfType<PrintScope.Products>().FirstOrDefault())
        {
            ShowChosen(chosen, show: false, quiet: true);
        }
    }

    private async Task PrepareSetAsync(PrintScope scope, IReadOnlyList<Product> allProducts, IReadOnlyList<ProductFolder> folders, CancellationToken cancellationToken)
    {
        scope = NamedAsNow(scope, folders);
        var products = ProductFolderRules.InListOrder(allProducts.Where(scope.Holds(folders)), folders);
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
