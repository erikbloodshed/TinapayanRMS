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

    /// <summary>Looks up a product's recipe cost by id, as the screen last read it; null while it hasn't been.</summary>
    private readonly Func<int, decimal?>? _findRecipeCost;

    public ProductComponentRow()
    {
    }

    /// <summary>
    /// A row that finds its product, and the product's recipe cost if the screen has read it, as soon as one is picked.
    /// The grid's add row edits one of these (controls:GridAddNewRow.ItemFactory): until it's committed it isn't in the
    /// screen's rows, so the screen can't resolve it.
    /// </summary>
    public ProductComponentRow(Func<int, Product?> findProduct, Func<int, decimal?> findRecipeCost)
    {
        _findProduct = findProduct;
        _findRecipeCost = findRecipeCost;
    }

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
                RecipeCost = value is { } costId ? _findRecipeCost!(costId) : null;
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
            this.RaisePropertyChanged(nameof(Cost));
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
            this.RaisePropertyChanged(nameof(Cost));
        }
    }

    /// <summary>What the line brings into the composite's cost: its quantity at the product's recipe cost.</summary>
    public decimal Cost => CompositeProductRules.CalculateComponentCost(RecipeCost ?? 0m, Quantity ?? 0);

    /// <summary>What the Costing tab and the printout call the line: "Ensaymada × 6".</summary>
    public string LineName => LineNameFor(Product?.Name ?? string.Empty, Quantity ?? 0);

    /// <summary>"Ensaymada × 6": a product in a composite, and how many of it go in.</summary>
    public static string LineNameFor(string productName, int quantity) => $"{productName} × {quantity}";

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => ComponentProductId is null && Quantity is null or 0;

    /// <summary>
    /// Points each row at its product in <paramref name="productsById"/> (the screen's current copies, so a renamed
    /// product shows its new name) and at its recipe cost in <paramref name="costsById"/>, or none while it hasn't been
    /// read. A row whose product isn't there keeps the one it has, if it's the same.
    /// </summary>
    public static void Resolve(
        IEnumerable<ProductComponentRow> rows, IReadOnlyDictionary<int, Product> productsById, IReadOnlyDictionary<int, decimal> costsById)
    {
        foreach (var row in rows)
        {
            if (row.ComponentProductId is not { } id)
            {
                row.Product = null;
                row.RecipeCost = null;
                continue;
            }

            row.Product = productsById.GetValueOrDefault(id) ?? (row.Product?.Id == id ? row.Product : null);
            row.RecipeCost = costsById.TryGetValue(id, out var cost) ? cost : null;
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
                throw new InvalidOperationException(row.Quantity is { } count
                    ? $"Pick the product for the line of {count:#,0}, or delete that line."
                    : "Pick a product for every line, or delete the line without one.");
            }

            if (row.Quantity is not > 0)
            {
                throw new InvalidOperationException($"Enter how many {product.Name} go in, or delete its line.");
            }

            if (lines.Any(l => l.Product.Id == product.Id))
            {
                throw new InvalidOperationException($"{product.Name} is on two lines. Put its quantity on one line and delete the other.");
            }

            lines.Add((product, row.Quantity.Value));
        }

        return lines;
    }
}
