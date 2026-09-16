using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialRepository
{
    /// <summary>Every raw material, both types, with its category, base unit and alternative units, by name.</summary>
    Task<IReadOnlyList<RawMaterial>> GetAllAsync(CancellationToken cancellationToken);

    Task<RawMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Rejects fields that don't fit the material's type or source (RawMaterialRules), and a linked
    /// ingredient's link to itself, to packaging, past 100% of the linked-to ingredient's weight or cost
    /// together with its other parts, or round a loop. A linked ingredient is costed as it's added.
    /// </summary>
    Task AddAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the name, category, weight loss percent, a linked ingredient's link and shares (checked
    /// as AddAsync checks them, and cascaded), and (packaging only, while nothing is measured in it yet)
    /// base unit. The type and source are fixed once created.
    /// </summary>
    Task UpdateAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Raw Materials screen edits in one transaction, so nothing is saved unless
    /// every part passes: the material itself (added when its Id is 0, otherwise updated as
    /// UpdateAsync does), its alternative units, a new price and its BOM, each checked as their own
    /// repositories check them. Returns the material's id.
    /// </summary>
    /// <param name="units">
    /// The material's complete set of alternative units: saved ones missing from it are removed,
    /// those with an Id are updated and those without are added. Only their fields are read.
    /// </param>
    /// <param name="newPrice">
    /// A price entry to add, or null for none. Its unit is named by unit of measure, since a unit added
    /// in the same save has no id yet; a null unit means a price per base unit.
    /// </param>
    /// <param name="bomLines">A made ingredient's new BOM, in grams, replacing the old one, or null to leave the BOM as it is.</param>
    Task<int> SaveAsync(
        RawMaterial material,
        IReadOnlyList<RawMaterialUnit> units,
        (DateOnly Date, int? UnitOfMeasureId, decimal Price)? newPrice,
        IReadOnlyList<(int ComponentIngredientId, decimal Grams)>? bomLines,
        CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
