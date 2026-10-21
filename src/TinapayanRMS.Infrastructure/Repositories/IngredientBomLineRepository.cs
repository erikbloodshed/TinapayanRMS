using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class IngredientBomLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IIngredientBomLineRepository
{
    public Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<IngredientBomLine>>(
            async dbContext => (await dbContext.Connection().QueryAsync<IngredientBomLine, RawMaterial, IngredientBomLine>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<IngredientBomLine>("l")}, {Columns.Of<RawMaterial>("c")}
                    FROM ingredient_bom_lines l
                    JOIN raw_materials c ON c.id = l.component_ingredient_id
                    WHERE l.ingredient_id = @ingredientId
                    ORDER BY l.sort_order, l.id
                    """,
                    new { ingredientId },
                    cancellationToken),
                (line, component) =>
                {
                    line.ComponentIngredient = component;
                    return line;
                },
                splitOn: "Id")).AsList(),
            cancellationToken);

    public Task<IReadOnlyList<IngredientBomLine>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<IngredientBomLine>>(
            async dbContext => (await dbContext.Connection().QueryAsync<IngredientBomLine>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<IngredientBomLine>("l")}
                    FROM ingredient_bom_lines l
                    ORDER BY l.ingredient_id, l.sort_order, l.id
                    """,
                    null,
                    cancellationToken))).AsList(),
            cancellationToken);

    public async Task SetLinesAsync(
        int ingredientId, IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The old lines' delete, the new lines' insert and the cost they give commit together.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await SetLinesAsync(dbContext, ingredientId, lines, cancellationToken);

        // Recomputes ingredientId's own CostPerBaseUnit from the lines just saved, then
        // ripples the change to anything that depends on it.
        await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, ingredientId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the made ingredient's BOM with <paramref name="lines"/> and saves, once every line
    /// passes. Its cost is the caller's to cascade, in its transaction (IngredientCostPropagator).
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int ingredientId,
        IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines,
        CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == ingredientId, cancellationToken)
            ?? throw new BusinessRuleException($"Ingredient {ingredientId} does not exist.");

        // Made implies the Ingredient type (RawMaterialRules.EnsureFieldsMatchType).
        if (ingredient.Source != RawMaterialSource.Made)
        {
            throw new BusinessRuleException("Only made ingredients have a BOM.");
        }

        IngredientBomRules.EnsureValidLines(lines);

        // ingredientId's own (about-to-be-replaced) lines are excluded, so the cycle
        // check only sees everyone else's current BOM and links: a component linked to
        // this ingredient closes a loop as surely as one made from it.
        var componentIdsByIngredientId = await IngredientDependencies.LoadAsync(dbContext, ingredientId, cancellationToken);

        IngredientBomRules.EnsureNoCycle(ingredientId, lines.Select(l => l.ComponentIngredientId), componentIdsByIngredientId);

        var componentIds = lines.Select(l => l.ComponentIngredientId).ToList();
        var components = await dbContext.RawMaterials
            .Where(m => componentIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!components.TryGetValue(line.ComponentIngredientId, out var component))
            {
                throw new BusinessRuleException($"Ingredient {line.ComponentIngredientId} does not exist.");
            }

            RawMaterialRules.EnsureUsableInRecipes(component.Type, component.Name);
        }

        await dbContext.IngredientBomLines
            .Where(l => l.IngredientId == ingredientId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.IngredientBomLines.AddRange(lines.Select((l, index) => new IngredientBomLine
        {
            IngredientId = ingredientId,
            ComponentIngredientId = l.ComponentIngredientId,
            Grams = l.Grams,
            SortOrder = index,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
