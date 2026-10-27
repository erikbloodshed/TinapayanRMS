using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Made ingredients' bills of materials: each one's sub-assembly's lines, then its own add-ins (IngredientBomLine.IsAddIn).
/// Read only. The lines are written on the Sub-Assemblies screen, and the add-ins with the made ingredient
/// (IRawMaterialRepository.SaveAsync).
/// </summary>
public interface IIngredientBomLineRepository
{
    /// <summary>The lines with their component.</summary>
    Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    /// <summary>
    /// Every made ingredient's lines, without their components (the caller has the ingredients already): each BOM's in
    /// its order, by made ingredient. What the Products screen's Scaling tab opens a made ingredient into.
    /// </summary>
    Task<IReadOnlyList<IngredientBomLine>> GetAllAsync(CancellationToken cancellationToken);
}
