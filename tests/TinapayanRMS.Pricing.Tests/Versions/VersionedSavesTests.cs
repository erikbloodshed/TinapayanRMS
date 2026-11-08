using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
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

        var saved = await CreateSaves().SaveProductAsync(product, null, null, null, null, false, DateTime.Now, Cancellation);

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
        var defaults = new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarginPercent = 60m };

        var changed = await CreateSaves().ApplyCostingAsync([pandesalId, ensaymadaId], defaults, DateTime.Now, Cancellation);

        changed.Should().Equal(["Pandesal"], "Ensaymada had the defaults already");
        var pandesal = (await costings.GetForProductAsync(pandesalId, Cancellation))!;
        (pandesal.LaborPercent, pandesal.OverheadPercent, pandesal.AdministrativePercent, pandesal.MarginPercent).Should().Be((35m, 25m, 15m, 60m));
        var versions = new ProductVersionRepository(CreateDbContextFactory());
        (await versions.GetForProductAsync(pandesalId, Cancellation)).Select(v => v.Note)
            .Should().Equal(VersionedSaves.CostingDefaultsNote, ProductVersionRules.BaselineNote);
        (await versions.GetForProductAsync(ensaymadaId, Cancellation)).Should().BeEmpty("nothing about it changed");
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
        await CreateSaves().SaveProductAsync((await products.GetByIdAsync(pandesalId, Cancellation))!, null, null, null, null, false, DateTime.Now, Cancellation);
        var versionCount = (await versions.GetForProductAsync(pandesalId, Cancellation)).Count;

        var product = (await products.GetByIdAsync(pandesalId, Cancellation))!;
        product.Name = "Pandesal Classic";
        var save = () => CreateSaves(refuseVersions: true).SaveProductAsync(product, null, null, null, null, false, DateTime.Now, Cancellation);

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
