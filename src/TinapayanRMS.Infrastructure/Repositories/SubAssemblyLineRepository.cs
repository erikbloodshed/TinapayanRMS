using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class SubAssemblyLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyLineRepository
{
    public async Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssemblyAsync(int subAssemblyId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SubAssemblyLines
            .AsNoTracking()
            .Include(l => l.Ingredient)
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(int subAssemblyId, IReadOnlyList<(int IngredientId, decimal Grams)> lines, CancellationToken cancellationToken)
    {
        SubAssemblyRules.EnsureValidLines(lines);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subAssembly = await dbContext.SubAssemblies.FirstOrDefaultAsync(s => s.Id == subAssemblyId, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {subAssemblyId} does not exist.");

        var ingredientCosts = await dbContext.Ingredients
            .Where(i => lines.Select(l => l.IngredientId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.CostPerGram, cancellationToken);

        foreach (var line in lines)
        {
            if (!ingredientCosts.ContainsKey(line.IngredientId))
            {
                throw new InvalidOperationException($"Ingredient {line.IngredientId} does not exist.");
            }
        }

        await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.SubAssemblyLines.AddRange(lines.Select(l => new SubAssemblyLine
        {
            SubAssemblyId = subAssemblyId,
            IngredientId = l.IngredientId,
            Grams = l.Grams,
        }));

        var costInputs = lines.Select(l => (l.Grams, IngredientCostPerGram: ingredientCosts[l.IngredientId])).ToList();
        (subAssembly.TotalWeight, subAssembly.TotalCost) = SubAssemblyRules.CalculateTotals(costInputs);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
