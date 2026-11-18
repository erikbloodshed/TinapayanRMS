using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// Made ingredients' bills of materials, read from the sub-assembly each is made from and its own add-ins (IngredientBoms).
/// Nothing is written here: the lines are the Sub-Assemblies screen's, and the add-ins RawMaterialRepository.SaveAsync's.
/// </summary>
public sealed class IngredientBomLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IIngredientBomLineRepository
{
    public Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<IngredientBomLine>>(
            async dbContext =>
            {
                // The lines through the one EF query every BOM is read by (IngredientBoms); their ingredients through Dapper.
                var lines = await IngredientBoms.ForAsync(dbContext, [ingredientId], cancellationToken);
                if (lines.Count == 0)
                {
                    return lines;
                }

                var components = (await dbContext.QueryAsync<RawMaterial>(
                        $"SELECT {Columns.Of<RawMaterial>("m")} FROM raw_materials m WHERE m.id IN ({DapperReads.IdsTable}) ORDER BY m.id",
                        new { ids = DapperReads.IdList(lines.Select(l => l.ComponentIngredientId).Distinct()) },
                        cancellationToken))
                    .ToDictionary(m => m.Id);
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
