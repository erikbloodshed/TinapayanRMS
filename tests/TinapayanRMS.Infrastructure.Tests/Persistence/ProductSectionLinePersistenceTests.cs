using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A section's lines that are others of the product's sections (UsageSectionLine), as the recipe's Save writes them
/// (SetRecipeAsync): named in the draft by a saved sub-assembly's id or a new section's stand-in, stored by the sub-assembly,
/// read back with the add-ins in one command, checked before anything is written, and carried by Clone and Create Variant.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductSectionLinePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private ProductCloner CreateCloner() => new(CreateDbContextFactory());

    /// <summary>Each of the product's lines that is a section, as "Dough: Sponge 300 at 1", by role names.</summary>
    private async Task<List<string>> SectionLinesAsync(int productId)
    {
        var usages = await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(productId, Cancellation);
        var lines = (await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetLinesForProductAsync(productId, Cancellation)).SectionLines;
        string RoleOf(int subAssemblyId) => usages.Single(u => u.SubAssemblyId == subAssemblyId).SubAssembly!.Role!.Name;
        return [.. lines.Select(l => $"{RoleOf(usages.Single(u => u.Id == l.UsageId).SubAssemblyId)}: {RoleOf(l.SectionSubAssemblyId)} {l.Grams:0.##} at {l.Position}")];
    }

    /// <summary>A product with a new dough and a new sponge of its own (product-owned roles), 300 g of the sponge on the dough's lines.</summary>
    private async Task<(int ProductId, int FlourId)> SeedSpongeInDoughAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true, canGoIntoOtherSections: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m),
            ],
            []));
        return (productId, flourId);
    }

    [Test]
    public async Task A_new_section_can_be_on_another_new_ones_lines_named_by_its_stand_in()
    {
        var (productId, _) = await SeedSpongeInDoughAsync();

        (await SectionLinesAsync(productId)).Should().Equal("Dough: Sponge 300 at 1");
    }

    [Test]
    public async Task Saving_again_by_the_saved_ids_keeps_it_and_saving_without_it_takes_it_away()
    {
        var (productId, flourId) = await SeedSpongeInDoughAsync();
        var (doughRoleId, spongeRoleId) = (await RoleIdAsync("Dough"), await RoleIdAsync("Sponge"));
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);
        var spongeId = await SubAssemblyIdAsync(productId, spongeRoleId);
        NewSubAssemblyDraft Dough(params SectionLineDraft[] lines) =>
            new(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m) { Id = doughId, SectionLines = lines };
        var sponge = new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { Id = spongeId };

        await SaveRecipeAsync(productId, new RecipeDraft([Dough(new SectionLineDraft(spongeId, 250m, 25m, 0)), sponge], []));
        (await SectionLinesAsync(productId)).Should().Equal("Dough: Sponge 250 at 0");

        await SaveRecipeAsync(productId, new RecipeDraft([Dough(), sponge], []));
        (await SectionLinesAsync(productId)).Should().BeEmpty();
    }

    [Test]
    public async Task A_linked_section_can_have_one_of_the_products_own_among_its_add_ins()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true, canGoIntoOtherSections: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pan de Sal", doughRoleId, [(flourId, 1000m)], weightPerPiece: 30m);
        var productId = await AddProductAsync("Spanish Bread");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m)],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m) { SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 0), 400m, null, 0)] }]));

        (await SectionLinesAsync(productId)).Should().Equal("Dough: Sponge 400 at 0");
    }

    [Test]
    public async Task A_loop_is_refused_and_nothing_is_written()
    {
        var doughRoleId = await AddRoleAsync("Dough", isProductOwned: true, canGoIntoOtherSections: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true, canGoIntoOtherSections: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");

        var save = () => SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 0), 10m, null, 1)],
                },
            ],
            []));

        await save.Should().ThrowAsync<BusinessRuleException>().WithMessage("Dough uses Sponge, which uses Dough*");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(u => u.ProductId == productId, Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task A_section_the_recipe_does_not_have_is_refused()
    {
        var doughRoleId = await AddRoleAsync("Dough", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");

        var save = () => SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 5), 300m, null, 1)],
                },
            ],
            []));

        await save.Should().ThrowAsync<BusinessRuleException>().WithMessage("Dough uses a section that isn't in this recipe any more*");
    }

    [Test]
    public async Task A_clone_uses_its_own_copy_and_a_variant_what_its_source_did()
    {
        var (sourceId, _) = await SeedSpongeInDoughAsync();

        var cloneId = await CreateCloner().CloneAsync(sourceId, "Spanish Bread Copy", Cancellation);
        var variantId = await CreateCloner().CreateVariantAsync(sourceId, "Spanish Bread Small", Cancellation);

        (await SectionLinesAsync(cloneId)).Should().Equal("Dough: Sponge 300 at 1");
        var cloneSpongeId = await SubAssemblyIdAsync(cloneId, await RoleIdAsync("Sponge"));
        cloneSpongeId.Should().NotBe(await SubAssemblyIdAsync(sourceId, await RoleIdAsync("Sponge")), "a clone has its own copy");
        (await SectionLinesAsync(variantId)).Should().Equal("Dough: Sponge 300 at 1");
    }

    [Test]
    public async Task An_unlocked_section_saves_and_a_clone_and_a_variant_keep_it_unlocked()
    {
        var (productId, flourId) = await SeedSpongeInDoughAsync();
        var (doughRoleId, spongeRoleId) = (await RoleIdAsync("Dough"), await RoleIdAsync("Sponge"));
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);
        var spongeId = await SubAssemblyIdAsync(productId, spongeRoleId);
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    Id = doughId,
                    SectionLines = [new SectionLineDraft(spongeId, 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.Yield, 1m) { Id = spongeId, IsUnlocked = true },
            ],
            []));

        var cloneId = await CreateCloner().CloneAsync(productId, "Spanish Bread Copy", Cancellation);
        var variantId = await CreateCloner().CreateVariantAsync(productId, "Spanish Bread Small", Cancellation);

        await using var dbContext = CreateDbContext();
        var unlocked = await dbContext.ProductSubAssemblyUsages.Where(u => u.IsUnlocked).Select(u => u.ProductId).ToListAsync(Cancellation);
        unlocked.Should().BeEquivalentTo([productId, cloneId, variantId]);
    }

    [Test]
    public async Task A_product_with_a_section_on_anothers_lines_can_be_deleted_for_good()
    {
        var (productId, _) = await SeedSpongeInDoughAsync();

        await CreateRepository().DeleteAsync(productId, Cancellation);
        await CreateRepository().DeletePermanentlyAsync(productId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.Products.AnyAsync(p => p.Id == productId, Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblies.AnyAsync(s => s.OwnerProductId == productId, Cancellation)).Should().BeFalse();
        (await dbContext.UsageSectionLines.AnyAsync(Cancellation)).Should().BeFalse();
    }

    private async Task<int> RoleIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyRoles.Where(r => r.Name == name).Select(r => r.Id).SingleAsync(Cancellation);
    }
}
