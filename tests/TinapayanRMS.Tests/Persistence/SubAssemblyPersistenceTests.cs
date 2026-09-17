using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// Sub-assemblies are made inside the product that owns them and saved with it (ProductRepository.SaveAsync's
/// recipe): their lines in grams and totals, links from other products, and the guards on removing one.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SubAssemblyPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRoleRepository CreateRoleRepository() => new(CreateDbContextFactory());

    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    private SubAssemblyLineRepository CreateLineRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private async Task<SubAssembly> GetSubAssemblyAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation);
    }

    private static OwnedSubAssemblyDraft Owned(int id, int roleId, IReadOnlyList<RecipeLine> lines, decimal weightPerPiece = 10m) =>
        new(id, roleId, lines, UsageAuthoredField.WeightPerPiece, weightPerPiece);

    [Fact]
    public async Task Saving_a_new_sub_assembly_makes_it_the_products_own_and_computes_its_totals()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft([Owned(0, roleId, [(flourId, 800m), (sugarId, 200m)], 50m)], []));

        var subAssembly = await GetSubAssemblyAsync(await OwnedSubAssemblyIdAsync(productId, roleId));
        subAssembly.Name.Should().BeNull("a product's own sub-assembly goes by its role");
        subAssembly.OwnerProductId.Should().Be(productId);
        subAssembly.TotalWeight.Should().Be(1000m);
        subAssembly.TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09
        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(subAssembly.Id);
        usage.AuthoredField.Should().Be(UsageAuthoredField.WeightPerPiece);
        usage.AuthoredValue.Should().Be(50m);
        usage.SubAssembly!.Role!.Name.Should().Be("Dough");
        usage.SubAssembly.OwnerProduct!.Name.Should().Be("Pandesal");
    }

    [Fact]
    public async Task A_usage_can_store_a_typed_yield_instead_of_a_weight_per_piece()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, roleId, [(flourId, 1200m)], UsageAuthoredField.Yield, 24m)], []));

        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield);
        usage.AuthoredValue.Should().Be(24m);
    }

    [Fact]
    public async Task Resaving_replaces_the_lines_moves_a_sub_assembly_to_another_role_and_clears_a_name_from_before()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var waterId = await AddIngredientAsync("Water");
        var (productId, subAssemblyId) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(sugarId, 100m), (waterId, 100m)]);

        // As one saved before a product's own went by its role had it.
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblies.SingleAsync(s => s.Id == subAssemblyId, Cancellation)).Name = "Custard";
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await SaveRecipeAsync(productId, new RecipeDraft([Owned(subAssemblyId, fillingRoleId, [(sugarId, 500m)])], []));

        var subAssembly = await GetSubAssemblyAsync(subAssemblyId);
        subAssembly.Name.Should().BeNull("a product's own goes by its role, so an old name goes with the next save");
        subAssembly.RoleId.Should().Be(fillingRoleId, "the owner can move its sub-assembly to another role");
        subAssembly.TotalWeight.Should().Be(500m);
        subAssembly.TotalCost.Should().Be(45m);
        (await CreateLineRepository().GetForSubAssembliesAsync([subAssemblyId], Cancellation)).Should().ContainSingle().Which.Grams.Should().Be(500m);
    }

    [Fact]
    public async Task A_sub_assembly_left_out_of_the_recipe_is_deleted_with_its_lines()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await SaveRecipeAsync(productId, new RecipeDraft([], []));

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(s => s.Id == custardId, Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblyLines.AnyAsync(l => l.SubAssemblyId == custardId, Cancellation)).Should().BeFalse();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Another_product_can_link_a_sub_assembly_at_its_own_piece_size()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var smallId = await AddProductAsync("Ensaymada Small");

        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var usage = (await CreateUsageRepository().GetForProductAsync(smallId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(doughId);
        usage.AuthoredValue.Should().Be(30m);
        usage.SubAssembly!.OwnerProductId.Should().Be(bigId, "a link is a usage of another product's sub-assembly");
        usage.SubAssembly.OwnerProduct!.Name.Should().Be("Ensaymada Big");
        (await CreateSubAssemblyRepository().GetLinkedByAsync(bigId, Cancellation))[doughId].Should().Equal("Ensaymada Small");
    }

    [Fact]
    public async Task A_product_cant_link_its_own_sub_assembly()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)]);

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 10m)]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't link one*");
    }

    [Fact]
    public async Task Linking_a_nonexistent_sub_assembly_is_rejected()
    {
        var productId = await AddProductAsync("Pandesal");

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(999_999, UsageAuthoredField.WeightPerPiece, 10m)]));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Removing_a_sub_assembly_another_product_links_is_refused_and_changes_nothing()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)]);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var act = () => SaveRecipeAsync(bigId, new RecipeDraft([], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada Small links Ensaymada Big's Dough*");
        (await CreateUsageRepository().GetForProductAsync(bigId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == doughId);
    }

    [Fact]
    public async Task A_recipe_takes_one_sub_assembly_in_each_role_its_own_and_linked_together_but_two_products_can_each_have_one()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var (_, sweetDoughId) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)]);
        var productId = await AddProductAsync("Pandesal");

        var twoOwn = () => SaveRecipeAsync(productId, new RecipeDraft(
            [Owned(0, doughRoleId, [(flourId, 100m)]), Owned(0, doughRoleId, [(flourId, 200m)])], []));
        var ownAndLinked = () => SaveRecipeAsync(productId, new RecipeDraft(
            [Owned(0, doughRoleId, [(flourId, 100m)])], [new LinkedUsageDraft(sweetDoughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        await twoOwn.Should().ThrowAsync<InvalidOperationException>().WithMessage("This recipe has more than one sub-assembly in the Dough role*");
        await ownAndLinked.Should().ThrowAsync<InvalidOperationException>().WithMessage("This recipe has more than one sub-assembly in the Dough role*");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [Owned(0, fillingRoleId, [(flourId, 100m)])], [new LinkedUsageDraft(sweetDoughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        await AddProductWithSubAssemblyAsync("Cream Loaf", doughRoleId, [(flourId, 100m)]);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.CountAsync(s => s.RoleId == doughRoleId, Cancellation)).Should().Be(2, "Ensaymada and Cream Loaf each have their own Dough");
    }

    [Fact]
    public async Task Moving_a_sub_assembly_to_a_role_a_product_linking_it_already_has_is_refused()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", doughRoleId, [(flourId, 1000m)]);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft(
            [Owned(0, fillingRoleId, [(sugarId, 100m)])], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var act = () => SaveRecipeAsync(bigId, new RecipeDraft([Owned(doughId, fillingRoleId, [(flourId, 1000m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ensaymada Small links this sub-assembly and already has one in the Filling role*");
        (await GetSubAssemblyAsync(doughId)).RoleId.Should().Be(doughRoleId);
    }

    [Fact]
    public async Task Saving_a_sub_assembly_that_isnt_the_products_own_is_rejected()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);
        var otherId = await AddProductAsync("Pandesal");

        var act = () => SaveRecipeAsync(otherId, new RecipeDraft([Owned(doughId, roleId, [(flourId, 10m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*isn't one of this product's own*");
    }

    [Fact]
    public async Task A_packaging_material_on_a_line_is_rejected_and_the_existing_lines_survive()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.SingleAsync(Cancellation);
            var box = new RawMaterial { Type = RawMaterialType.Packaging, Name = "Cake Box", CategoryId = category.Id, BaseUnitId = pieceId };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([Owned(custardId, roleId, [(sugarId, 100m), (boxId, 1m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'Cake Box' is packaging*");
        (await CreateLineRepository().GetForSubAssembliesAsync([custardId], Cancellation))
            .Should().ContainSingle("a rejected save rolls back, so the existing lines survive");
    }

    [Fact]
    public async Task A_nonexistent_ingredient_is_rejected_and_the_existing_lines_survive()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([Owned(custardId, roleId, [(sugarId, 50m), (999_999, 50m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await CreateLineRepository().GetForSubAssembliesAsync([custardId], Cancellation)).Should().ContainSingle().Which.Grams.Should().Be(100m);
        (await GetSubAssemblyAsync(custardId)).TotalCost.Should().Be(9m);
    }

    [Fact]
    public async Task Duplicate_ingredients_and_empty_lines_are_rejected()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var productId = await AddProductAsync("Ensaymada");

        var duplicate = () => SaveRecipeAsync(productId, new RecipeDraft([Owned(0, roleId, [(sugarId, 100m), (sugarId, 50m)])], []));
        var empty = () => SaveRecipeAsync(productId, new RecipeDraft([Owned(0, roleId, [])], []));

        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        await empty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one line*");
    }

    [Fact]
    public async Task A_non_positive_piece_size_is_rejected()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var productId = await AddProductAsync("Ensaymada");

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([Owned(0, roleId, [(sugarId, 100m)], weightPerPiece: 0m)], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*must be positive*");
    }

    [Fact]
    public async Task GetForSubAssembliesAsync_returns_the_lines_of_only_the_sub_assemblies_asked_for()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 2);
        var finishingRoleId = await AddRoleAsync("Finishing", sortOrder: 3);
        var sugarId = await AddIngredientAsync("Sugar");
        var milkId = await AddIngredientAsync("Milk");
        var productId = await AddProductAsync("Ensaymada");
        await SaveRecipeAsync(productId, new RecipeDraft(
        [
            Owned(0, fillingRoleId, [(sugarId, 60m), (milkId, 50m)]),
            Owned(0, toppingRoleId, [(milkId, 200m)]),
            Owned(0, finishingRoleId, [(sugarId, 10m)]),
        ], []));
        var custardId = await OwnedSubAssemblyIdAsync(productId, fillingRoleId);
        var ubeId = await OwnedSubAssemblyIdAsync(productId, toppingRoleId);

        var lines = await CreateLineRepository().GetForSubAssembliesAsync([custardId, ubeId], Cancellation);

        lines.Select(l => l.SubAssemblyId).Should().BeEquivalentTo(new[] { custardId, custardId, ubeId });
        lines.Should().OnlyContain(l => l.Ingredient != null, "a product's recipe shows each line's ingredient");
        (await CreateLineRepository().GetForSubAssembliesAsync([], Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinkableAsync_returns_sub_assemblies_with_lines_and_their_owner_and_role()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m)]);

        var linkable = await CreateSubAssemblyRepository().GetLinkableAsync(Cancellation);

        // A product's own has no name, so they come by their owners' names.
        linkable.Select(s => (s.Name, s.OwnerProduct!.Name, s.Role!.Name)).Should().Equal(
            ((string?)null, "Ensaymada", "Dough"),
            ((string?)null, "Pandesal", "Dough"));
    }

    [Fact]
    public async Task GetByIdsAsync_returns_the_sub_assemblies_with_their_role_and_owner()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);

        var subAssembly = (await CreateSubAssemblyRepository().GetByIdsAsync([doughId], Cancellation)).Should().ContainSingle().Subject;

        subAssembly.Role!.Name.Should().Be("Dough");
        subAssembly.OwnerProduct!.Name.Should().Be("Ensaymada");
        (await CreateSubAssemblyRepository().GetByIdsAsync([], Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_product_takes_its_own_sub_assemblies_and_their_lines_with_it()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await CreateProductRepository().DeleteAsync(productId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(s => s.Id == custardId, Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblyLines.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_a_product_whose_sub_assembly_another_product_links_is_refused_until_it_is_unlinked()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)]);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var act = () => CreateProductRepository().DeleteAsync(bigId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada Small links Ensaymada Big's Dough*");
        (await CreateProductRepository().GetByIdAsync(bigId, Cancellation)).Should().NotBeNull();

        // Once the smaller one has its own copy instead, the bigger one can go.
        await SaveRecipeAsync(smallId, new RecipeDraft([Owned(0, roleId, [(flourId, 1000m)], weightPerPiece: 30m)], []));
        await CreateProductRepository().DeleteAsync(bigId, Cancellation);
        (await CreateProductRepository().GetByIdAsync(bigId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_an_ingredient_that_is_in_use_by_a_sub_assembly_is_blocked()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.RawMaterials.Where(i => i.Id == sugarId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_a_role_that_is_in_use_by_a_sub_assembly_is_blocked()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 100m)]);

        var act = () => CreateRoleRepository().DeleteAsync(roleId, Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_an_unused_role_succeeds()
    {
        var roleId = await AddRoleAsync("Dough");

        await CreateRoleRepository().DeleteAsync(roleId, Cancellation);

        (await CreateRoleRepository().GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_UpdateAsync_rejects_a_nonexistent_role()
    {
        var act = () => CreateRoleRepository().UpdateAsync(new SubAssemblyRole { Id = 999_999, Name = "Ghost", SortOrder = 1 }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_UpdateAsync_renames_a_role_and_changes_its_sort_order()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 1);
        var roleRepository = CreateRoleRepository();

        await roleRepository.UpdateAsync(new SubAssemblyRole { Id = roleId, Name = "Sourdough", SortOrder = 9 }, Cancellation);

        var updated = (await roleRepository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Sourdough");
        updated.SortOrder.Should().Be(9);
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_AddAsync_persists_a_new_role()
    {
        var roleRepository = CreateRoleRepository();

        await roleRepository.AddAsync(new SubAssemblyRole { Name = "Glaze", SortOrder = 5 }, Cancellation);

        var all = await roleRepository.GetAllAsync(Cancellation);
        all.Should().ContainSingle(r => r.Name == "Glaze" && r.SortOrder == 5);
    }
}
