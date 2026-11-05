using System.Collections.ObjectModel;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A composite's Products table, in the Products screen's Recipe Structure (ComponentsSectionView, through
/// ProductsViewModel.ComponentsTable): its lines, each a product and how many of it go in, the add row,
/// what the product cell offers, and what each product brings, read off the UI thread once per product. The screen keeps
/// whether the composite takes its products' packaging (a product field and an undo step), the undo history these lines
/// are part of, and the costs they add up to.
/// </summary>
/// <param name="findProduct">A product as the screen's list holds it.</param>
/// <param name="productsById">The screen's products by id, as it holds them now.</param>
/// <param name="includesPackaging">Whether the composite takes its products' packaging, which a new row's Cost follows.</param>
/// <param name="showFailure">Shows a failure on the screen, for a cost read nothing awaits.</param>
/// <param name="work">The screen's work nothing awaits, which counts the cost reads (ViewModelBase.WhenIdleAsync).</param>
public sealed class ProductComponents(
    SavedProductReader savedProducts,
    Func<int, Product?> findProduct,
    Func<IReadOnlyDictionary<int, Product>> productsById,
    Func<CostBasis> costBasis,
    Func<bool> includesPackaging,
    Action<Exception, string> showFailure,
    BackgroundWork work) : ReactiveObject
{
    /// <summary>
    /// What one of each product in the table brings, by product id: its recipe and packaging costs as saved, read with the
    /// composite, when one is picked, and again with every LoadCommand. Kept for the product showing only.
    /// </summary>
    private readonly Dictionary<int, ComponentCosts> _costs = [];

    /// <summary>The products whose recipe cost is being read, so a second pick of one doesn't read it twice.</summary>
    private readonly HashSet<int> _fetching = [];

    /// <summary>Every composite's lines as saved, without their products: which composites hold which products, for the picker.</summary>
    private IReadOnlyList<ProductComponent> _links = [];

    private ProductComponentRow? _addingRow;

    /// <summary>A composite's lines, typed straight into its Products table: each a product and how many of it go in.</summary>
    public ObservableCollection<ProductComponentRow> ComponentEdits { get; } = [];

    /// <summary>
    /// The line the table's add row edits: it finds its product, and its recipe cost once that's read, before it's
    /// committed. Kept, so a cost read for it can price it while it's still being typed.
    /// </summary>
    public Func<ProductComponentRow> NewComponentRow => () => _addingRow = NewRowFor(null, null);

    private ProductComponentRow? _selectedComponentEdit;
    public ProductComponentRow? SelectedComponentEdit
    {
        get => _selectedComponentEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedComponentEdit, value);
    }

    /// <summary>
    /// What the product cell offers: every product but the one showing and the composites that hold it, directly or
    /// through another, which would end up holding themselves (CompositeProductRules.Contains).
    /// </summary>
    public ObservableCollection<Product> ComponentProductOptions { get; } = [];

    private decimal _componentsCost;
    /// <summary>
    /// What the products bring together, for the Products tab: the sum of its Cost column, so the composite's recipe cost
    /// less its own sections, and its products' packaging too while it includes theirs. The screen sets it as it works out
    /// the costs.
    /// </summary>
    public decimal ComponentsCost
    {
        get => _componentsCost;
        internal set => this.RaiseAndSetIfChanged(ref _componentsCost, value);
    }

    /// <summary>The products the lines hold, each once.</summary>
    public IReadOnlyList<int> ProductIds => [.. ComponentEdits.Select(r => r.ComponentProductId).OfType<int>().Distinct()];

    /// <summary>
    /// The composite's lines as saved, each finding its product and its costs as read with the composite; what Save
    /// compares the recipe against is the screen's to settle after this.
    /// </summary>
    public void Load(IReadOnlyList<SavedComponent> components)
    {
        foreach (var component in components)
        {
            _costs[component.Component.ComponentProductId] = component.Costs;
        }

        SelectedComponentEdit = null;
        ComponentEdits.Clear();
        ComponentEdits.AddRange(components.Select(c => NewRowFor(c.Component.ComponentProductId, c.Component.Quantity)));

        // A product the list doesn't hold yet shows as it was read with the composite.
        var products = new Dictionary<int, Product>(productsById());
        foreach (var component in components)
        {
            products.TryAdd(component.Component.ComponentProductId, component.Component.ComponentProduct!);
        }

        ProductComponentRow.Resolve(ComponentEdits, products, CostsAtBasis());
    }

    /// <summary>No lines, and no costs read: a composite's are that composite's.</summary>
    public void Clear()
    {
        SelectedComponentEdit = null;
        ComponentEdits.Clear();
        _costs.Clear();
    }

    /// <summary>The lines as an undo step keeps them.</summary>
    public IReadOnlyList<ComponentLineState> Capture() =>
        [.. ComponentEdits.Select(r => new ComponentLineState(r.ComponentProductId, r.Quantity))];

    /// <summary>
    /// The lines an undo step kept, each finding its product and the recipe cost read for it again (a cost not read yet is
    /// read now, and prices the line when it arrives).
    /// </summary>
    public void Restore(IEnumerable<ComponentLineState> lines)
    {
        ComponentEdits.Clear();
        foreach (var line in lines)
        {
            ComponentEdits.Add(NewRowFor(line.ProductId, line.Quantity));
        }
    }

    /// <summary>
    /// What a LoadCommand read again: the products' fresh recipe costs, and every composite's lines as saved, for the
    /// picker (<see cref="RefreshOptions"/> follows). The lines may hold unsaved edits, so they stay as they are.
    /// </summary>
    public void TakeFresh(IReadOnlyDictionary<int, ComponentCosts> costs, IReadOnlyList<ProductComponent> links)
    {
        _links = links;
        foreach (var (productId, cost) in costs)
        {
            _costs[productId] = cost;
        }

        ResolveRows();
    }

    /// <summary>
    /// What the product cell offers: every product but <paramref name="productId"/> and the composites holding it,
    /// directly or through another, as saved. A new composite (0) can hold any: nothing holds it yet.
    /// </summary>
    public void RefreshOptions(int productId, IEnumerable<Product> products)
    {
        var held = _links
            .GroupBy(c => c.ProductId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(c => c.ComponentProductId)]);
        ComponentProductOptions.MergeFrom(
            [.. products.Where(p => productId == 0 || (p.Id != productId && !CompositeProductRules.Contains(p.Id, productId, held)))],
            p => p.Id);
    }

    /// <summary>The lines' products and costs at the screen's basis, as it holds them now.</summary>
    public void ResolveRows() => ProductComponentRow.Resolve(ComponentEdits, productsById(), CostsAtBasis());

    /// <summary>The lines, and the add row still being typed into, priced again: a cost arrived, or the basis changed.</summary>
    public void Reprice()
    {
        var products = productsById();
        ProductComponentRow.Resolve(ComponentEdits, products, CostsAtBasis());
        if (_addingRow is { } adding)
        {
            ProductComponentRow.Resolve([adding], products, CostsAtBasis());
        }
    }

    /// <summary>Each line's Cost, and the add row's, takes the products' packaging in or leaves it out.</summary>
    public void SetIncludesPackaging(bool includes)
    {
        foreach (var row in _addingRow is { } adding ? ComponentEdits.Append(adding) : ComponentEdits)
        {
            row.IncludesPackaging = includes;
        }
    }

    /// <summary>
    /// Reads what each product picked brings, when it hasn't been read yet: off the UI thread, once per product however
    /// often it's picked. The line, and the add row still being typed into, are priced when it arrives, and the
    /// composite's costs follow.
    /// </summary>
    public void ReadMissingCosts()
    {
        List<int> missing = [.. ProductIds.Where(id => !_costs.ContainsKey(id) && _fetching.Add(id))];
        if (missing.Count > 0)
        {
            _ = work.RunAsync(() => FetchCostsAsync(missing));
        }
    }

    /// <summary>
    /// What one of a product brings, as read; null while it hasn't been, and the read starts, so the add row prices itself
    /// once it arrives, before the line is committed.
    /// </summary>
    private ProductComponentRow.UnitCosts? FindCosts(int id)
    {
        if (_costs.TryGetValue(id, out var costs))
        {
            return AtBasis(costs);
        }

        if (_fetching.Add(id))
        {
            _ = work.RunAsync(() => FetchCostsAsync([id]));
        }

        return null;
    }

    private async Task FetchCostsAsync(IReadOnlyList<int> productIds)
    {
        try
        {
            var costs = await Task.Run(() => savedProducts.ReadComponentCostsAsync(productIds, CancellationToken.None));
            foreach (var (productId, cost) in costs)
            {
                _costs[productId] = cost;
            }
        }
        catch (Exception ex)
        {
            // Nothing awaits this read, so what went wrong is shown rather than lost. The line stays unpriced, and the
            // next LoadCommand reads it again.
            showFailure(ex, "Couldn't read what a product in the Products table costs");
        }
        finally
        {
            foreach (var productId in productIds)
            {
                _fetching.Remove(productId);
            }
        }

        Reprice();
    }

    /// <summary>What each product read so far brings at the screen's costs, buffered or not.</summary>
    private Dictionary<int, ProductComponentRow.UnitCosts> CostsAtBasis() => _costs.ToDictionary(c => c.Key, c => AtBasis(c.Value));

    private ProductComponentRow.UnitCosts AtBasis(ComponentCosts costs) => new(costs.RecipeAt(costBasis()), costs.PackagingAt(costBasis()));

    /// <summary>A row for the table, finding its product and costs, its Cost following the checkbox as it stands.</summary>
    private ProductComponentRow NewRowFor(int? productId, int? quantity) =>
        new(findProduct, FindCosts)
        {
            IncludesPackaging = includesPackaging(),
            ComponentProductId = productId,
            Quantity = quantity,
        };
}
