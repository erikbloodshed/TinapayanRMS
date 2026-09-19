using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyLineRepository
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
                    FROM SubAssemblyLines l
                    JOIN RawMaterials i ON i.Id = l.IngredientId
                    WHERE l.SubAssemblyId IN ({DapperReads.IdsTable})
                    ORDER BY l.Id
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
    /// Replaces the sub-assembly's whole line set on the caller's context (edited together, like an
    /// ingredient's BOM), recomputes its TotalWeight/TotalCost, and saves. Lines are in grams, and every
    /// one must be an ingredient (RawMaterialRules.EnsureUsableInRecipes). Nothing is deleted until every
    /// line passes.
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
            .Select(m => new { m.Id, m.Name, m.Type, m.BufferedCostPerBaseUnit })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!ingredients.TryGetValue(line.IngredientId, out var ingredient))
            {
                throw new InvalidOperationException($"Ingredient {line.IngredientId} does not exist.");
            }

            RawMaterialRules.EnsureUsableInRecipes(ingredient.Type, ingredient.Name);
        }

        await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == subAssembly.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.SubAssemblyLines.AddRange(lines.Select(l => new SubAssemblyLine
        {
            SubAssemblyId = subAssembly.Id,
            IngredientId = l.IngredientId,
            Grams = l.Grams,
            IsFormulaBase = l.IsFormulaBase,
            AuthoredBakersPercent = l.AuthoredBakersPercent,
        }));

        var costInputs = lines.Select(l => (l.Grams, IngredientCostPerGram: ingredients[l.IngredientId].BufferedCostPerBaseUnit)).ToList();
        (subAssembly.TotalWeight, subAssembly.TotalCost) = SubAssemblyRules.CalculateTotals(costInputs);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
