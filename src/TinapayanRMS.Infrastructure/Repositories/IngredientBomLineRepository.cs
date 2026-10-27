using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// Made ingredients' bills of materials, read from the sub-assembly each is made from and its own add-ins (IngredientBoms).
/// Nothing is written here: the lines are the Sub-Assemblies screen's, and the add-ins RawMaterialRepository.SaveAsync's.
/// </summary>
public class IngredientBomLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IIngredientBomLineRepository
{
    public Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<IngredientBomLine>>(
            async dbContext =>
            {
                var lines = await IngredientBoms.ForAsync(dbContext, [ingredientId], cancellationToken);
                var componentIds = lines.Select(l => l.ComponentIngredientId).Distinct().ToList();
                var components = await dbContext.RawMaterials
                    .AsNoTracking()
                    .Where(m => componentIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, cancellationToken);
                foreach (var line in lines)
                {
                    line.ComponentIngredient = components[line.ComponentIngredientId];
                }

                return lines;
            },
            cancellationToken);

    public Task<IReadOnlyList<IngredientBomLine>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<IngredientBomLine>>(
            async dbContext => await IngredientBoms.ForAsync(dbContext, null, cancellationToken),
            cancellationToken);
}
