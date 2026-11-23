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
/// A section that goes whole into another of the product's (ProductSubAssemblyUsage.GoesIntoSubAssemblyId), as the recipe's
/// Save writes it (SetRecipeAsync): named in the draft by a saved sub-assembly's id or a new section's stand-in, stored as the
/// sub-assembly, checked before anything is written, and carried by Clone and Create Variant.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class ProductSectionFoldPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private ProductCloner CreateCloner() => new(CreateDbContextFactory());

    /// <summary>Each of the product's sub-assemblies, by role name, with the role name of the one it goes into.</summary>
    private async Task<Dictionary<string, string?>> FoldsAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var usages = await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .Select(u => new { u.SubAssemblyId, Role = u.SubAssembly!.Role!.Name, u.GoesIntoSubAssemblyId })
            .ToListAsync(Cancellation);
        return usages.ToDictionary(
            u => u.Role,
            u => u.GoesIntoSubAssemblyId is { } id ? usages.Single(t => t.SubAssemblyId == id).Role : null);
    }

    /// <summary>A product with a new dough and a new sponge of its own (product-owned roles), the sponge going into the dough.</summary>
    private async Task<(int ProductId, int FlourId)> SeedSpongeIntoDoughAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m),
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { GoesInto = RecipeDraft.SectionIdOf(0, 0) },
            ],
            []));
        return (productId, flourId);
    }

    [Test]
    public async Task A_new_section_can_go_into_another_new_one_named_by_its_stand_in()
    {
        var (productId, _) = await SeedSpongeIntoDoughAsync();

        (await FoldsAsync(productId)).Should().BeEquivalentTo(new Dictionary<string, string?> { ["Dough"] = null, ["Sponge"] = "Dough" });
    }

    [Test]
    public async Task Saving_again_by_the_saved_ids_keeps_it_and_saving_without_it_takes_it_away()
    {
        var (productId, flourId) = await SeedSpongeIntoDoughAsync();
        var (doughRoleId, spongeRoleId) = (await RoleIdAsync("Dough"), await RoleIdAsync("Sponge"));
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);
        var spongeId = await SubAssemblyIdAsync(productId, spongeRoleId);
        NewSubAssemblyDraft Dough() => new(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m) { Id = doughId };
        NewSubAssemblyDraft Sponge(int? goesInto) =>
            new(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { Id = spongeId, GoesInto = goesInto };

        await SaveRecipeAsync(productId, new RecipeDraft([Dough(), Sponge(doughId)], []));
        (await FoldsAsync(productId))["Sponge"].Should().Be("Dough");

        await SaveRecipeAsync(productId, new RecipeDraft([Dough(), Sponge(null)], []));
        (await FoldsAsync(productId))["Sponge"].Should().BeNull();
    }

    [Test]
    public async Task A_section_of_the_products_own_can_go_into_one_it_links()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pan de Sal", doughRoleId, [(flourId, 1000m)], weightPerPiece: 30m);
        var productId = await AddProductAsync("Spanish Bread");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { GoesInto = doughId }],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m)]));

        (await FoldsAsync(productId))["Sponge"].Should().Be("Dough");
    }

    [Test]
    public async Task A_section_going_into_another_with_a_piece_size_is_refused_and_nothing_is_written()
    {
        var doughRoleId = await AddRoleAsync("Dough", isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");

        var save = () => SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m),
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 10m) { GoesInto = RecipeDraft.SectionIdOf(0, 0) },
            ],
            []));

        await save.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sponge goes whole into Dough*");
        (await FoldsAsync(productId)).Should().BeEmpty();
    }

    [Test]
    public async Task A_section_going_into_one_the_recipe_does_not_have_is_refused()
    {
        var spongeRoleId = await AddRoleAsync("Sponge", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");

        var save = () => SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { GoesInto = RecipeDraft.SectionIdOf(0, 5) }],
            []));

        await save.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sponge goes into a section that isn't in this recipe any more*");
    }

    [Test]
    public async Task A_clone_goes_into_its_own_copy_and_a_variant_into_what_its_source_did()
    {
        var (sourceId, _) = await SeedSpongeIntoDoughAsync();

        var cloneId = await CreateCloner().CloneAsync(sourceId, "Spanish Bread Copy", Cancellation);
        var variantId = await CreateCloner().CreateVariantAsync(sourceId, "Spanish Bread Small", Cancellation);

        (await FoldsAsync(cloneId))["Sponge"].Should().Be("Dough");
        var cloneDoughId = await SubAssemblyIdAsync(cloneId, await RoleIdAsync("Dough"));
        cloneDoughId.Should().NotBe(await SubAssemblyIdAsync(sourceId, await RoleIdAsync("Dough")), "a clone has its own copy");
        (await FoldsAsync(variantId))["Sponge"].Should().Be("Dough");
    }

    [Test]
    public async Task A_product_with_a_section_going_into_another_can_be_deleted_for_good()
    {
        var (productId, _) = await SeedSpongeIntoDoughAsync();

        await CreateRepository().DeleteAsync(productId, Cancellation);
        await CreateRepository().DeletePermanentlyAsync(productId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.Products.AnyAsync(p => p.Id == productId, Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblies.AnyAsync(s => s.OwnerProductId == productId, Cancellation)).Should().BeFalse();
    }

    private async Task<int> RoleIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyRoles.Where(r => r.Name == name).Select(r => r.Id).SingleAsync(Cancellation);
    }
}
