using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Npgsql;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What the Print tab prints: the product the Products screen has open, a folder of the Products list (a stage, or a
/// category or Uncategorized in one), or every product.
/// </summary>
/// <param name="Label">What the tab's Print choice reads: "This product", "Breads · Production", "All products".</param>
public closed record PrintScope(string Label)
{
    /// <summary>A set's title over its contents, and in the job's name; null for This product.</summary>
    public virtual string? Title => null;

    /// <summary>
    /// <paramref name="product"/> is in the set: any product for All products; for a folder, its stage's, and in a
    /// category's folder that category's, in Uncategorized a product with none.
    /// </summary>
    public bool Holds(Product product) => this switch
    {
        ThisProduct => false,
        Folder folder => product.Stage == folder.Stage && (folder.IsStage || product.CategoryId == folder.CategoryId),
        AllProducts => true,
    };

    /// <summary>
    /// Its label, which a screen reader reads for the dropdown's choice. Sealed, so each case keeps it rather than
    /// a record's own "Folder { Label = … }".
    /// </summary>
    public sealed override string ToString() => Label;

    /// <summary>The product the Products screen has open, unsaved edits included.</summary>
    public sealed record ThisProduct() : PrintScope("This product");

    /// <summary>A folder of the list: "Production" for a stage's, "Breads · Production" for a category's, or "Uncategorized · Production".</summary>
    public sealed record Folder(string Label, ProductStage Stage, int? CategoryId, bool IsStage) : PrintScope(Label)
    {
        public Folder(ProductFolderNode folder)
            : this(folder.IsStage ? folder.Title : $"{folder.Title} · {ProductFolderNode.StageTitle(folder.Stage)}", folder.Stage, folder.CategoryId, folder.IsStage)
        {
        }

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
/// after a contents page (the user's choices, 2026-09-27).
/// </summary>
public sealed class PrintTabViewModel : ViewModelBase
{
    private readonly ProductsViewModel _products;
    private readonly IProductRepository _productRepository;
    private readonly SavedProductReader _savedProducts;

    // The preparation running; a newer one cancels it.
    private CancellationTokenSource? _preparing;

    // Set while a Print request picks the choice, so the choice alone doesn't prepare the pages: showing the tab does.
    private bool _isPicking;

    public PrintTabViewModel(ProductsViewModel products, IProductRepository productRepository, SavedProductReader savedProducts, ILogger logger)
        : base(logger)
    {
        _products = products;
        _productRepository = productRepository;
        _savedProducts = savedProducts;

        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(PrepareAsync));

        this.WhenAnyValue(x => x.SelectedScope)
            .Skip(1)
            .Where(_ => !_isPicking)
            .Subscribe(_ => LoadCommand.Execute().Subscribe());
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
            if (value is not null)
            {
                this.RaiseAndSetIfChanged(ref _selectedScope, value);
            }
        }
    }

    private ProductPrintSet? _printSet;
    /// <summary>The pages as last prepared; null until the first are, and kept when a preparation brings none.</summary>
    public ProductPrintSet? PrintSet
    {
        get => _printSet;
        private set => this.RaiseAndSetIfChanged(ref _printSet, value);
    }

    private string? _progress;
    /// <summary>While a set is read: "Preparing 30 products…".</summary>
    public string? Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string? _note;
    /// <summary>What the pages leave out: an open form on Products that can't be applied yet, or unsaved edits a set prints without.</summary>
    public string? Note
    {
        get => _note;
        private set => this.RaiseAndSetIfChanged(ref _note, value);
    }

    /// <summary>Prepares the pages for the choice showing.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

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
        var products = ProductTree.InListOrder((await _productRepository.GetAllAsync(cancellationToken)).Where(scope.Holds));
        var printedAt = DateTime.Now;

        // At the costs the Products screen shows: buffered, unless it's been switched to unbuffered. Read together, in the
        // same dozen or so queries however many products the set holds (2026-10-03; they were read one at a time, with
        // "Preparing 12 of 30…" while they were).
        Progress = products.Count == 1 ? "Preparing 1 product…" : $"Preparing {products.Count} products…";
        var saved = await _savedProducts.ReadAllAsync(products, _products.CostBasis, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        List<ProductPrintout> printouts = [.. saved.Select(s => s.ToPrintout(printedAt))];

        // The Products screen's product prints as it's saved; its edits there don't reach a set.
        Note = _products.History.IsDirty && _products.SelectedProduct is { } open && products.Any(p => p.Id == open.Id)
            ? $"{open.Name} has unsaved changes on Products; it's printed as saved."
            : null;
        PrintSet = new ProductPrintSet(scope.Title, printouts, printedAt);
    }
}
