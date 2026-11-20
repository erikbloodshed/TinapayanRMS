using System.Collections.ObjectModel;
using DynamicData;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>What a composite's products draft (<see cref="ProductComponentsDraft"/>) asks of its form: the cost basis what each product brings is priced at.</summary>
public interface IProductComponentsDraftHost
{
    /// <summary>Which of the materials' costs the screen shows, buffered or not: what each product brings is priced at it.</summary>
    CostBasis CostBasis { get; }
}

/// <summary>
/// A composite's products as its form edits them (ADR 0002): its lines, each a product and how many of it go in, whether it
/// takes its products' packaging too, what the product cell may offer (never the composite itself, nor one holding it), and
/// what each product brings, as read. It holds the rules that follow an edit (a line's Cost following the checkbox, the
/// add row's too), its undo share (<see cref="Capture"/>, <see cref="Restore"/>), and a load's re-pointing at fresh products
/// and costs (<see cref="TakeFresh"/>, which changes nothing typed, ADR 0007). It reads nothing: a line wanting a cost not
/// read yet says so (<see cref="CostWanted"/>), and the Products tab (<see cref="ProductComponents"/>) reads it and hands
/// it over (<see cref="TakeCosts"/>), as it has Remove Line, the row picked and the checkbox's undo step.
/// </summary>
public sealed class ProductComponentsDraft : ObservableState, IDisposable
{
    private readonly IProductComponentsDraftHost _host;
    private readonly IReadOnlyList<Product> _products;
    private readonly IDisposable _rowChanges;

    /// <summary>
    /// What one of each product in the table brings, by product id: its recipe and packaging costs as saved, read with the
    /// composite, when one is picked, and again with every LoadCommand. Kept for the product showing only.
    /// </summary>
    private readonly Dictionary<int, ComponentCosts> _costs = [];

    /// <summary>Every composite's lines as saved, without their products: which composites hold which products, for the picker.</summary>
    private IReadOnlyList<ProductComponent> _links = [];

    private int _generation;

    private ProductComponentRow? _addingRow;

    /// <param name="host">The form: the cost basis what each product brings is priced at.</param>
    /// <param name="products">Every product in use, as the screen holds them now: what a line's product cell finds and offers.</param>
    public ProductComponentsDraft(IProductComponentsDraftHost host, IReadOnlyList<Product> products)
    {
        _host = host;
        _products = products;

        // Whatever it holds changing, its fields, its lines or a line typed into: what the form's dirty check follows.
        PropertyChanged += (_, _) => Changed?.Invoke();
        _rowChanges = RowChanges.OnAnyChange(ComponentEdits, () => Changed?.Invoke());
    }

    /// <summary>Anything the draft holds changed: a field, a line, or a line typed into. What the form's dirty check follows.</summary>
    public event Action? Changed;

    /// <summary>
    /// A line, or the add row, wants what a product brings, which hasn't been read yet: the tab reads it and hands it over
    /// (<see cref="TakeCosts"/>), and the line is priced then.
    /// </summary>
    public event Action<int>? CostWanted;

    /// <summary>A composite's lines, typed straight into its Products table: each a product and how many of it go in.</summary>
    public ObservableCollection<ProductComponentRow> ComponentEdits { get; } = [];

    /// <summary>
    /// The line the table's add row edits: it finds its product, and its recipe cost once that's read, before it's
    /// committed. Kept, so a cost read for it can price it while it's still being typed.
    /// </summary>
    public ProductComponentRow NewRow() => _addingRow = NewRowFor(null, null);

    private bool _includesPackaging;
    /// <summary>
    /// A composite's products each bring their own packaging too (Product.IncludesComponentPackaging), the checkbox beside
    /// its Products table's Remove Line: it joins the composite's packaging, not its recipe cost.
    /// </summary>
    public bool IncludesPackaging
    {
        get => _includesPackaging;
        set
        {
            // Each line's Cost, and the add row's, take the products' packaging in or leave it out: first, since the change
            // recomputes the costs from the rows as it's raised (the form follows it).
            foreach (var row in _addingRow is { } adding ? ComponentEdits.Append(adding) : ComponentEdits)
            {
                row.IncludesPackaging = value;
            }

            if (RaiseAndSetIfChanged(ref _includesPackaging, value))
            {
                IncludesPackagingChanged?.Invoke();
            }

            RaisePropertyChanged(nameof(Caption));
        }
    }

    /// <summary>
    /// The products' packaging joined the composite's own, or left it: its costs follow, and the header's Material Cost
    /// tooltip says so too, as the Costing part works the captions out with the costs (ProductCosting.Costs).
    /// </summary>
    public event Action? IncludesPackagingChanged;

    /// <summary>The Products tab's caption: what each line brings, and where the products' own packaging goes.</summary>
    public string Caption => IncludesPackaging
        ? "Each product brings its recipe (ingredients) cost and its own packaging for how many of it go in: Cost is both. On the Costing tab the packaging joins this product's Packaging. A topping or filling over the whole goes on a tab of its own, with Add Sub-Assembly."
        : "Each product brings its recipe (ingredients) cost for how many of it go in. Its own packaging stays out: this product's Packaging covers the box and anything wrapping the pieces. A topping or filling over the whole goes on a tab of its own, with Add Sub-Assembly.";

    /// <summary>
    /// What the product cell offers: every product but the one showing and the composites that hold it, directly or
    /// through another, which would end up holding themselves (CompositeProductRules.Contains).
    /// </summary>
    public ObservableCollection<Product> ComponentProductOptions { get; } = [];

    /// <summary>
    /// What the products bring together, for the Products tab: the sum of its Cost column, so the composite's recipe cost
    /// less its own sections, and its products' packaging too while it includes theirs. A line still being filled in
    /// brings nothing yet.
    /// </summary>
    public decimal ComponentsCost => CostedLines.Sum(r => r.Cost);

    /// <summary>The lines that bring a cost: a product picked, and a quantity of it.</summary>
    public IEnumerable<ProductComponentRow> CostedLines => ComponentEdits.Where(r => r.Product is not null && r.Quantity > 0);

    /// <summary>The products the lines hold, each once.</summary>
    public IReadOnlyList<int> ProductIds => [.. ComponentEdits.Select(r => r.ComponentProductId).OfType<int>().Distinct()];

    /// <summary>
    /// Which load the costs held came with, counted up by each (<see cref="TakeFresh"/>) and each composite cleared: a read
    /// the tab starts takes it, and hands it back with what it read (<see cref="TakeCosts"/>), which a load since makes stale.
    /// </summary>
    public int Generation => _generation;

    /// <summary>The products the lines hold whose costs haven't been read: what the tab reads after a pick or an undo.</summary>
    public IReadOnlyList<int> MissingCosts => [.. ProductIds.Where(id => !_costs.ContainsKey(id))];

    /// <summary>
    /// The composite's lines as saved, each finding its product and its costs as read with the composite; what Save
    /// compares the recipe against is the form's to settle after this.
    /// </summary>
    public void Load(IReadOnlyList<SavedComponent> components)
    {
        foreach (var component in components)
        {
            _costs[component.Component.ComponentProductId] = component.Costs;
        }

        ComponentEdits.Clear();
        ComponentEdits.AddRange(components.Select(c => NewRowFor(c.Component.ComponentProductId, c.Component.Quantity)));

        // A product the list doesn't hold yet shows as it was read with the composite.
        var products = ProductsById();
        foreach (var component in components)
        {
            products.TryAdd(component.Component.ComponentProductId, component.Component.ComponentProduct!);
        }

        ProductComponentRow.Resolve(ComponentEdits, products, CostsAtBasis());
    }

    /// <summary>No lines, and no costs read: a composite's are that composite's.</summary>
    public void Clear()
    {
        ComponentEdits.Clear();
        _costs.Clear();
        _generation++;
    }

    /// <summary>The lines as an undo step keeps them.</summary>
    public IReadOnlyList<ComponentLineSnapshot> Capture() =>
        [.. ComponentEdits.Select(r => new ComponentLineSnapshot(r.ComponentProductId, r.Quantity))];

    /// <summary>
    /// The lines an undo step kept, each finding its product and the recipe cost read for it again (a cost not read yet is
    /// wanted now, and prices the line when it arrives).
    /// </summary>
    public void Restore(IEnumerable<ComponentLineSnapshot> lines)
    {
        ComponentEdits.Clear();
        foreach (var line in lines)
        {
            ComponentEdits.Add(NewRowFor(line.ProductId, line.Quantity));
        }
    }

    /// <summary>
    /// What a LoadCommand read again: the products' fresh recipe costs, and every composite's lines as saved, for the
    /// picker (<see cref="RefreshOptions"/> follows). The lines may hold unsaved edits, so they stay as they are, re-pointed
    /// at the fresh products and costs. A cost held for a product no line holds now goes: kept, it would price that product
    /// as it was before the load if it's picked again, so it's read again then instead (<see cref="CostWanted"/>). One a line
    /// holds stays until a fresh one comes, as a line added while the load read was read for then.
    /// </summary>
    public void TakeFresh(IReadOnlyDictionary<int, ComponentCosts> costs, IReadOnlyList<ProductComponent> links)
    {
        _links = links;
        _generation++;

        // The lines' products alone: the add row is the last one made, typed in or committed and taken out since. One still
        // being typed keeps the cost it has, and wants it read again as it's next priced (Reprice).
        var held = ProductIds.ToHashSet();
        foreach (var stale in _costs.Keys.Where(id => !held.Contains(id)).ToList())
        {
            _costs.Remove(stale);
        }

        foreach (var (productId, cost) in costs)
        {
            _costs[productId] = cost;
        }

        ResolveRows();
    }

    /// <summary>
    /// What products bring, read since they were wanted while the costs held came with <paramref name="generation"/>: the
    /// lines, and the add row, priced with them. A read a load overtook (<see cref="Generation"/> moved on) is let go, as
    /// the load brought fresher costs, or dropped the product's: false then, and what's still missing is the tab's to read
    /// again.
    /// </summary>
    public bool TakeCosts(IReadOnlyDictionary<int, ComponentCosts> costs, int generation)
    {
        if (generation != _generation)
        {
            return false;
        }

        foreach (var (productId, cost) in costs)
        {
            _costs[productId] = cost;
        }

        Reprice();
        return true;
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

    /// <summary>The lines' products and costs at the form's basis, as it holds them now.</summary>
    public void ResolveRows() => ProductComponentRow.Resolve(ComponentEdits, ProductsById(), CostsAtBasis());

    /// <summary>The lines, and the add row still being typed into, priced again: a cost arrived, or the basis changed.</summary>
    public void Reprice()
    {
        var products = ProductsById();
        ProductComponentRow.Resolve(ComponentEdits, products, CostsAtBasis());

        // The add row, unless its product's cost went with a load: it keeps the one it shows, and wants it read again, as
        // nothing else would read it before it's committed.
        if (_addingRow is { } adding)
        {
            if (adding.ComponentProductId is { } id && !_costs.ContainsKey(id))
            {
                CostWanted?.Invoke(id);
            }
            else
            {
                ProductComponentRow.Resolve([adding], products, CostsAtBasis());
            }
        }
    }

    /// <summary>A product as the screen's list holds it.</summary>
    private Product? FindProduct(int id) => _products.FirstOrDefault(p => p.Id == id);

    /// <summary>The screen's products by id, as it holds them now.</summary>
    private Dictionary<int, Product> ProductsById() => _products.ToDictionary(p => p.Id);

    /// <summary>
    /// What one of a product brings, as read; null while it hasn't been, and it's wanted, so the add row prices itself once
    /// it arrives, before the line is committed.
    /// </summary>
    private ProductComponentRow.UnitCosts? FindCosts(int id)
    {
        if (_costs.TryGetValue(id, out var costs))
        {
            return AtBasis(costs);
        }

        CostWanted?.Invoke(id);
        return null;
    }

    /// <summary>What each product read so far brings at the form's costs, buffered or not.</summary>
    private Dictionary<int, ProductComponentRow.UnitCosts> CostsAtBasis() => _costs.ToDictionary(c => c.Key, c => AtBasis(c.Value));

    private ProductComponentRow.UnitCosts AtBasis(ComponentCosts costs) => new(costs.RecipeAt(_host.CostBasis), costs.PackagingAt(_host.CostBasis));

    /// <summary>A row for the table, finding its product and costs, its Cost following the checkbox as it stands.</summary>
    private ProductComponentRow NewRowFor(int? productId, int? quantity) =>
        new(FindProduct, FindCosts)
        {
            IncludesPackaging = IncludesPackaging,
            ComponentProductId = productId,
            Quantity = quantity,
        };

    public void Dispose() => _rowChanges.Dispose();
}
