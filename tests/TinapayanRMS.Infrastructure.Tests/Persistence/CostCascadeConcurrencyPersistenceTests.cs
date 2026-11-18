using System.Data.Common;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Two saves that change costs at once, as two PCs on one database can make them. Each cascades in its own transaction,
/// and the second takes its turn behind the first (Transactions.BeginOrJoinTransactionAsync's lock), so it works its made
/// and linked ingredients out from what the first committed: neither leaves a stored cost worked out from stale inputs.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class CostCascadeConcurrencyPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Two_price_changes_saved_at_once_both_reach_the_made_ingredient()
    {
        // Syrup is made from 1 kg of flour and 1 kg of sugar, each at 0.05 a gram.
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar");
        await SetPriceAsync(sugarId, unitId: null, price: 0.05m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(flourId, 1000m), (sugarId, 1000m)]);

        // The flour's new price is held as it's about to commit, its cascade done; the sugar's is saved meanwhile.
        var flourHeld = new HeldCommit();
        var flourSave = new RawMaterialBrandPriceRepository(FactoryWith(flourHeld))
            .SetAsync(flourId, [new RawMaterialBrandPrice { Price = 0.07m }], Cancellation);
        await flourHeld.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        var sugarSave = CreateBrandPriceRepository().SetAsync(sugarId, [new RawMaterialBrandPrice { Price = 0.09m }], Cancellation);
        await UntilASaveWaitsOnAnotherAsync();
        flourHeld.Release();
        await Task.WhenAll(flourSave, sugarSave);

        // Both new prices are in Syrup's cost, (0.07 + 0.09) / 2 a gram, as Recalculate Costs works it out.
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation)).CostPerBaseUnit.Should().Be(0.08m);
        (await new CostRecalculator(CreateDbContextFactory()).RecalculateAllAsync(Cancellation)).IngredientsCorrected
            .Should().Be(0, "no stored cost was worked out from stale inputs");
    }

    /// <summary>Until a connection to the test database waits on a lock another holds: the second save, behind the first.</summary>
    private async Task UntilASaveWaitsOnAnotherAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var dbContext = CreateDbContext())
            {
                var waiting = await dbContext.Database
                    .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
                    .SingleAsync(Cancellation);
                if (waiting > 0)
                {
                    return;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The second save never waited on the first.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation);
        }
    }

    private OptionsFactory FactoryWith(IInterceptor interceptor)
    {
        string connectionString;
        using (var dbContext = CreateDbContext())
        {
            connectionString = dbContext.Database.GetConnectionString()!;
        }

        var options = new DbContextOptionsBuilder<TinapayanRMSDbContext>();
        TinapayanRMSDbContext.Configure(options, connectionString).AddInterceptors(interceptor);
        return new OptionsFactory(options.Options);
    }

    private sealed class OptionsFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }

    /// <summary>Holds a transaction as it's about to commit, everything in it written, until <see cref="Release"/>.</summary>
    private sealed class HeldCommit : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => _reached.Task;

        public void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _reached.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            return result;
        }
    }
}
