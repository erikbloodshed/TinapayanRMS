using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PriceScenarioRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPriceScenarioRepository
{
    public Task<IReadOnlyList<PriceScenario>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<PriceScenario>>(
            async dbContext =>
            {
                var scenarios = await dbContext.QueryAsync<PriceScenario>(
                    $"SELECT {Columns.Of<PriceScenario>("s")} FROM price_scenarios s ORDER BY s.saved_at DESC, s.name", null, cancellationToken);
                var lines = (await dbContext.QueryAsync<PriceScenarioLine>(
                        $"SELECT {Columns.Of<PriceScenarioLine>("l")} FROM price_scenario_lines l ORDER BY l.sort_order, l.id", null, cancellationToken))
                    .ToLookup(l => l.PriceScenarioId);
                foreach (var scenario in scenarios)
                {
                    scenario.Lines = [.. lines[scenario.Id]];
                }

                return scenarios;
            },
            cancellationToken);

    public async Task<int> SaveAsync(string name, IReadOnlyList<PriceScenarioLine> lines, DateTime savedAt, CancellationToken cancellationToken)
    {
        PriceScenarioRules.EnsureValid(name, lines);
        name = name.Trim();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);

        // The name's column ignores case, so "supplier a" finds "Supplier A", which keeps the name it has.
        var scenario = await dbContext.PriceScenarios.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (scenario is null)
        {
            scenario = new PriceScenario { Name = name };
            dbContext.PriceScenarios.Add(scenario);
        }
        else
        {
            await dbContext.PriceScenarioLines.Where(l => l.PriceScenarioId == scenario.Id).ExecuteDeleteAsync(cancellationToken);
        }

        scenario.SavedAt = savedAt;
        scenario.Lines = [.. lines.Select((line, i) => new PriceScenarioLine
        {
            RawMaterialId = line.RawMaterialId,
            ChangePercent = line.ChangePercent,
            NewPrice = line.NewPrice,
            SortOrder = i,
        })];

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return scenario.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.PriceScenarios.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
