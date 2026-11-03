namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One product in a composite product (<see cref="Product.IsComposite"/>), and how many of it go in: 6 Ensaymada in a
/// Pasalubong Box. The quantity counts the component's sellable units, so 1 of "Ensaymada Box of 8" is the whole box.
/// The component brings its recipe (ingredient) cost, worked out on read like every product cost. Its own packaging stays
/// out, since the composite's packaging covers the box and anything wrapping the pieces, unless the composite includes its
/// products' packaging (Product.IncludesComponentPackaging), which then joins the composite's. A component can itself be a
/// composite, as long as no composite ends up containing itself.
/// </summary>
public sealed class ProductComponent
{
    public int Id { get; set; }

    /// <summary>The composite this line is in.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The product that goes in: a single product, a variant, or another composite.</summary>
    public int ComponentProductId { get; set; }

    public Product? ComponentProduct { get; set; }

    /// <summary>How many of the component's sellable units go in: a whole number, at least 1.</summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Where the line sits in its composite's Products table, from 0: the user orders the lines by dragging them, and every
    /// save writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }
}
