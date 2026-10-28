using AwesomeAssertions;
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
[Collection(DatabaseCollection.Name)]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
