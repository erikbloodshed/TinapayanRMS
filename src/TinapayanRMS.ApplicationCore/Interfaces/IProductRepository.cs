using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A recipe section whose lines the product's Recipe Structure edits: its role, its gram lines and the product's piece
/// size. Its <see cref="Placement"/> says where Save puts it: the product's own sub-assembly, new or saved
/// (<see cref="Id"/>), or one Save keeps on the Sub-Assemblies screen and links. Either is named after the product and its
/// role (SubAssemblyRules.StandaloneNameFor).
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

    /// <summary>Where Save puts it: as its role says, unless the Products form asked for something else.</summary>
    public SectionPlacement Placement { get; init; } = new SectionPlacement.ByRole();

    /// <summary>
    /// The recipe's other sections on its lines, each with its grams and its place among them (UsageSectionLine). Empty
    /// for a section of ingredients alone.
    /// </summary>
    public IReadOnlyList<SectionLineDraft> SectionLines { get; init; } = [];

    /// <summary>Unlocked while another section uses it on its lines (ProductSubAssemblyUsage.IsUnlocked); false otherwise.</summary>
    public bool IsUnlocked { get; init; }

    /// <summary>Save keeps it on the Sub-Assemblies screen and links it: moved there, or to In-House.</summary>
    public bool MovesToSubAssemblies => Placement is SectionPlacement.MoveToSubAssemblies or SectionPlacement.MoveToInHouse;
}

/// <summary>
/// Where Save puts a recipe section's sub-assembly (<see cref="NewSubAssemblyDraft.Placement"/>): one case for each thing
/// the Products form can ask of it.
/// </summary>
public closed record SectionPlacement
{
    /// <summary>
    /// Its role decides (SubAssemblyRole.IsProductOwned): the product's own in a product-owned role, otherwise kept on the
    /// Sub-Assemblies screen and linked. A saved one of the product's own stays its own, unless it moves out of a
    /// product-owned role into one that isn't.
    /// </summary>
    public sealed record ByRole : SectionPlacement;

    /// <summary>
    /// A new one is the product's own whatever its role: a linked section unlinked in the Products form, or a clone's copy
    /// of its source's own. A saved one's role decides, as for <see cref="ByRole"/>.
    /// </summary>
    public sealed record ProductsOwn : SectionPlacement;

    /// <summary>
    /// Move to Sub-Assemblies: kept on the Sub-Assemblies screen and linked, whatever its role. A saved one keeps its id,
    /// name and lines (as written in this save).
    /// </summary>
    public sealed record MoveToSubAssemblies : SectionPlacement;

    /// <summary>
    /// Move to In-House: moved to
    /// the Sub-Assemblies screen as <see cref="MoveToSubAssemblies"/> is, and a new made ingredient is made from it. The
    /// product links it at the same piece size, so it costs what it did.
    /// </summary>
    public sealed record MoveToInHouse(MadeIngredientDraft Ingredient) : SectionPlacement;
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

    /// <summary>
    /// The lines <see cref="Lines"/> were changed from, as the form took them: Save is refused when the sub-assembly's have
    /// been saved as others since (SavedVersionRules.EnsureLinesNotSavedSince). Null, from a caller that isn't a form, isn't
    /// checked.
    /// </summary>
    public IReadOnlyList<RecipeLine>? LinesTaken { get; init; }

    /// <summary>
    /// The recipe's other sections among its add-ins, each with its grams and its place among them (UsageSectionLine):
    /// this product's, as its add-ins are. Empty for none.
    /// </summary>
    public IReadOnlyList<SectionLineDraft> SectionLines { get; init; } = [];

    /// <summary>Unlocked while another section uses it on its lines (ProductSubAssemblyUsage.IsUnlocked); false otherwise.</summary>
    public bool IsUnlocked { get; init; }
}

/// <summary>
/// A line of a recipe section that is another of the recipe's sections, as Save writes it (UsageSectionLine): a sponge on
/// the dough's lines.
/// </summary>
/// <param name="SectionId">The section it uses (<see cref="RecipeDraft.SectionIdOf"/>): a saved sub-assembly's id, or a new section's stand-in.</param>
/// <param name="AuthoredBakersPercent">The percentage typed for it, or null when its grams were.</param>
/// <param name="Position">Its place in its section's table, among the ingredient lines (or a linked one's add-ins).</param>
public sealed record SectionLineDraft(int SectionId, decimal Grams, decimal? AuthoredBakersPercent, int Position);

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

    /// <summary>
    /// The product's own sub-assemblies as the form took them. One that's its own now, isn't among these and isn't kept
    /// as its own here became so since (its role made product-owned), so Save is refused rather than delete it as one
    /// taken out (SavedVersionRules.OwnershipChanged). Null, from a caller that isn't a form, isn't checked.
    /// </summary>
    public IReadOnlyCollection<int>? OwnTaken { get; init; }

    /// <summary>
    /// How a section of this recipe is named where it's on another's lines (<see cref="SectionLineDraft.SectionId"/>): its
    /// sub-assembly's id once it has one, or -(i + 1) for the i-th of
    /// <see cref="New"/> while it's new (Id 0).
    /// </summary>
    public static int SectionIdOf(int subAssemblyId, int newIndex) => subAssemblyId > 0 ? subAssemblyId : -(newIndex + 1);
}

/// <summary>
/// What a product's packaging links, as Save writes it: another product's packaging (<see cref="OtherProduct"/>), a
/// standalone packaging set (<see cref="Set"/>, one set per sellable unit), or neither (<see cref="None"/>), when the
/// product owns its packaging lines; or a new set made of its lines (<see cref="NewSet"/>).
/// </summary>
public closed record PackagingLinkDraft
{
    /// <summary>No link: the product's packaging is its own lines.</summary>
    public sealed record None : PackagingLinkDraft;

    /// <summary>
    /// Move to Sub-Assemblies on the Products screen's Packaging tab: the packaging lines saved with it become a new
    /// packaging set on the Sub-Assemblies screen, named after the product and the Packaging role, and the product links
    /// it in their place.
    /// </summary>
    public sealed record NewSet : PackagingLinkDraft;

    /// <summary>A standalone packaging set from the Sub-Assemblies screen.</summary>
    public sealed record Set(int SubAssemblyId) : PackagingLinkDraft;

    /// <summary>Another product's packaging, whatever it is: its own lines, or what it links in turn.</summary>
    public sealed record OtherProduct(int ProductId) : PackagingLinkDraft;
}

/// <summary>
/// A product's bulk packaging as Save writes it: the packaging one bulk pack takes (a box several units share, its tape),
/// each line's quantity per pack, or the packaging set on the Sub-Assemblies screen it links instead (<paramref name="SetId"/>,
/// whose lines are the pack's), and how many sellable units one pack holds (Product.BulkPackagingQuantity), which its cost
/// is shared out among, the product's own either way. No lines and no set is no bulk packaging, and the quantity is then
/// stored as null.
/// </summary>
/// <param name="Lines">The product's own lines for one pack: none while it links a set.</param>
/// <param name="SetId">The packaging set its bulk packaging links (PackagingLink.IsBulk), or null for its own lines.</param>
public sealed record BulkPackagingDraft(
    IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> Lines,
    int? Quantity,
    int? SetId = null)
{
    /// <summary>It's some bulk packaging, its own lines or a set's: what needs how many products a pack holds.</summary>
    public bool HasPackaging => Lines.Count > 0 || SetId is not null;
}

/// <summary>
/// A product filed under another folder of the Products list: the stage and the folder (ProductFolder) it goes in from now
/// on. <paramref name="FolderId"/> null files it at the top of the stage. Its category stays as it is.
/// </summary>
public sealed record ProductMove(int ProductId, ProductStage Stage, int? FolderId);

/// <summary>
/// What a Promote or a Send Back did (IProductRepository.MoveToStageAsync): each product's move, and the folders made for
/// them in the other stage, which an Undo takes away again if they're still empty.
/// </summary>
public sealed record StageMove(IReadOnlyList<ProductMove> Moves, IReadOnlyList<int> MadeFolderIds);

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
    /// Changes the name, description, category, quantity required and current price, and stamps UpdatedAt. The stage and
    /// folder stay as they are; whether it's a composite can't change (a different IsComposite is refused).
    /// </summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the products as saved now (UpdatedAt) and changes nothing else, in one command however many there are: what
    /// a change to something they're priced from that isn't theirs leaves them (the Products list's Apply Costing Defaults).
    /// </summary>
    Task StampSavedAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Files each product under another folder of the Products list, all in one save: sets its stage and folder,
    /// nothing else, and stamps UpdatedAt. Refused, with nothing moved, if a product or a folder doesn't exist, or a
    /// folder is in the other stage.
    /// </summary>
    Task MoveAsync(IReadOnlyCollection<ProductMove> moves, CancellationToken cancellationToken);

    /// <summary>The same from the Products list: refused, with nothing moved, over a save made since a tab holding one of its products with unsaved changes opened it (<paramref name="checks"/>, one a tab).</summary>
    Task MoveAsync(IReadOnlyCollection<ProductMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>
    /// Promote and Send Back: files each product in <paramref name="stage"/>, in the folder at the same path as its own
    /// (Innovation › Breads › Holiday to Production › Breads › Holiday), making the folders that aren't there yet; one at
    /// the top of its stage goes to the top of the other. A product in <paramref name="stage"/> already is left out. All in
    /// one save, stamping UpdatedAt.
    /// </summary>
    Task<StageMove> MoveToStageAsync(IReadOnlyCollection<int> productIds, ProductStage stage, CancellationToken cancellationToken);

    /// <summary>The same from the Products list: refused, with nothing moved, over a save made since a tab holding one of its products with unsaved changes opened it (<paramref name="checks"/>, one a tab).</summary>
    Task<StageMove> MoveToStageAsync(IReadOnlyCollection<int> productIds, ProductStage stage, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the product and nothing else, stamping UpdatedAt; its own sub-assemblies, named after it, take the new
    /// name as a Save's rename gives it to them.
    /// </summary>
    Task RenameAsync(int productId, string name, CancellationToken cancellationToken);

    /// <summary>The same from the Products list: refused over a save made since a tab holding the product with unsaved changes opened it (<paramref name="checks"/>).</summary>
    Task RenameAsync(int productId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Products screen edits in one transaction, so nothing is saved unless every
    /// part passes: the product itself (added when its Id is 0, otherwise updated as UpdateAsync does),
    /// its recipe, its packaging lines and its costing parameters, each checked as their own repositories
    /// check them. Returns the product's id.
    /// </summary>
    /// <param name="product">
    /// Only its Id, Name, Description, CategoryId, QuantityRequired, CurrentPrice and IsComposite are read, and a new one's
    /// Stage and FolderId; it's never given an id. A new product goes in the stage and folder it holds (Innovation unless
    /// it's set; the folder's stage when they differ, and the top of the stage when the folder is gone), and a saved one
    /// keeps its stage and folder. Whether it's a composite is a new one's to say; a saved one's can't change.
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
    /// <param name="bulkPackaging">
    /// The new bulk packaging, its lines and how many units a pack holds (one needs the other:
    /// ProductRules.EnsureValidBulkPackaging), or null to leave it as it is. The product's own, whatever its packaging links.
    /// </param>
    /// <param name="costing">The new costing parameters (only their cost and percentage fields are read), or null to leave them as they are.</param>
    Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        BulkPackagingDraft? bulkPackaging,
        CostingParameters? costing,
        CancellationToken cancellationToken);

    /// <summary>SaveAsync leaving the bulk packaging as it is.</summary>
    Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the product to the Deleted folder: it's stamped DeletedAt and kept whole, so RestoreAsync brings it back as it
    /// was. Everything it uses stays held by it, as the Dependents lists and refusals show ("Cheese Roll (deleted)"). Refused
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
