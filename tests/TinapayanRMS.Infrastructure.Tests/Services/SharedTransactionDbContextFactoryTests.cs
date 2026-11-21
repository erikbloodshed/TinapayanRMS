using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// The unit of work (IUnitOfWork, SharedTransactionDbContextFactory): repositories' writes, each in a transaction of its
/// own, made inside one run commit together or not at all, and its reads see what it has written so far.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class SharedTransactionDbContextFactoryTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static ProductVersionContent Recipe(string name) => new()
    {
        ProductName = name,
        CurrentPrice = 5m,
        Sections =
        [
            new VersionSection(
                1,
                "Pandesal Dough",
                false,
                new VersionRole(1, "Dough", 1, false, false),
                UsageAuthoredField.WeightPerPiece,
                60m,
                [new VersionLine(1, 500m, true, null)],
                []),
        ],
        Materials = [],
    };

    [Test]
    public async Task Writes_made_in_a_run_that_returns_are_kept()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var versions = new ProductVersionRepository(CreateDbContextFactory());

        await CreateUnitOfWork().RunAsync(async () =>
        {
            await products.RenameAsync(productId, "Pandesal Classic", Cancellation);
            return await versions.AddAsync(productId, Recipe("Pandesal Classic"), DateTime.Now, null, Cancellation);
        }, Cancellation);

        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal Classic");
        (await versions.GetForProductAsync(productId, Cancellation)).Should().ContainSingle();
    }

    [Test]
    public async Task A_run_that_throws_keeps_none_of_its_writes_even_those_their_repositories_committed()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var versions = new ProductVersionRepository(CreateDbContextFactory());

        var run = () => CreateUnitOfWork().RunAsync<int>(async () =>
        {
            await products.RenameAsync(productId, "Pandesal Classic", Cancellation);
            await versions.AddAsync(productId, Recipe("Pandesal Classic"), DateTime.Now, null, Cancellation);
            throw new BusinessRuleException("The version that follows couldn't be recorded.");
        }, Cancellation);

        await run.Should().ThrowAsync<BusinessRuleException>();
        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal");
        (await versions.HasAnyAsync(productId, Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task A_read_in_a_run_sees_what_the_run_has_written()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());

        var nameRead = await CreateUnitOfWork().RunAsync(async () =>
        {
            await products.RenameAsync(productId, "Pandesal Classic", Cancellation);
            return (await products.GetByIdAsync(productId, Cancellation))!.Name;
        }, Cancellation);

        nameRead.Should().Be("Pandesal Classic");
    }

    [Test]
    public async Task A_run_inside_a_run_is_part_of_it()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var unitOfWork = CreateUnitOfWork();

        var run = () => unitOfWork.RunAsync<int>(async () =>
        {
            await CreateUnitOfWork().RunAsync(async () =>
            {
                await products.RenameAsync(productId, "Pandesal Classic", Cancellation);
                return 0;
            }, Cancellation);
            throw new BusinessRuleException("Refused after the inner run returned.");
        }, Cancellation);

        await run.Should().ThrowAsync<BusinessRuleException>();
        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal");
    }

    [Test]
    public async Task A_snapshot_reads_as_of_its_first_query_whatever_commits_after()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var cancellation = Cancellation;

        var (before, after) = await CreateUnitOfWork().ReadSnapshotAsync(async () =>
        {
            var before = (await products.GetByIdAsync(productId, cancellation))!.Name;
            await OutsideAsync(() => products.RenameAsync(productId, "Pandesal Classic", cancellation));
            return (before, (await products.GetByIdAsync(productId, cancellation))!.Name);
        }, Cancellation);

        (before, after).Should().Be(("Pandesal", "Pandesal"));
        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal Classic", "the rename was committed all the same");
    }

    [Test]
    public async Task A_write_in_a_snapshot_is_refused()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());

        var writing = () => CreateUnitOfWork().ReadSnapshotAsync(async () =>
        {
            await products.RenameAsync(productId, "Pandesal Classic", Cancellation);
            return 0;
        }, Cancellation);

        (await writing.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>())
            .Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.ReadOnlySqlTransaction);
        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal");
    }

    // A run that writes takes its turn as it begins, so a save that changes costs, made beside it, waits for it to commit and
    // writes after it: it can't commit between the run's reads and its writes (CostCascadeConcurrencyPersistenceTests).
    [Test]
    public async Task A_save_beside_a_run_that_writes_waits_until_the_run_has_committed()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var cancellation = Cancellation;
        Task? beside = null;

        var readFirst = await CreateUnitOfWork().RunAsync(async () =>
        {
            var name = (await products.GetByIdAsync(productId, cancellation))!.Name;
            beside = OutsideAsync(() => products.RenameAsync(productId, "Pandesal Classic", cancellation));
            await UntilAConnectionWaitsOnALockAsync();
            await products.RenameAsync(productId, name + " Special", cancellation);
            return name;
        }, Cancellation);
        await beside!;

        readFirst.Should().Be("Pandesal", "nothing committed between the run's read and its write");
        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal Classic", "the save beside it wrote after the run");
    }

    // A write that changes no cost takes no turn (a category's), so it can still commit between a run's read and its write,
    // and PostgreSQL cancels the run's: it's run again, and sees what the other committed.
    [Test]
    public async Task A_run_cancelled_for_a_save_beside_it_is_run_again_and_sees_what_that_save_committed()
    {
        var categories = new ProductCategoryRepository(CreateDbContextFactory());
        await categories.AddAsync(new Domain.Entities.ProductCategory { Name = "Breads" }, Cancellation);
        var categoryId = (await categories.GetAllAsync(Cancellation)).Single().Id;
        var cancellation = Cancellation;
        var attempts = 0;

        var readFirst = await CreateUnitOfWork().RunAsync(async () =>
        {
            attempts++;
            var name = (await categories.GetAllAsync(cancellation)).Single().Name;
            if (attempts == 1)
            {
                // Committed after the run read the category: its own write to it can't be serialized after it.
                await OutsideAsync(() => categories.UpdateAsync(new Domain.Entities.ProductCategory { Id = categoryId, Name = "Loaves" }, cancellation));
            }

            await categories.UpdateAsync(new Domain.Entities.ProductCategory { Id = categoryId, Name = name + " and Rolls" }, cancellation);
            return name;
        }, Cancellation);

        attempts.Should().Be(2);
        readFirst.Should().Be("Loaves", "the second try read what the other save committed");
        (await categories.GetAllAsync(Cancellation)).Single().Name.Should().Be("Loaves and Rolls");
    }

    [Test]
    public async Task A_run_cancelled_every_time_gives_up_after_its_attempts_with_what_postgresql_said()
    {
        var categories = new ProductCategoryRepository(CreateDbContextFactory());
        await categories.AddAsync(new Domain.Entities.ProductCategory { Name = "Breads" }, Cancellation);
        var categoryId = (await categories.GetAllAsync(Cancellation)).Single().Id;
        var cancellation = Cancellation;
        var attempts = 0;

        var run = () => CreateUnitOfWork().RunAsync(async () =>
        {
            attempts++;
            var name = (await categories.GetAllAsync(cancellation)).Single().Name;
            await OutsideAsync(() => categories.UpdateAsync(new Domain.Entities.ProductCategory { Id = categoryId, Name = $"Breads {attempts}" }, cancellation));
            await categories.UpdateAsync(new Domain.Entities.ProductCategory { Id = categoryId, Name = name + " and Rolls" }, cancellation);
            return 0;
        }, Cancellation);

        (await run.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>())
            .Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.SerializationFailure);
        attempts.Should().Be(Infrastructure.Services.SharedTransactionDbContextFactory.Attempts);
    }

    [Test]
    public async Task A_run_refused_by_a_rule_isnt_run_again()
    {
        var attempts = 0;

        var run = () => CreateUnitOfWork().RunAsync<int>(() =>
        {
            attempts++;
            throw new BusinessRuleException("Refused.");
        }, Cancellation);

        await run.Should().ThrowAsync<BusinessRuleException>();
        attempts.Should().Be(1);
    }

    /// <summary>Until a connection to the test database waits on a lock another holds: the save beside the run, behind it.</summary>
    private async Task UntilAConnectionWaitsOnALockAsync()
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
                throw new TimeoutException("The save beside the run never waited on it.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> without the run this is called in, as another copy of the app saves. TestContext.Current
    /// doesn't flow there either, so <paramref name="work"/> takes its cancellation from its caller.
    /// </summary>
    private static Task OutsideAsync(Func<Task> work)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(work, CancellationToken.None);
        }
    }

    [Test]
    public async Task After_a_run_that_threw_a_repository_writes_on_its_own_again()
    {
        var productId = await AddProductAsync("Pandesal");
        var products = new ProductRepository(CreateDbContextFactory());
        var run = () => CreateUnitOfWork().RunAsync<int>(() => throw new BusinessRuleException("Refused."), Cancellation);
        await run.Should().ThrowAsync<BusinessRuleException>();

        await products.RenameAsync(productId, "Pandesal Classic", Cancellation);

        (await products.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Pandesal Classic");
    }
}
