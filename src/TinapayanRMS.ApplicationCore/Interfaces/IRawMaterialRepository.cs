using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A raw material filed under another folder of the Raw Materials list: the category it goes in from now on, which has
/// to be one of its own type.
/// </summary>
public sealed record RawMaterialMove(int RawMaterialId, int CategoryId);

public interface IRawMaterialRepository
{
    /// <summary>Every raw material, both types, with its category, base unit and alternative units, by name.</summary>
    Task<IReadOnlyList<RawMaterial>> GetAllAsync(CancellationToken cancellationToken);

    Task<RawMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// The products whose cost the material goes into, alphabetically, each once. An ingredient counts through the
    /// sub-assemblies a product uses (its own and linked ones) and the add-ins on its linked usages, and so does every
    /// made ingredient it's in and linked ingredient cut from it, however deep, and every composite holding such a product,
    /// directly or through another composite. Packaging counts through a product's own packaging lines and a packaging set
    /// its packaging links, and so does every product whose packaging links that product's, down the chain; a composite's
    /// products bring their packaging into it only when it includes theirs, however deep.
    /// </summary>
    Task<IReadOnlyList<LinkingProduct>> GetDependentsAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Rejects fields that don't fit the material's type or source (RawMaterialRules), and a linked
    /// ingredient's link to itself, to packaging, past 100% of the linked-to ingredient's weight or cost
    /// together with its other parts, or round a loop. A linked ingredient is costed as it's added.
    /// </summary>
    Task AddAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the name, category, a made ingredient's weight loss percent and sub-assembly, a linked ingredient's link and shares (checked
    /// as AddAsync checks them, and cascaded), and (packaging only, while nothing is measured in it yet)
    /// base unit. The type and source are fixed once created.
    /// </summary>
    Task UpdateAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Puts each purchased material's target buffer back on top of its dearest brand (Reports' Cost Buffers): its
    /// buffered cost becomes that, and its buffer percent the target again, with the cascade a Save runs, all in one
    /// transaction. A material with no price, or not purchased, is passed over, and so is one already at its target.
    /// Returns how many were changed.
    /// </summary>
    Task<int> ReapplyTargetBuffersAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Files each material under another category, all in one save: sets its category, nothing else, and stamps
    /// UpdatedAt. Refused, with nothing moved, if a material or a category doesn't exist, or a category is of the
    /// other type. A category costs nothing, so nothing cascades.
    /// </summary>
    Task MoveAsync(IReadOnlyCollection<RawMaterialMove> moves, CancellationToken cancellationToken);

    /// <summary>The same from the Raw Materials list: refused, with nothing moved, over a save made since the form showing opened its material (<paramref name="showing"/>).</summary>
    Task MoveAsync(IReadOnlyCollection<RawMaterialMove> moves, SavedVersionCheck? showing, CancellationToken cancellationToken);

    /// <summary>Renames the material and nothing else, stamping UpdatedAt.</summary>
    Task RenameAsync(int id, string name, CancellationToken cancellationToken);

    /// <summary>The same from the Raw Materials list: refused over a save made since the form showing opened the material (<paramref name="showing"/>).</summary>
    Task RenameAsync(int id, string name, SavedVersionCheck? showing, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Raw Materials screen edits in one transaction, so nothing is saved unless
    /// every part passes: the material itself (added when its Id is 0, otherwise updated as
    /// UpdateAsync does), its alternative units, its brand prices and a made ingredient's add-ins, each checked as
    /// their own repositories check them. A made ingredient is refused when its sub-assembly is a packaging set or a
    /// product's own, an add-in is already in that recipe, or the recipe has the made ingredient in it, however deep.
    /// Returns the material's id.
    /// </summary>
    /// <param name="units">
    /// The material's complete set of alternative units: saved ones missing from it are removed,
    /// those with an Id are updated and those without are added. Only their fields are read.
    /// </param>
    /// <param name="brandPrices">
    /// A purchased material's complete set of brand prices, replacing the saved ones, or null to leave
    /// them as they are. Each unit is named by unit of measure, since a unit added in the same save has
    /// no id yet; a null unit means a price per base unit. The material is costed at the dearest per base
    /// unit, and a new cost is logged in price history, dated today (RawMaterialBrandPriceRepository).
    /// </param>
    /// <param name="addIns">
    /// A made ingredient's new add-ins, in grams, replacing the old ones, or null to leave them as they are. The recipe
    /// they go on top of is the sub-assembly it's made from (RawMaterial.SubAssemblyId), which is edited on the
    /// Sub-Assemblies screen, never here.
    /// </param>
    Task<int> SaveAsync(
        RawMaterial material,
        IReadOnlyList<RawMaterialUnit> units,
        IReadOnlyList<(string? Brand, int? UnitOfMeasureId, decimal Price)>? brandPrices,
        IReadOnlyList<(int IngredientId, decimal Grams)>? addIns,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the material with its units, prices and price history, refused while anything uses it, naming each user
    /// (RawMaterialRules.EnsureNotInUse).
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
