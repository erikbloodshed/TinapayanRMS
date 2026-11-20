using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.SubAssemblies;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.SubAssemblies;

/// <summary>
/// The Sub-Assemblies editor on the real repositories: what it refuses, in the order a Save asks, what a Save writes, and
/// what it says a Save does to the products linking a recipe. The screen's own tests (Presentation.Tests' SubAssemblies)
/// drive it through the form.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class SubAssemblyEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public void A_sub_assembly_needs_a_name_and_a_role()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid("", 1)).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid("Sweet Dough", null)).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid("Sweet Dough", 1)).Should().NotThrow();
    }

    [Test]
    public async Task A_save_with_no_lines_is_refused_saying_what_to_add()
    {
        var editor = CreateEditor();

        await editor.Awaiting(e => e.CheckSaveAsync(Request(0, "Sweet Dough", 1, lines: []), Cancellation))
            .Should().ThrowAsync<BusinessRuleException>().WithMessage("Add the ingredients that go into Sweet Dough, in grams.");
        await editor.Awaiting(e => e.CheckSaveAsync(PackagingRequest(0, "Box Set", 1, lines: []), Cancellation))
            .Should().ThrowAsync<BusinessRuleException>().WithMessage("Add the packaging materials that go into Box Set, per set.");
        await editor.Awaiting(e => e.SaveAsync(Request(0, "Sweet Dough", 1, lines: []), DateTime.Now, Cancellation))
            .Should().ThrowAsync<BusinessRuleException>("a Save checks again, whoever calls it");
    }

    [Test]
    public async Task A_new_recipe_is_saved_with_its_lines_as_typed()
    {
        var roleId = await AddRoleAsync("Dough");
        var flour = await IngredientAsync("Bread Flour", 0.05m);
        var water = await IngredientAsync("Water", 0m);

        var saved = await CreateEditor().SaveAsync(
            Request(0, "Sweet Dough", roleId, [new(flour, 1000m, true, null), new(water, 650m, false, 65m)]), DateTime.Now, Cancellation);

        var content = await CreateQueries().ReadContentAsync(saved.Id, isPackagingSet: false, Cancellation);
        content.Lines.Select(l => (l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)).Should().Equal(
            (flour.Id, 1000m, true, (decimal?)null), (water.Id, 650m, false, (decimal?)65m));
        content.PackagingLines.Should().BeEmpty();
    }

    [Test]
    public async Task A_packaging_set_is_saved_with_what_one_set_takes()
    {
        var roleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Box");

        var saved = await CreateEditor().SaveAsync(PackagingRequest(0, "Box Set", roleId, [new(boxId, null, 2m)]), DateTime.Now, Cancellation);

        var content = await CreateQueries().ReadContentAsync(saved.Id, isPackagingSet: true, Cancellation);
        content.PackagingLines.Select(l => (l.PackagingMaterialId, l.QtyPerSet)).Should().Equal((boxId, 2m));
    }

    [Test]
    public async Task A_save_is_refused_over_one_made_since_the_form_took_it()
    {
        var roleId = await AddRoleAsync("Dough");
        var flour = await IngredientAsync("Bread Flour", 0.05m);
        var editor = CreateEditor();
        var saved = await editor.SaveAsync(Request(0, "Sweet Dough", roleId, [new(flour, 1000m, false, null)]), DateTime.Now, Cancellation);
        var openedAt = (await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneAsync(Cancellation)).Single().UpdatedAt;

        // Another PC saves it after this form took it.
        await editor.SaveAsync(Request(saved.Id, "Sweet Dough", roleId, [new(flour, 900m, false, null)], openedAt), DateTime.Now, Cancellation);

        await editor.Awaiting(e => e.SaveAsync(Request(saved.Id, "Sweet Dough", roleId, [new(flour, 800m, false, null)], openedAt), DateTime.Now, Cancellation))
            .Should().ThrowAsync<SavedSinceException>();
    }

    [Test]
    public async Task A_check_says_what_new_lines_do_to_each_linking_products_base_cost()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);

        // Pandesal x 8 takes a tenth of the batch: 25.00 of flour, so 2.50 and the shop's 3% wastage, 2.575; 450 g makes 2.3175.
        var packId = await AddProductAsync("Pandesal x 8");
        await SaveRecipeAsync(packId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.Yield, 10m)]));
        var flour = (await new RawMaterialRepository(CreateDbContextFactory()).GetByIdAsync(flourId, Cancellation))!;

        var impacts = await CreateEditor(asksAboutCosts: true).CheckSaveAsync(
            Request(doughId, "Pandesal Dough", doughRoleId, [new(flour, 450m, false, null)]), Cancellation);

        var impact = impacts.Should().ContainSingle().Which;
        impact.ProductName.Should().Be("Pandesal x 8");
        (Math.Round(impact.BaseCostBefore, 2, MidpointRounding.AwayFromZero), Math.Round(impact.BaseCostAfter, 2, MidpointRounding.AwayFromZero))
            .Should().Be((2.58m, 2.32m));
    }

    [Test]
    public async Task A_check_asks_nothing_of_a_new_recipe()
    {
        var roleId = await AddRoleAsync("Dough");
        var flour = await IngredientAsync("Bread Flour", 0.05m);

        var impacts = await CreateEditor(asksAboutCosts: true).CheckSaveAsync(Request(0, "Sweet Dough", roleId, [new(flour, 1000m, false, null)]), Cancellation);

        impacts.Should().BeEmpty("no product links a sub-assembly not saved yet");
    }

    private static SubAssemblySaveRequest Request(int id, string name, int roleId, IReadOnlyList<IngredientLineDraft> lines, DateTime? savedVersion = null) =>
        new(id, name, roleId, false, savedVersion, lines, []);

    private static SubAssemblySaveRequest PackagingRequest(int id, string name, int roleId, IReadOnlyList<PackagingLineDraft> lines) =>
        new(id, name, roleId, true, null, [], lines);

    private async Task<RawMaterial> IngredientAsync(string name, decimal costPerGram)
    {
        var id = await AddIngredientAsync(name, costPerGram);
        return (await new RawMaterialRepository(CreateDbContextFactory()).GetByIdAsync(id, Cancellation))!;
    }

    private async Task<int> AddPackagingAsync(string name)
    {
        var material = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Packaging,
            CategoryId = await AddPackagingCategoryAsync(),
            BaseUnitId = await GetOrCreateUnitOfMeasureAsync("Piece", "pc"),
            Source = RawMaterialSource.Purchased,
        };
        await new RawMaterialRepository(CreateDbContextFactory()).AddAsync(material, Cancellation);
        return material.Id;
    }

    private SubAssemblyQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new SubAssemblyQueries(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory));
    }

    private SubAssemblyEditor CreateEditor(bool asksAboutCosts = false)
    {
        var factory = CreateDbContextFactory();
        var productRepository = new ProductRepository(factory);
        return new SubAssemblyEditor(
            new SubAssemblyRepository(factory),
            new SubAssemblyValidator(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            asksAboutCosts
                ? new SharedRecipeImpact(
                    new SubAssemblyRepository(factory),
                    productRepository,
                    new SavedProductReader(
                        new PackagingLinkRepository(factory),
                        new ProductSubAssemblyUsageRepository(factory),
                        new PackagingLineRepository(factory),
                        new CostingParametersRepository(factory),
                        new SubAssemblyLineRepository(factory),
                        productRepository,
                        new CostingDefaultsRepository(factory),
                        CreateUnitOfWork()))
                : null);
    }
}
