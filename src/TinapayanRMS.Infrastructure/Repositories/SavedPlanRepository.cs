using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SavedPlanRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISavedPlanRepository
{
    public Task<IReadOnlyList<SavedPlan>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<SavedPlan>>(
            async dbContext =>
            {
                var plans = await dbContext.QueryAsync<SavedPlan>(
                    $"SELECT {Columns.Of<SavedPlan>("p")} FROM saved_plans p ORDER BY p.saved_at DESC, p.name", null, cancellationToken);
                var lines = (await dbContext.QueryAsync<SavedPlanLine>(
                        $"SELECT {Columns.Of<SavedPlanLine>("l")} FROM saved_plan_lines l ORDER BY l.sort_order, l.id", null, cancellationToken))
                    .ToLookup(l => l.SavedPlanId);
                foreach (var plan in plans)
                {
                    plan.Lines = [.. lines[plan.Id]];
                }

                return plans;
            },
            cancellationToken);

    public async Task<int> SaveAsync(string name, IReadOnlyList<SavedPlanLine> lines, bool halfBatches, DateTime savedAt, CancellationToken cancellationToken)
    {
        SavedPlanRules.EnsureValid(name, lines);
        name = name.Trim();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The name's column ignores case, so "saturday" finds "Saturday", which keeps the name it has.
        var plan = await dbContext.SavedPlans.FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
        if (plan is null)
        {
            plan = new SavedPlan { Name = name };
            dbContext.SavedPlans.Add(plan);
        }
        else
        {
            await dbContext.SavedPlanLines.Where(l => l.SavedPlanId == plan.Id).ExecuteDeleteAsync(cancellationToken);
        }

        plan.SavedAt = savedAt;
        plan.HalfBatches = halfBatches;
        plan.Lines = [.. lines.Select((line, i) => new SavedPlanLine { ProductId = line.ProductId, Units = line.Units, SortOrder = i })];

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return plan.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.SavedPlans.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
