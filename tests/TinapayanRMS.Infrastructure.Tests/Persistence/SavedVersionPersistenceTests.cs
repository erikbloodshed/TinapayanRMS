using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A form's Save says when the record it opened had last been saved, and is refused over a save made since, as another PC
/// on the same database makes one (SavedVersionRules). What moves that time is a save of the record itself, never a cost the
/// cascade moves.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class SavedVersionPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private readonly TestClock _clock = new(new DateTime(2026, 11, 18, 8, 0, 0));

    private ProductRepository Products => new(CreateDbContextFactory(_clock));

    private RawMaterialRepository RawMaterials => new(CreateDbContextFactory(_clock));

    private SubAssemblyRepository SubAssemblies => new(CreateDbContextFactory(_clock));

    private Task<int> SaveAsync(Product product) =>
        Products.SaveAsync(product, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private Task<int> SaveAsync(RawMaterial material) =>
        RawMaterials.SaveAsync(material, [], brandPrices: null, addIns: null, Cancellation);

    private async Task<Product> ProductAsync(int id) => (await Products.GetByIdAsync(id, Cancellation))!;

    private async Task<RawMaterial> RawMaterialAsync(int id) => (await RawMaterials.GetByIdAsync(id, Cancellation))!;

    private async Task<SubAssembly> SubAssemblyAsync(int id) => (await SubAssemblies.GetByIdsAsync([id], Cancellation)).Single();

    private void AMinuteLater() => _clock.Advance(TimeSpan.FromMinutes(1));

    [Test]
    public async Task A_product_saved_since_it_was_opened_refuses_the_save_and_keeps_the_one_made()
    {
        var id = await SaveAsync(new Product { Name = "Mamon" });
        var opened = await ProductAsync(id);

        AMinuteLater();
        var theirs = await ProductAsync(id);
        theirs.Name = "Mamon Classic";
        await SaveAsync(theirs);

        AMinuteLater();
        opened.CurrentPrice = 20m;
        var act = () => SaveAsync(opened);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Mamon Classic"));
        var stored = await ProductAsync(id);
        (stored.Name, stored.CurrentPrice).Should().Be(("Mamon Classic", (decimal?)null));
    }

    [Test]
    public async Task A_stamped_time_is_the_time_read_back_whatever_the_clock_read()
    {
        // A clock reading to the tick, where PostgreSQL keeps whole microseconds. A form's next Save is compared with the time
        // this one stamped, so what's stamped and what's read back have to be one time.
        var clock = new TestClock(new DateTime(2026, 11, 18, 8, 0, 0).AddTicks(1234567));
        var factory = CreateDbContextFactory(clock);
        var products = new ProductRepository(factory);
        await using var dbContext = await factory.CreateDbContextAsync(Cancellation);
        var stamped = dbContext.LocalNow();

        var id = await products.SaveAsync(new Product { Name = "Mamon" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        var saved = (await products.GetByIdAsync(id, Cancellation))!;

        saved.UpdatedAt.Should().Be(stamped);
        saved.CurrentPrice = 20m;
        var act = () => products.SaveAsync(saved, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_raw_material_saved_since_it_was_opened_refuses_the_save_and_keeps_the_one_made()
    {
        var id = await AddIngredientAsync("Flour", 0.05m);
        await SaveAsync(await RawMaterialAsync(id));
        var opened = await RawMaterialAsync(id);

        AMinuteLater();
        var theirs = await RawMaterialAsync(id);
        theirs.Name = "Bread Flour";
        await SaveAsync(theirs);

        AMinuteLater();
        opened.Name = "Cake Flour";
        var act = () => SaveAsync(opened);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Bread Flour"));
        (await RawMaterialAsync(id)).Name.Should().Be("Bread Flour");
    }

    [Test]
    public async Task A_cost_the_cascade_moves_isnt_a_save_of_the_ingredient()
    {
        var sugarId = await AddIngredientAsync("Sugar");
        await SetPriceAsync(sugarId, unitId: null, price: 0.05m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        var opened = await RawMaterialAsync(syrupId);

        // Sugar's new price cascades into the syrup while its form is open.
        await SetPriceAsync(sugarId, unitId: null, price: 0.09m);

        opened.Name = "Simple Syrup";
        var act = () => SaveAsync(opened);

        await act.Should().NotThrowAsync("nobody saved the syrup: only its cost followed the sugar");
        var saved = await RawMaterialAsync(syrupId);
        (saved.Name, saved.CostPerBaseUnit).Should().Be(("Simple Syrup", 0.09m));
    }

    [Test]
    public async Task A_sub_assembly_saved_since_it_was_opened_refuses_the_save_and_keeps_the_lines_saved()
    {
        var (id, flourId) = await AddDoughAsync();
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await SubAssemblies.SaveStandaloneAsync(await SubAssemblyAsync(id), [new RecipeLine(flourId, 200m)], Cancellation);

        AMinuteLater();
        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Dough"));
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.SingleAsync(l => l.SubAssemblyId == id, Cancellation)).Grams.Should().Be(200m);
    }

    [Test]
    public async Task A_sub_assembly_read_back_after_its_save_saves_again()
    {
        var (id, flourId) = await AddDoughAsync();

        AMinuteLater();
        var act = async () => await SubAssemblies.SaveStandaloneAsync(await SubAssemblyAsync(id), [new RecipeLine(flourId, 200m)], Cancellation);

        await act.Should().NotThrowAsync();
        (await SubAssemblyAsync(id)).UpdatedAt.Should().Be(_clock.Now);
    }

    [Test]
    public async Task A_rename_from_the_list_goes_through_and_a_form_opened_before_it_is_refused()
    {
        var (id, flourId) = await AddDoughAsync();
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await SubAssemblies.RenameAsync(id, "Sweet Dough", Cancellation);

        (await SubAssemblyAsync(id)).UpdatedAt.Should().Be(_clock.Now);
        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Sweet Dough"));
    }

    [Test]
    public async Task A_products_save_relining_a_sub_assembly_it_links_is_a_save_of_the_sub_assembly()
    {
        var (id, flourId) = await AddDoughAsync();
        var productId = await AddProductAsync("Cream Loaf");
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await Products.SaveAsync(
            await ProductAsync(productId),
            new RecipeDraft([], [new LinkedUsageDraft(id, UsageAuthoredField.WeightPerPiece, 50m) { Lines = [new RecipeLine(flourId, 500m)] }]),
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);

        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Dough"));
    }

    private async Task<(int Id, int FlourId)> AddDoughAsync()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var roleId = await AddRoleAsync("Base");
        var id = await SubAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Dough", RoleId = roleId }, [new RecipeLine(flourId, 100m)], Cancellation);
        return (id, flourId);
    }
}
