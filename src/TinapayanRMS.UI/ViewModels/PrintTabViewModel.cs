using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Microsoft.Data.SqlClient;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

public enum PrintScopeKind
{
    ThisProduct,
    Folder,
    AllProducts,
}

/// <summary>
/// What the Print tab prints: the product the Products screen has open, a folder of the Products list (a stage, or a
/// category or Uncategorized in one), or every product.
/// </summary>
/// <param name="Label">What the tab's Print choice reads: "This product", "Breads · Production", "All products".</param>
/// <param name="Title">A set's title over its contents, and in the job's name; null for This product.</param>
public sealed record PrintScope(PrintScopeKind Kind, string Label, string? Title = null, ProductStage Stage = default, int? CategoryId = null, bool IsStage = false)
{
    public static PrintScope ThisProduct { get; } = new(PrintScopeKind.ThisProduct, "This product");

    public static PrintScope AllProducts { get; } = new(PrintScopeKind.AllProducts, "All products", "All Products");

    /// <summary>A folder of the list: "Production" for a stage's, "Breads · Production" for a category's, or "Uncategorized · Production".</summary>
    public static PrintScope Folder(ProductFolderNode folder)
    {
        var title = folder.IsStage ? folder.Title : $"{folder.Title} · {ProductFolderNode.StageTitle(folder.Stage)}";
        return new(PrintScopeKind.Folder, title, title, folder.Stage, folder.CategoryId, folder.IsStage);
    }

    /// <summary>
    /// <paramref name="product"/> is in the set: any product for All products; for a folder, its stage's, and in a
    /// category's folder that category's, in Uncategorized a product with none.
    /// </summary>
    public bool Holds(Product product) => Kind switch
    {
        PrintScopeKind.AllProducts => true,
        PrintScopeKind.Folder => product.Stage == Stage && (IsStage || product.CategoryId == CategoryId),
        _ => false,
    };

    /// <summary>Its label, which a screen reader reads for the dropdown's choice.</summary>
    public override string ToString() => Label;
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

        LoadCommand = ReactiveCommand.CreateFromTask(PrepareAsync);

        this.WhenAnyValue(x => x.SelectedScope)
            .Skip(1)
            .Where(_ => !_isPicking)
            .Subscribe(_ => LoadCommand.Execute().Subscribe());
    }

    /// <summary>The choices the tab offers: This product, the folder a menu asked for (while there is one), All products.</summary>
    public ObservableCollection<PrintScope> Scopes { get; } = [PrintScope.ThisProduct, PrintScope.AllProducts];

    private PrintScope _selectedScope = PrintScope.ThisProduct;
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
    /// <summary>While a set is read: "Preparing 12 of 30…".</summary>
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
        if (scope.Kind == PrintScopeKind.Folder && !Scopes.Contains(scope))
        {
            foreach (var folder in Scopes.Where(s => s.Kind == PrintScopeKind.Folder).ToList())
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
        previous?.Cancel();

        try
        {
            if (SelectedScope.Kind == PrintScopeKind.ThisProduct)
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
        catch (SqlException) when (preparing.IsCancellationRequested)
        {
            // The same, cancelled in the middle of a read: SqlClient reports that as "Operation cancelled by user".
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
        var (printout, problem) = _products.PreparePrintout();
        Note = problem is null
            ? null
            : $"These pages are the product as it was: the open sub-assembly form on Products can't be applied yet. {problem}";
        if (printout is not null)
        {
            PrintSet = ProductPrintSet.Of(printout);
        }
    }

    private async Task PrepareSetAsync(PrintScope scope, CancellationToken cancellationToken)
    {
        Progress = "Preparing…";
        var products = ProductTree.InListOrder((await _productRepository.GetAllAsync(cancellationToken)).Where(scope.Holds));
        var printedAt = DateTime.Now;

        List<ProductPrintout> printouts = [];
        for (var i = 0; i < products.Count; i++)
        {
            Progress = $"Preparing {i + 1} of {products.Count}…";
            printouts.Add((await _savedProducts.ReadAsync(products[i], cancellationToken)).ToPrintout(printedAt));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The Products screen's product prints as it's saved; its edits there don't reach a set.
        Note = _products.History.IsDirty && _products.SelectedProduct is { } open && products.Any(p => p.Id == open.Id)
            ? $"{open.Name} has unsaved changes on Products; it's printed as saved."
            : null;
        PrintSet = new ProductPrintSet(scope.Title, printouts, printedAt);
    }
}
