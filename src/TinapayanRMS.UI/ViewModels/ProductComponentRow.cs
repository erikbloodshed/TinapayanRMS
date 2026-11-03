using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One row of a composite product's Products table, typed straight into as the Packaging table is: a product, and how
/// many of its sellable units go in. The grid's product cell sets <see cref="ComponentProductId"/>; the row finds that
/// product and its recipe cost through the screen's lookups, and the screen points it at fresh ones as they're read
/// (<see cref="Resolve"/>), which prices it. A row can be half filled in: <see cref="CompleteLines"/> checks them all
/// before they're saved.
/// </summary>
public sealed class ProductComponentRow : ReactiveObject
{
    /// <summary>Looks up a product by id as the screen has it.</summary>
    private readonly Func<int, Product?>? _findProduct;

    /// <summary>Looks up a product's recipe and packaging costs by id, as the screen last read them; null while it hasn't.</summary>
    private readonly Func<int, UnitCosts?>? _findCosts;

    public ProductComponentRow()
    {
    }

    /// <summary>
    /// A row that finds its product, and the product's costs if the screen has read them, as soon as one is picked.
    /// The grid's add row edits one of these (controls:GridAddNewRow.ItemFactory): until it's committed it isn't in the
    /// screen's rows, so the screen can't resolve it.
    /// </summary>
    public ProductComponentRow(Func<int, Product?> findProduct, Func<int, UnitCosts?> findCosts)
    {
        _findProduct = findProduct;
        _findCosts = findCosts;
    }

    /// <summary>What one sellable unit of a product brings, at the screen's cost basis: its recipe cost and its packaging cost.</summary>
    public readonly record struct UnitCosts(decimal Recipe, decimal Packaging);

    private int? _componentProductId;
    public int? ComponentProductId
    {
        get => _componentProductId;
        set
        {
            this.RaiseAndSetIfChanged(ref _componentProductId, value);
            if (_findProduct is not null)
            {
                Product = value is { } id ? _findProduct(id) : null;
                SetCosts(value is { } costId ? _findCosts!(costId) : null);
            }
        }
    }

    private Product? _product;
    /// <summary>The product <see cref="ComponentProductId"/> names, as the screen last loaded it.</summary>
    public Product? Product
    {
        get => _product;
        private set
        {
            this.RaiseAndSetIfChanged(ref _product, value);
            this.RaisePropertyChanged(nameof(LineName));
        }
    }

    private int? _quantity;
    /// <summary>
    /// How many of the product's sellable units go in: a whole number, 1 or more. For a product sold by the piece that's
    /// pieces; for a Box of 8 it's boxes. Null until it's typed, so the grid's cell is empty rather than 0.
    /// </summary>
    public int? Quantity
    {
        get => _quantity;
        set
        {
            this.RaiseAndSetIfChanged(ref _quantity, value);
            RaiseCostsChanged();
            this.RaisePropertyChanged(nameof(LineName));
        }
    }

    private decimal? _recipeCost;
    /// <summary>
    /// What one of the product brings: its recipe (sub-assemblies, ingredients) cost for one sellable unit, as it's saved,
    /// its packaging left out. Null until the screen has read it.
    /// </summary>
    public decimal? RecipeCost
    {
        get => _recipeCost;
        set
        {
            this.RaiseAndSetIfChanged(ref _recipeCost, value);
            RaiseCostsChanged();
        }
    }

    private decimal? _packagingCost;
    /// <summary>
    /// The product's own packaging for one sellable unit, as it's saved (the Packaging figure on its Costing tab), which
    /// the line brings only while the composite includes its products' packaging. Null until the screen has read it.
    /// </summary>
    public decimal? PackagingCost
    {
        get => _packagingCost;
        set
        {
            this.RaiseAndSetIfChanged(ref _packagingCost, value);
            RaiseCostsChanged();
        }
    }

    private bool _includesPackaging;
    /// <summary>The composite includes its products' packaging (its Products tab's checkbox): <see cref="Cost"/> adds it in.</summary>
    public bool IncludesPackaging
    {
        get => _includesPackaging;
        set
        {
            this.RaiseAndSetIfChanged(ref _includesPackaging, value);
            this.RaisePropertyChanged(nameof(Cost));
        }
    }

    /// <summary>What the line brings into the composite's recipe cost: its quantity at the product's recipe cost.</summary>
    public decimal RecipeCostTotal => CompositeProductRules.CalculateComponentCost(RecipeCost ?? 0m, Quantity ?? 0);

    /// <summary>What the line's products' packaging comes to: its quantity at the product's packaging cost.</summary>
    public decimal PackagingCostTotal => CompositeProductRules.CalculateComponentPackagingCost(PackagingCost ?? 0m, Quantity ?? 0);

    /// <summary>
    /// The line's Cost column: its quantity at the product's recipe cost, and at its packaging cost too while the composite
    /// includes its products' packaging. The two still go to different rows of the costing (recipe, packaging).
    /// </summary>
    public decimal Cost => RecipeCostTotal + (IncludesPackaging ? PackagingCostTotal : 0m);

    private void SetCosts(UnitCosts? costs)
    {
        RecipeCost = costs?.Recipe;
        PackagingCost = costs?.Packaging;
    }

    private void RaiseCostsChanged()
    {
        this.RaisePropertyChanged(nameof(RecipeCostTotal));
        this.RaisePropertyChanged(nameof(PackagingCostTotal));
        this.RaisePropertyChanged(nameof(Cost));
    }

    /// <summary>What the Costing tab and the printout call the line: "Ensaymada × 6".</summary>
    public string LineName => CompositeProductRules.LineName(Product?.Name ?? string.Empty, Quantity ?? 0);

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => ComponentProductId is null && Quantity is null or 0;

    /// <summary>
    /// Points each row at its product in <paramref name="productsById"/> (the screen's current copies, so a renamed
    /// product shows its new name) and at its costs in <paramref name="costsById"/>, or none while they haven't been
    /// read. A row whose product isn't there keeps the one it has, if it's the same.
    /// </summary>
    public static void Resolve(
        IEnumerable<ProductComponentRow> rows, IReadOnlyDictionary<int, Product> productsById, IReadOnlyDictionary<int, UnitCosts> costsById)
    {
        foreach (var row in rows)
        {
            if (row.ComponentProductId is not { } id)
            {
                row.Product = null;
                row.SetCosts(null);
                continue;
            }

            row.Product = productsById.GetValueOrDefault(id) ?? (row.Product?.Id == id ? row.Product : null);
            row.SetCosts(costsById.TryGetValue(id, out var costs) ? costs : null);
        }
    }

    /// <summary>
    /// The rows as lines, leaving out blank ones. A row with only a product or only a quantity is refused, and so is a
    /// product on two rows, each with a message naming it. At least two different products is Save's to check
    /// (CompositeProductRules.EnsureValidComponents), since a composite can be half built between Saves.
    /// </summary>
    public static IReadOnlyList<(Product Product, int Quantity)> CompleteLines(IEnumerable<ProductComponentRow> rows)
    {
        List<(Product Product, int Quantity)> lines = [];
        foreach (var row in rows.Where(r => !r.IsBlank))
        {
            if (row.Product is not { } product)
            {
                throw new BusinessRuleException(row.Quantity is { } count
                    ? $"Pick the product for the line of {count:#,0}, or delete that line."
                    : "Pick a product for every line, or delete the line without one.");
            }

            if (row.Quantity is not > 0)
            {
                throw new BusinessRuleException($"Enter how many {product.Name} go in, or delete its line.");
            }

            if (lines.Any(l => l.Product.Id == product.Id))
            {
                throw new BusinessRuleException($"{product.Name} is on two lines. Put its quantity on one line and delete the other.");
            }

            lines.Add((product, row.Quantity.Value));
        }

        return lines;
    }
}
