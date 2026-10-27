using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// Every made ingredient's bill of materials, read from what it's made of (2026-10-27): the lines of the sub-assembly it's
/// made from, then its own add-ins. The one definition of it, so the cascade (IngredientCostPropagator), the cycle check
/// (IngredientDependencies) and every screen reading a BOM (IngredientBomLineRepository) can't disagree. A sub-assembly
/// line edit changes the BOM of every made ingredient made from it, which is why SubAssemblyLineRepository.SetLinesAsync
/// cascades them.
/// </summary>
internal static class IngredientBoms
{
    /// <summary>Every made ingredient's lines, unordered: order by IngredientId, IsAddIn, SortOrder for each BOM in its order.</summary>
    public static IQueryable<IngredientBomLine> Query(TinapayanRMSDbContext dbContext) =>
        dbContext.RawMaterials
            .Where(m => m.SubAssemblyId != null)
            .Join(
                dbContext.SubAssemblyLines,
                m => m.SubAssemblyId!.Value,
                l => l.SubAssemblyId,
                (m, l) => new IngredientBomLine
                {
                    IngredientId = m.Id,
                    ComponentIngredientId = l.IngredientId,
                    Grams = l.Grams,
                    SortOrder = l.SortOrder,
                    IsAddIn = false,
                })
            .Concat(dbContext.IngredientAddInLines.Select(a => new IngredientBomLine
            {
                IngredientId = a.IngredientId,
                ComponentIngredientId = a.AddInIngredientId,
                Grams = a.Grams,
                SortOrder = a.SortOrder,
                IsAddIn = true,
            }));

    /// <summary>
    /// <paramref name="ingredientIds"/>' lines, each BOM in its order, read on <paramref name="dbContext"/> (inside its
    /// transaction, if it has one).
    /// </summary>
    public static async Task<List<IngredientBomLine>> ForAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int>? ingredientIds, CancellationToken cancellationToken)
    {
        var query = Query(dbContext);
        if (ingredientIds is not null)
        {
            query = query.Where(l => ingredientIds.Contains(l.IngredientId));
        }

        return await query
            .OrderBy(l => l.IngredientId)
            .ThenBy(l => l.IsAddIn)
            .ThenBy(l => l.SortOrder)
            .ToListAsync(cancellationToken);
    }
}
