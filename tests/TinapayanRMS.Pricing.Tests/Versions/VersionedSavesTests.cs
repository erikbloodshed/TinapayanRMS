using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Pricing.Tests.Fixtures;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Pricing.Tests.Versions;

/// <summary>
/// The saves that record versions (VersionedSaves), each written with its versions or not at all: a version that can't be
/// recorded takes its save back. The versions a save records, and their notes, are in the screens' *VersionTests.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class VersionedSavesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private const string Refused = "Simulated: the version couldn't be written.";

    [Test]
    public async Task SaveProduct_records_how_it_stood_then_the_product_as_saved()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var products = new ProductRepository(CreateDbContextFactory());
        var product = (await products.GetByIdAsync(pandesalId, Cancellation))!;
        var priceBefore = product.CurrentPrice;
        product.CurrentPrice = 7m;

        var saved = await CreateSaves().SaveProductAsync(product, null, null, null, null, null, false, DateTime.Now, Cancellation);

        saved.Product.CurrentPrice.Should().Be(7m);
        saved.Detail.Usages.Should().ContainSingle();
        var versions = await new ProductVersionRepository(CreateDbContextFactory()).GetForProductAsync(pandesalId, Cancellation);
        versions.Select(v => v.Note).Should().Equal(null, ProductVersionRules.BaselineNote);
        versions.Select(v => ProductVersionContent.FromJson(v.Content).CurrentPrice).Should().Equal(7m, priceBefore);
    }

    [Test]
    public async Task ApplyCosting_changes_and_versions_only_the_products_not_at_it_already()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [new RecipeLine(flourId, 400m)]);
        var costings = new CostingParametersRepository(CreateDbContextFactory());
        await costings.SetAsync(ensaymadaId, 35m, 25m, 15m, 60m, Cancellation);
        var defaults = new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m };

        var changed = await CreateSaves().ApplyCostingAsync(Each(defaults, pandesalId, ensaymadaId), DateTime.Now, Cancellation);

        changed.Should().Equal(["Pandesal"], "Ensaymada had the defaults already");
        var pandesal = (await costings.GetForProductAsync(pandesalId, Cancellation))!;
        (pandesal.LaborPercent, pandesal.OverheadPercent, pandesal.AdministrativePercent, pandesal.MarkupPercent).Should().Be((35m, 25m, 15m, 60m));
        var versions = new ProductVersionRepository(CreateDbContextFactory());
        (await versions.GetForProductAsync(pandesalId, Cancellation)).Select(v => v.Note)
            .Should().Equal(VersionedSaves.CostingDefaultsNote, ProductVersionRules.BaselineNote);
        (await versions.GetForProductAsync(ensaymadaId, Cancellation)).Should().BeEmpty("nothing about it changed");
    }

    [Test]
    public async Task ApplyCosting_to_several_versions_each_changed_one_with_a_baseline_only_when_it_had_none()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [new RecipeLine(flourId, 400m)]);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [new RecipeLine(flourId, 300m)]);
        var (spanishBreadId, _) = await AddProductWithSubAssemblyAsync("Spanish Bread", doughRoleId, [new RecipeLine(flourId, 200m)]);
        var costings = new CostingParametersRepository(CreateDbContextFactory());
        await costings.SetAsync(ensaymadaId, 35m, 25m, 15m, 60m, Cancellation);
        await costings.SetAsync(spanishBreadId, 10m, 5m, 5m, 40m, Cancellation);
        await costings.SetAsync(cheeseRollId, 20m, 10m, 5m, 50m, Cancellation);
        var products = new ProductRepository(CreateDbContextFactory());
        var cheeseRoll = (await products.GetByIdAsync(cheeseRollId, Cancellation))!;
        cheeseRoll.CurrentPrice = 9m;
        await CreateSaves().SaveProductAsync(cheeseRoll, null, null, null, null, null, false, DateTime.Now, Cancellation);
        var ensaymadaSavedAt = (await products.GetByIdAsync(ensaymadaId, Cancellation))!.UpdatedAt;
        var defaults = new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m };
        var appliedAt = DateTime.Now;

        // Given out of the list's order, and Pandesal twice: the names come back in the order given, each once.
        var changed = await CreateSaves().ApplyCostingAsync(Each(defaults, cheeseRollId, ensaymadaId, pandesalId, spanishBreadId, pandesalId), appliedAt, Cancellation);

        changed.Should().Equal(["Cheese Roll", "Pandesal", "Spanish Bread"], "Ensaymada had the defaults already");
        foreach (var id in new[] { pandesalId, cheeseRollId, spanishBreadId })
        {
            var costing = (await costings.GetForProductAsync(id, Cancellation))!;
            (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarkupPercent).Should().Be((35m, 25m, 15m, 60m));
            (await products.GetByIdAsync(id, Cancellation))!.UpdatedAt.Should().BeOnOrAfter(appliedAt.AddSeconds(-1), "it's stamped as saved");
        }

        (await products.GetByIdAsync(ensaymadaId, Cancellation))!.UpdatedAt.Should().Be(ensaymadaSavedAt, "nothing about it changed");
        var versions = new ProductVersionRepository(CreateDbContextFactory());
        (await versions.GetForProductAsync(ensaymadaId, Cancellation)).Should().BeEmpty();
        (await versions.GetForProductAsync(cheeseRollId, Cancellation)).Select(v => v.Note)
            .Should().Equal([VersionedSaves.CostingDefaultsNote, null, ProductVersionRules.BaselineNote], "it had versions, so no baseline goes in again");
        var spanishBread = await versions.GetForProductAsync(spanishBreadId, Cancellation);
        spanishBread.Select(v => v.Note).Should().Equal(VersionedSaves.CostingDefaultsNote, ProductVersionRules.BaselineNote);
        spanishBread.Select(v => ProductVersionContent.FromJson(v.Content).Costing!.LaborPercent).Should().Equal([35m, 10m], "its baseline is how it stood before");
        spanishBread[0].SavedAt.Should().BeCloseTo(appliedAt, TimeSpan.FromMilliseconds(1), "PostgreSQL keeps microseconds");
        (await versions.GetForProductAsync(pandesalId, Cancellation)).Select(v => v.Note)
            .Should().Equal(VersionedSaves.CostingDefaultsNote, ProductVersionRules.BaselineNote);
    }

    [Test]
    public async Task ApplyCosting_writes_each_product_the_costing_given_for_it_in_one_go()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var (ubeCakeId, _) = await AddProductWithSubAssemblyAsync("Ube Cake", doughRoleId, [new RecipeLine(flourId, 400m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [new RecipeLine(flourId, 300m)]);
        var costings = new CostingParametersRepository(CreateDbContextFactory());
        await costings.SetAsync(ensaymadaId, 50m, 25m, 15m, 80m, Cancellation);
        var settings = new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m };
        var cakes = new CostingParameters { LaborPercent = 50m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 80m };

        var changed = await CreateSaves().ApplyCostingAsync([(pandesalId, settings), (ubeCakeId, cakes), (ensaymadaId, cakes)], DateTime.Now, Cancellation);

        changed.Should().Equal(["Pandesal", "Ube Cake"], "Ensaymada had what it was given already");
        async Task<(decimal, decimal, decimal, decimal)> CostingOf(int id) =>
            (await costings.GetForProductAsync(id, Cancellation)) is { } c ? (c.LaborPercent, c.OverheadPercent, c.AdministrativePercent, c.MarkupPercent) : default;
        (await CostingOf(ubeCakeId)).Should().Be((50m, 25m, 15m, 80m));
        (await CostingOf(pandesalId)).Should().Be((35m, 25m, 15m, 60m));
    }

    /// <summary>Each of <paramref name="productIds"/> given <paramref name="costing"/>, as Apply Costing Defaults gives products of one category.</summary>
    private static IReadOnlyList<(int ProductId, CostingParameters Costing)> Each(CostingParameters costing, params int[] productIds) =>
        [.. productIds.Select(id => (id, costing))];

    [Test]
    public async Task ApplyCosting_whose_versions_are_refused_changes_no_costing()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var costings = new CostingParametersRepository(CreateDbContextFactory());
        await costings.SetAsync(pandesalId, 10m, 5m, 5m, 40m, Cancellation);
        var defaults = new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m };

        var apply = () => CreateSaves(refuseVersions: true).ApplyCostingAsync(Each(defaults, pandesalId), DateTime.Now, Cancellation);

        await apply.Should().ThrowAsync<TimeoutException>().WithMessage(Refused);
        (await costings.GetForProductAsync(pandesalId, Cancellation))!.LaborPercent.Should().Be(10m);
    }

    [Test]
    public async Task SaveProduct_whose_version_is_refused_saves_nothing()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var products = new ProductRepository(CreateDbContextFactory());

        // A version to start from, so its Save writes the product before the version that's refused.
        var versions = new ProductVersionRepository(CreateDbContextFactory());
        await CreateSaves().SaveProductAsync((await products.GetByIdAsync(pandesalId, Cancellation))!, null, null, null, null, null, false, DateTime.Now, Cancellation);
        var versionCount = (await versions.GetForProductAsync(pandesalId, Cancellation)).Count;

        var product = (await products.GetByIdAsync(pandesalId, Cancellation))!;
        product.Name = "Pandesal Classic";
        var save = () => CreateSaves(refuseVersions: true).SaveProductAsync(product, null, null, null, null, null, false, DateTime.Now, Cancellation);

        await save.Should().ThrowAsync<TimeoutException>().WithMessage(Refused);
        (await products.GetByIdAsync(pandesalId, Cancellation))!.Name.Should().Be("Pandesal");
        (await versions.GetForProductAsync(pandesalId, Cancellation)).Should().HaveCount(versionCount);
    }

    [Test]
    public async Task SaveSubAssembly_whose_products_versions_are_refused_saves_nothing()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());

        var save = () => CreateSaves(refuseVersions: true).SaveSubAssemblyAsync(
            doughId,
            "Pandesal Dough",
            cancellationToken => subAssemblies.SaveStandaloneAsync(
                new SubAssembly { Id = doughId, Name = "Pandesal Dough", RoleId = doughRoleId }, [new RecipeLine(flourId, 450m)], cancellationToken),
            DateTime.Now,
            Cancellation);

        await save.Should().ThrowAsync<TimeoutException>().WithMessage(Refused);
        var lines = await new SubAssemblyLineRepository(CreateDbContextFactory()).GetForSubAssembliesAsync([doughId], Cancellation);
        lines.Single().Grams.Should().Be(500m);
    }

    [Test]
    public async Task MakeProductFrom_whose_first_version_is_refused_makes_nothing()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var cloner = new ProductCloner(CreateDbContextFactory());

        var make = () => CreateSaves(refuseVersions: true).MakeProductFromAsync(
            pandesalId, "Pandesal Copy", cloner.CloneAsync, "Cloned from", DateTime.Now, Cancellation);

        await make.Should().ThrowAsync<TimeoutException>().WithMessage(Refused);
        (await new ProductRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Select(p => p.Name).Should().Equal("Pandesal");
    }

    private VersionedSaves CreateSaves(bool refuseVersions = false)
    {
        var factory = CreateDbContextFactory();
        var saves = VersionRecorders.CreateSaves(factory, CreateUnitOfWork());
        if (!refuseVersions)
        {
            return saves;
        }

        var productRepository = new ProductRepository(factory);
        var versions = new RefusingVersions(new ProductVersionRepository(factory));
        var reader = new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            productRepository,
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
        return new VersionedSaves(
            CreateUnitOfWork(), productRepository, versions, new CostingParametersRepository(factory), reader, new ProductVersionRecorder(reader, productRepository, versions));
    }

    /// <summary>The real version repository, except that every version it's asked to write is refused, as a database might.</summary>
    private sealed class RefusingVersions(IProductVersionRepository inner) : IProductVersionRepository
    {
        public Task<IReadOnlyList<ProductVersion>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
            inner.GetForProductAsync(productId, cancellationToken);

        public Task<bool> HasAnyAsync(int productId, CancellationToken cancellationToken) => inner.HasAnyAsync(productId, cancellationToken);

        public Task<IReadOnlyList<ProductVersion>> GetLatestAsOfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            inner.GetLatestAsOfAsync(asOf, cancellationToken);

        public Task<ProductVersion?> AddAsync(
            int productId, ProductVersionContent content, DateTime savedAt, string? note, CancellationToken cancellationToken) =>
            throw new TimeoutException(Refused);

        public Task<IReadOnlyList<ProductVersion?>> AddRangeAsync(IReadOnlyList<ProductVersionDraft> drafts, CancellationToken cancellationToken) =>
            throw new TimeoutException(Refused);

        public Task SetNoteAsync(int versionId, string? note, CancellationToken cancellationToken) => inner.SetNoteAsync(versionId, note, cancellationToken);
    }
}
