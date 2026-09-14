using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyLineRepository
{
    public async Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssemblyAsync(int subAssemblyId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblyLines
            .AsNoTracking()
            .Include(l => l.Ingredient)
            .Include(l => l.Unit)
            .ThenInclude(u => u!.UnitOfMeasure)
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(
        int subAssemblyId, IReadOnlyList<(int IngredientId, int? UnitId, decimal Quantity)> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subAssembly = await dbContext.SubAssemblies.FirstOrDefaultAsync(s => s.Id == subAssemblyId, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {subAssemblyId} does not exist.");

        var grams = await IngredientQuantityConverter.ToGramsAsync(dbContext, lines, cancellationToken);
        SubAssemblyRules.EnsureValidLines([.. lines.Select((l, i) => (l.IngredientId, grams[i]))]);

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
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.SubAssemblyLines.AddRange(lines.Select((l, i) => new SubAssemblyLine
        {
            SubAssemblyId = subAssemblyId,
            IngredientId = l.IngredientId,
            Quantity = l.Quantity,
            UnitId = l.UnitId,
            Grams = grams[i],
        }));

        var costInputs = lines.Select((l, i) => (grams[i], IngredientCostPerGram: ingredients[l.IngredientId].BufferedCostPerBaseUnit)).ToList();
        (subAssembly.TotalWeight, subAssembly.TotalCost) = SubAssemblyRules.CalculateTotals(costInputs);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
