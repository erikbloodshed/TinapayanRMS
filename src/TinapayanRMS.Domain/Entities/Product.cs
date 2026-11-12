using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>A short description shown under the product's name, e.g. "Soft bread with a creamy filling". Optional.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// What kind of product it is (Breads, Cakes, …): the Products list's category filter, the Wastage % its category may
    /// set, and the category on a printout. Optional: null for an uncategorized product. It isn't a folder (FolderId).
    /// </summary>
    public int? CategoryId { get; set; }

    public ProductCategory? Category { get; set; }

    /// <summary>
    /// Whether it's under development or in production: the Products list's top-level folder for it. A new product
    /// starts in the stage Settings gives it (CostingDefaults.NewProductStage), a clone or variant in Innovation;
    /// dragging it to another folder in the list moves it (IProductRepository.MoveAsync).
    /// </summary>
    public ProductStage Stage { get; set; }

    /// <summary>
    /// The Products list's folder it's filed in (ProductFolder), in its stage; null for one loose at the top of its stage.
    /// Only a move changes a saved product's (IProductRepository.MoveAsync), as only a move changes its stage.
    /// </summary>
    public int? FolderId { get; set; }

    /// <summary>
    /// Made of other products (<see cref="ProductComponent"/>, at least two different ones) and its own packaging, and
    /// sub-assemblies only in product-owned roles (a topping, a filling): a Pasalubong Box of 6 Ensaymada and 4 Cheese Rolls. Each product in it brings its recipe cost,
    /// and its packaging only with <see cref="IncludesComponentPackaging"/>. Picked when the product is made and fixed once
    /// it's saved; a composite is always one sellable unit (<see cref="QuantityRequired"/> 1).
    /// </summary>
    public bool IsComposite { get; set; }

    /// <summary>
    /// A composite's products each bring their own packaging too, beside the composite's: the wrapper each Ensaymada is
    /// sold in, for a box that holds them wrapped. It joins the composite's packaging cost, so labor, overhead and
    /// administrative don't go on it. Only a composite can have it (CK_Products_ComponentPackagingOnComposite).
    /// </summary>
    public bool IncludesComponentPackaging { get; set; }

    /// <summary>
    /// How many sellable units one bulk pack holds (6 for a box of 6): its bulk packaging lines (PackagingLine.IsBulk) cost
    /// each unit their total over this (ProductRules.CalculateBulkPackagingCost). A whole number, 1 or more, set while it
    /// has bulk packaging and null otherwise (ProductRules.EnsureValidBulkPackaging, CK_Products_BulkPackagingQuantity).
    /// </summary>
    public int? BulkPackagingQuantity { get; set; }

    /// <summary>
    /// How many finished pieces make one sellable unit: 1 for a product sold by the piece, 8 for a "Box of 8".
    /// The recipe is authored per piece whatever this is; the costing multiplies by it, and the packaging lines
    /// are for one whole sellable unit. Always at least 1 (CK_Products_QuantityRequired).
    /// </summary>
    public int QuantityRequired { get; set; } = 1;

    /// <summary>
    /// What the shop charges for one sellable unit now, with VAT, as entered: set beside the computed selling
    /// price to see the markup it actually earns. Optional: null when not entered.
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// When the product was last saved, in local time: set each time it's added or updated, and for a
    /// clone when it's made. Null for a product not saved since this was added.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When it was deleted, in local time; null while it's in use. A deleted product waits in the Products list's Deleted
    /// folder, whole, to be restored or deleted permanently: it keeps its stage, category, recipe, packaging and costing,
    /// and still holds what it uses (IProductRepository.DeleteAsync). Nothing else lists it.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;
}
