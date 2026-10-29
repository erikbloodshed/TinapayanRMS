using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A recipe section whose lines the product's Recipe Structure edits: its role, its gram lines and the product's piece
/// size. In a product-owned role (SubAssemblyRole.IsProductOwned), or marked <see cref="IsOwn"/>, it's the product's own
/// sub-assembly, new or saved (<see cref="Id"/>); otherwise, or marked <see cref="Release"/>, one Save keeps on the
/// Sub-Assemblies screen and links. Either is named after the product and its role (SubAssemblyRules.StandaloneNameFor).
/// </summary>
/// <param name="SectionName">
/// Optional: what tells it apart from others in its role, e.g. "Ube" beside "Cheese" (SubAssemblyRules.EnsureSectionsDistinct),
/// and what's put after the product and role in its saved name.
/// </param>
public sealed record NewSubAssemblyDraft(
    int RoleId,
    IReadOnlyList<RecipeLine> Lines,
    UsageAuthoredField AuthoredField,
    decimal AuthoredValue,
    string? SectionName = null)
{
    /// <summary>
    /// The product's own saved sub-assembly this section is, whose lines, role and name Save rewrites in place; 0 for a
    /// new one.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Save makes a new one the product's own whatever its role: a linked section unlinked in the Products form, or a
    /// clone's copy of its source's own. Otherwise its role decides (SubAssemblyRole.IsProductOwned). A saved one of the
    /// product's own stays its own, unless it moves out of a product-owned role into one that isn't.
    /// </summary>
    public bool IsOwn { get; init; }

    /// <summary>
    /// Save keeps it on the Sub-Assemblies screen and links it, whatever its role: a product's own moved there from the
    /// Products form. A saved one keeps its id, name and lines (as written in this save). Overrides <see cref="IsOwn"/>
    /// and the role.
    /// </summary>
    public bool Release { get; init; }

    /// <summary>
    /// Save moves it to the Sub-Assemblies screen, as <see cref="Release"/> does, and adds a new made ingredient made from
    /// it (Move to In-House, 2026-10-27; until then the lines became a bill of materials of the made ingredient's own). The
    /// product links it at the same piece size, so it costs what it did. Null for any other section.
    /// </summary>
    public MadeIngredientDraft? ToMadeIngredient { get; init; }

    /// <summary>Save keeps it on the Sub-Assemblies screen and links it: moved there (<see cref="Release"/>), or to In-House.</summary>
    public bool MovesToSubAssemblies => Release || ToMadeIngredient is not null;
}

/// <summary>
/// A made ingredient made from a sub-assembly by Move to In-House or Make In-House Ingredient: its name, unique among raw
/// materials, and the Made category it goes in. It has no add-ins and loses no weight in the making (a weight loss of 0%),
/// so it costs per gram what the recipe does; the real loss can be set on the Raw Materials screen afterwards.
/// </summary>
public sealed record MadeIngredientDraft(string Name, int CategoryId);

/// <summary>A sub-assembly the product links, at the product's own piece size.</summary>
public sealed record LinkedUsageDraft(int SubAssemblyId, UsageAuthoredField AuthoredField, decimal AuthoredValue)
{
    /// <summary>
    /// This product's own extra ingredients on top of the linked recipe, in grams, and never one the recipe
    /// already has (ProductRules.EnsureValidAddIns). They go into the batch, so the piece size shares out the
    /// owner's lines plus these. Empty for a usage that takes the recipe as it is.
    /// </summary>
    public IReadOnlyList<(int IngredientId, decimal Grams)> AddIns { get; init; } = [];

    /// <summary>
    /// The linked sub-assembly's new lines, changed in this product's recipe: Save writes them to the sub-assembly
    /// itself, on the Sub-Assemblies screen, so every product linking it follows. Null leaves its lines as they are.
    /// </summary>
    public IReadOnlyList<RecipeLine>? Lines { get; init; }
}

/// <summary>
/// A product's whole recipe of sub-assemblies: the sections whose lines it edits (its own, and new ones), and the ones
/// it links. Saving it replaces the product's usages; one of its own it no longer lists is deleted, and one on the
/// Sub-Assemblies screen is kept. A composite product's recipe is its <see cref="Components"/>, and any sections it has
/// are in product-owned roles (CompositeProductRules.EnsureSectionRoles).
/// </summary>
public sealed record RecipeDraft(IReadOnlyList<NewSubAssemblyDraft> New, IReadOnlyList<LinkedUsageDraft> Linked)
{
    /// <summary>
    /// A composite product's products, in its Products table's order, each with how many of its sellable units go in: at
    /// least two different ones (CompositeProductRules.EnsureValidComponents). Empty for a product made of sub-assemblies,
    /// whose <see cref="New"/> and <see cref="Linked"/> are then its recipe; a composite's are only its toppings, fillings
    /// and the like, in product-owned roles.
    /// </summary>
    public IReadOnlyList<(int ComponentProductId, int Quantity)> Components { get; init; } = [];
}

/// <summary>
/// What a product's packaging links, as Save writes it: another product's packaging, a standalone packaging
/// set (one set per sellable unit), or neither (<see cref="None"/>), when the product owns its packaging lines; or a
/// new set made of its lines (<see cref="NewSet"/>).
/// </summary>
public sealed record PackagingLinkDraft(int? LinkedProductId, int? LinkedSubAssemblyId)
{
    /// <summary>No link: the product's packaging is its own lines.</summary>
    public static PackagingLinkDraft None { get; } = new(null, null);

    /// <summary>
    /// Move to Sub-Assemblies on the Products screen's Packaging tab: the packaging lines saved with it become a new
    /// packaging set on the Sub-Assemblies screen, named after the product and the Packaging role, and the product links
    /// it in their place.
    /// </summary>
    public static PackagingLinkDraft NewSet { get; } = new(null, null) { IsNewSet = true };

    /// <summary>This is <see cref="NewSet"/>: the link is to a set Save makes of the product's lines.</summary>
    public bool IsNewSet { get; init; }
}

/// <summary>
/// A product filed under another folder of the Products list: the stage and category it goes in from now on.
/// <paramref name="CategoryId"/> null leaves it uncategorized.
/// </summary>
public sealed record ProductMove(int ProductId, ProductStage Stage, int? CategoryId);

public interface IProductRepository
{
    /// <summary>Every product in use, by name: none of those in the Deleted folder (GetDeletedAsync).</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The products in the Deleted folder, by name, each as it was when it was deleted.</summary>
    Task<IReadOnlyList<Product>> GetDeletedAsync(CancellationToken cancellationToken);

    /// <summary>The product with this id, deleted or not; null when there's none.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>The products with these ids, deleted or not, by name; an id with no product is left out.</summary>
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>
    /// A composite product's lines, in its Products table's order, each with its product (and that product's category);
    /// none for a product made of sub-assemblies.
    /// </summary>
    Task<IReadOnlyList<ProductComponent>> GetComponentsAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// The Wastage % each of <paramref name="productIds"/> is priced with because its category sets one
    /// (ProductCategory.WastagePercent), by product id. A product without a category, or whose category leaves it to the
    /// shop-wide one, isn't in it.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> GetCategoryWastageForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>GetComponentsAsync for several composites at once, in one query: each one's lines in its Products table's order.</summary>
    Task<IReadOnlyList<ProductComponent>> GetComponentsForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>Every composite's lines, without their products: which product goes into which, for the loop check.</summary>
    Task<IReadOnlyList<ProductComponent>> GetAllComponentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The ids of the products, deleted or not, whose saved recipe or packaging reads through these sub-assemblies, or
    /// through <paramref name="productIds"/>' sections and packaging: those products themselves, every product that uses
    /// one of the sub-assemblies (their own or the products' sections), every product whose packaging links one of them
    /// as a set, and every product whose packaging links one of those products, however many links deep. What a save of
    /// them can change besides itself, for the version history.
    /// </summary>
    Task<IReadOnlyList<int>> GetProductsReadingAsync(
        IReadOnlyCollection<int> subAssemblyIds, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>Adds the product and stamps its UpdatedAt.</summary>
    Task AddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the name, description, category, quantity required and current price, and stamps UpdatedAt. The stage stays
    /// as it is; whether it's a composite can't change (a different IsComposite is refused).
    /// </summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Files each product under another folder of the Products list, all in one save: sets its stage and category,
    /// nothing else, and stamps UpdatedAt. Refused, with nothing moved, if a product or a category doesn't exist.
    /// </summary>
    Task MoveAsync(IReadOnlyCollection<ProductMove> moves, CancellationToken cancellationToken);

    /// <summary>
    /// Sets each product's Current Price, all in one save, and stamps UpdatedAt (2026-10-29, the Price Simulator's Set Current
    /// Prices). Refused, with nothing changed, if a product doesn't exist or a price is negative.
    /// </summary>
    Task SetCurrentPricesAsync(IReadOnlyCollection<(int ProductId, decimal CurrentPrice)> prices, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the product and nothing else, stamping UpdatedAt; its own sub-assemblies, named after it, take the new
    /// name as a Save's rename gives it to them.
    /// </summary>
    Task RenameAsync(int productId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Products screen edits in one transaction, so nothing is saved unless every
    /// part passes: the product itself (added when its Id is 0, otherwise updated as UpdateAsync does),
    /// its recipe, its packaging lines and its costing parameters, each checked as their own repositories
    /// check them. Returns the product's id.
    /// </summary>
    /// <param name="product">
    /// Only its Id, Name, Description, CategoryId, QuantityRequired, CurrentPrice and IsComposite are read, and a new one's
    /// Stage; it's never given an id. A new product goes in the stage it holds (Research and Development unless it's set),
    /// and a saved one keeps its stage. Whether it's a composite is a new one's to say; a saved one's can't change.
    /// </param>
    /// <param name="recipe">
    /// The product's complete recipe of sub-assemblies, or null to leave it as it is. One of its own it no longer lists is
    /// deleted; one from the Sub-Assemblies screen stays there. A composite's recipe is its products (RecipeDraft.Components)
    /// and its sections in product-owned roles; a new composite saved with none is refused, as one with fewer than two
    /// different products is, and so is a section in another role.
    /// </param>
    /// <param name="packagingLink">
    /// What the packaging links from now on, or null to leave the link as it is. Written before the lines, so
    /// a product unlinked here can be given its own lines in the same save. Removing a link doesn't copy the lines it
    /// showed: pass them as <paramref name="packagingLines"/>. <see cref="PackagingLinkDraft.NewSet"/> makes
    /// <paramref name="packagingLines"/> a new packaging set on the Sub-Assemblies screen, named after the product
    /// ("Ensaymada Packaging", numbered when taken), and links it: the product keeps no lines of its own.
    /// </param>
    /// <param name="packagingLines">The new packaging lines, as IPackagingLineRepository.SetLinesAsync takes them, or null to leave them as they are.</param>
    /// <param name="costing">The new costing parameters (only their cost and percentage fields are read), or null to leave them as they are.</param>
    Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the product to the Deleted folder: it's stamped DeletedAt and kept whole, so RestoreAsync brings it back as it
    /// was. Everything it uses stays held by it, as the Used By lists and refusals show ("Cheese Roll (deleted)"). Refused
    /// while another product links its packaging, or while a composite holds it, deleted or not.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the product out of the Deleted folder, back to its stage and category (Uncategorized if that category has
    /// gone since). Nothing else changes, UpdatedAt included.
    /// </summary>
    Task RestoreAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the product for good, with its own sub-assemblies, usages, packaging, costing and, for a composite, its
    /// lines; the sub-assemblies it links from the Sub-Assemblies screen, and the products a composite holds, stay. Refused
    /// as DeleteAsync is.
    /// </summary>
    Task DeletePermanentlyAsync(int id, CancellationToken cancellationToken);
}
