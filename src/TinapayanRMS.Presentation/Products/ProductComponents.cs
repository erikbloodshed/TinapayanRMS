using System.Collections.ObjectModel;
using System.Collections.Specialized;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What a composite's Products table (<see cref="ProductComponents"/>) asks of the Products screen: its runners, messages
/// and undo steps. The cost basis what each product brings is priced at is what its draft asks
/// (<see cref="IProductComponentsDraftHost"/>). The screen implements it explicitly.
/// </summary>
public interface IProductComponentsHost : IScreenPartHost, IUndoRecorder;

/// <summary>
/// A composite's Products table, in the Products screen's Recipe Structure (ComponentsSection, through
/// ProductForm.ComponentsTable): Remove Line and the checkbox, each change one undo step of the screen's form
/// (<see cref="IProductComponentsHost"/>), the line picked, and what each product brings, read off the UI thread once per
/// product as its draft wants it. The lines themselves, the checkbox's state, what the product cell offers and what the
/// lines cost are its draft's (<see cref="Draft"/>), whose fields the tab passes on under the names the page binds; the
/// screen keeps the undo history these lines are part of, and the costs they add up to.
/// </summary>
public sealed class ProductComponents : ObservableState, IDisposable
{
    private readonly IProductComponentsHost _host;
    private readonly IProductQueries _queries;

    /// <summary>The products whose recipe cost is being read, so a second pick of one doesn't read it twice.</summary>
    private readonly HashSet<int> _fetching = [];

    /// <param name="host">The Products screen: its runners and messages, and its undo steps.</param>
    /// <param name="draft">The composite's products the tab edits, which the form loads and saves.</param>
    /// <param name="queries">What reads what each product brings to a composite (its recipe and packaging costs as saved).</param>
    public ProductComponents(IProductComponentsHost host, ProductComponentsDraft draft, IProductQueries queries)
    {
        _host = host;
        _queries = queries;
        Draft = draft;

        // The draft's fields are the tab's to the page, under the same names; a line picked that leaves the table, however
        // it went (removed, the composite shown afresh, an undo), is picked no longer; a cost a line wants is read.
        draft.PropertyChanged += DraftChanged;
        draft.ComponentEdits.CollectionChanged += LinesChanged;
        draft.CostWanted += ReadCost;

        RemoveLineCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => draft.ComponentEdits.Remove(SelectedComponentEdit!)),
            () => SelectedComponentEdit is not null);
    }

    /// <summary>The composite's products the tab edits: its lines, the checkbox, what the product cell offers, their costs.</summary>
    public ProductComponentsDraft Draft { get; }

    /// <summary>The Products table's Remove Line: takes out the line picked, as one undo step.</summary>
    public ScreenCommand RemoveLineCommand { get; }

    /// <summary>A composite's lines, typed straight into its Products table (the draft's).</summary>
    public ObservableCollection<ProductComponentRow> ComponentEdits => Draft.ComponentEdits;

    /// <summary>The line the table's add row edits: it finds its product, and its recipe cost once that's read (the draft's).</summary>
    public Func<ProductComponentRow> NewComponentRow => Draft.NewRow;

    private ProductComponentRow? _selectedComponentEdit;
    public ProductComponentRow? SelectedComponentEdit
    {
        get => _selectedComponentEdit;
        set => RaiseAndSetIfChanged(ref _selectedComponentEdit, value);
    }

    /// <summary>A composite's products each bring their own packaging too (the draft's).</summary>
    public bool IncludesPackaging
    {
        get => Draft.IncludesPackaging;
        set => Draft.IncludesPackaging = value;
    }

    /// <summary>
    /// The Include each product's packaging checkbox's side of <see cref="IncludesPackaging"/>: a tick or an untick from the
    /// screen is one undo step, whether it came by mouse, key or UI Automation. A step around the box's focus couldn't give
    /// that, since the box keeps the cursor from one click to the next.
    /// </summary>
    public bool IsPackagingTicked
    {
        get => Draft.IncludesPackaging;
        set
        {
            if (value != Draft.IncludesPackaging)
            {
                _host.AsUndoStep(() => Draft.IncludesPackaging = value);
            }
        }
    }

    /// <summary>The Products tab's caption: what each line brings, and where the products' own packaging goes (the draft's).</summary>
    public string Caption => Draft.Caption;

    /// <summary>What the product cell offers (the draft's).</summary>
    public ObservableCollection<Product> ComponentProductOptions => Draft.ComponentProductOptions;

    /// <summary>What the products bring together, for the Products tab: the sum of its Cost column (the draft's).</summary>
    public decimal ComponentsCost => Draft.ComponentsCost;

    /// <summary>
    /// Reads what each product picked brings, when it hasn't been read yet: off the UI thread, once per product however
    /// often it's picked. The line, and the add row still being typed into, are priced when it arrives, and the
    /// composite's costs follow.
    /// </summary>
    public void ReadMissingCosts()
    {
        List<int> missing = [.. Draft.MissingCosts.Where(_fetching.Add)];
        if (missing.Count > 0)
        {
            _ = _host.Work.RunAsync(() => FetchCostsAsync(missing));
        }
    }

    /// <summary>A line, or the add row, wants what <paramref name="productId"/> brings: it's read, once however often it's wanted.</summary>
    private void ReadCost(int productId)
    {
        if (_fetching.Add(productId))
        {
            _ = _host.Work.RunAsync(() => FetchCostsAsync([productId]));
        }
    }

    private async Task FetchCostsAsync(IReadOnlyList<int> productIds)
    {
        // The load the costs held came with: one since makes what's read here stale (TakeCosts).
        var generation = Draft.Generation;
        IReadOnlyDictionary<int, ComponentCosts> costs = new Dictionary<int, ComponentCosts>();
        try
        {
            costs = await Task.Run(() => _queries.ReadComponentCostsAsync(productIds, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // Nothing awaits this read, so what went wrong is shown rather than lost. The line stays unpriced, and the
            // next LoadCommand reads it again.
            _host.ShowFailure(ex, "Couldn't read what a product in the Products table costs");
        }
        finally
        {
            foreach (var productId in productIds)
            {
                _fetching.Remove(productId);
            }
        }

        if (!Draft.TakeCosts(costs, generation))
        {
            // A load came while it read: what the lines still hold and the load didn't bring is read again.
            ReadMissingCosts();
        }
    }

    private void DraftChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(e.PropertyName);
        if (e.PropertyName == nameof(ProductComponentsDraft.IncludesPackaging))
        {
            RaisePropertyChanged(nameof(IsPackagingTicked));
        }
    }

    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedComponentEdit is { } picked && !Draft.ComponentEdits.Contains(picked))
        {
            SelectedComponentEdit = null;
        }
    }

    public void Dispose()
    {
        Draft.PropertyChanged -= DraftChanged;
        Draft.ComponentEdits.CollectionChanged -= LinesChanged;
        Draft.CostWanted -= ReadCost;
    }
}
