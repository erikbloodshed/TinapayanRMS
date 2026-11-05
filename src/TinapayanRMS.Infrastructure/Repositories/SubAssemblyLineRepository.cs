using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class SubAssemblyLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyLineRepository
{
    public async Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssembliesAsync(IReadOnlyCollection<int> subAssemblyIds, CancellationToken cancellationToken)
    {
        if (subAssemblyIds.Count == 0)
        {
            return [];
        }

        return await dbContextFactory.ReadAsync<IReadOnlyList<SubAssemblyLine>>(
            async dbContext => (await dbContext.Connection().QueryAsync<SubAssemblyLine, RawMaterial, SubAssemblyLine>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<SubAssemblyLine>("l")}, {Columns.Of<RawMaterial>("i")}
                    FROM sub_assembly_lines l
                    JOIN raw_materials i ON i.id = l.ingredient_id
                    WHERE l.sub_assembly_id IN ({DapperReads.IdsTable})
                    ORDER BY l.sort_order, l.id
                    """,
                    new { ids = DapperReads.IdList(subAssemblyIds) },
                    cancellationToken),
                (line, ingredient) =>
                {
                    line.Ingredient = ingredient;
                    return line;
                },
                splitOn: "Id")).AsList(),
            cancellationToken);
    }

    /// <summary>
    /// Replaces the sub-assembly's whole line set on the caller's context (edited together) and saves. Lines are in grams,
    /// and every one must be an ingredient
    /// (RawMaterialRules.EnsureUsableInRecipes). Nothing is deleted until every line passes. Every line write goes
    /// through here, so here is where the made ingredients made from it are cascaded, in the caller's transaction,
    /// after a check that no line closes a loop through one of them.
    /// </summary>
    /// <param name="subAssembly">The sub-assembly, tracked by <paramref name="dbContext"/> and saved already (it has its id).</param>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        SubAssembly subAssembly,
        IReadOnlyList<RecipeLine> lines,
        CancellationToken cancellationToken)
    {
        SubAssemblyRules.EnsureValidLines(lines);

        var ingredientIds = lines.Select(l => l.IngredientId).ToList();
        var ingredients = await dbContext.RawMaterials
            .Where(m => ingredientIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!ingredients.TryGetValue(line.IngredientId, out var ingredient))
            {
                throw new BusinessRuleException($"Ingredient {line.IngredientId} does not exist.");
            }

            RawMaterialRules.EnsureUsableInRecipes(ingredient.Type, ingredient.Name);
        }

        await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == subAssembly.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.SubAssemblyLines.AddRange(lines.Select((l, index) => new SubAssemblyLine
        {
            SubAssemblyId = subAssembly.Id,
            IngredientId = l.IngredientId,
            Grams = l.Grams,
            IsFormulaBase = l.IsFormulaBase,
            AuthoredBakersPercent = l.AuthoredBakersPercent,
            SortOrder = index,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);

        // A made ingredient made from it has these lines in its BOM (IngredientBoms), so each is checked for a loop through
        // them, then repriced, which reaches every made ingredient using it in turn.
        var madeFromIt = await dbContext.RawMaterials
            .Where(m => m.SubAssemblyId == subAssembly.Id)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(cancellationToken);
        foreach (var made in madeFromIt)
        {
            IngredientBomRules.EnsureNotInOwnRecipe(made.Id, made.Name, subAssembly.Name, ingredientIds);
            await IngredientDependencies.EnsureNoCycleAsync(dbContext, made.Id, cancellationToken);
        }

        await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, [.. madeFromIt.Select(m => m.Id)], cancellationToken);
    }
}
