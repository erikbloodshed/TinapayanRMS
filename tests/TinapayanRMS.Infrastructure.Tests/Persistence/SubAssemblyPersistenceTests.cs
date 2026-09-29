using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A product's new recipe sections are saved with it (ProductRepository.SaveAsync's recipe) as sub-assemblies on the
/// Sub-Assemblies screen, named after the product and their role, and linked: their lines in grams and totals, their
/// names, links from other products, and what's kept when a section or a product goes.
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

    private async Task<List<string>> SubAssemblyNamesAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.OrderBy(s => s.Id).Select(s => s.Name).ToListAsync(Cancellation);
    }

    private static NewSubAssemblyDraft New(int roleId, IReadOnlyList<RecipeLine> lines, decimal weightPerPiece = 10m, string? sectionName = null) =>
        new(roleId, lines, UsageAuthoredField.WeightPerPiece, weightPerPiece, sectionName);

    [Fact]
    public async Task Saving_a_new_section_keeps_it_as_a_sub_assembly_named_after_the_product_and_its_role_and_links_it()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft([New(roleId, [(flourId, 800m), (sugarId, 200m)], 50m) with { ShrinkagePercent = 10m }], []));

        var subAssembly = await GetSubAssemblyAsync(await SubAssemblyIdAsync(productId, roleId));
        subAssembly.Name.Should().Be("Pandesal Dough");
        subAssembly.TotalWeight.Should().Be(1000m);
        subAssembly.TotalCost.Should().Be(58m); // 800*0.05 + 200*0.09
        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(subAssembly.Id);
        usage.AuthoredField.Should().Be(UsageAuthoredField.WeightPerPiece);
        usage.AuthoredValue.Should().Be(50m, "the piece size is the product's, on its usage");
        usage.ShrinkagePercent.Should().Be(10m);
        usage.SubAssembly!.Role!.Name.Should().Be("Dough");
        (await CreateSubAssemblyRepository().GetStandaloneAsync(Cancellation)).Should().ContainSingle(s => s.Id == subAssembly.Id, "it's on the Sub-Assemblies screen");
        (await CreateSubAssemblyRepository().GetStandaloneLinkedByAsync(Cancellation))[subAssembly.Id]
            .Select(p => p.Name).Should().Equal("Pandesal");
    }

    [Fact]
    public async Task A_usage_can_store_a_typed_yield_instead_of_a_weight_per_piece()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(flourId, 1200m)], UsageAuthoredField.Yield, 24m)], []));

        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield);
        usage.AuthoredValue.Should().Be(24m);
    }

    [Fact]
    public async Task A_sections_own_name_goes_after_the_product_and_role_unless_it_is_just_the_roles()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var productId = await AddProductAsync("Cheese Roll");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [New(doughRoleId, [(flourId, 1000m)], sectionName: "dough"), New(fillingRoleId, [(sugarId, 100m)], sectionName: " Cheese ")], []));

        (await SubAssemblyNamesAsync()).Should().Equal("Cheese Roll Dough", "Cheese Roll Filling: Cheese");
    }

    [Fact]
    public async Task A_name_already_taken_gets_the_next_number_among_every_sub_assembly_and_this_saves_own()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var milkId = await AddIngredientAsync("Milk");
        var productId = await AddProductAsync("Ensaymada");
        await SaveRecipeAsync(productId, new RecipeDraft([New(fillingRoleId, [(sugarId, 100m)])], []));
        var firstId = await SubAssemblyIdAsync(productId, fillingRoleId);

        // Make a Copy of it, then another section going by the role's own name: both would be "Ensaymada Filling".
        await SaveRecipeAsync(productId, new RecipeDraft(
            [New(fillingRoleId, [(sugarId, 100m), (milkId, 50m)]), New(fillingRoleId, [(milkId, 80m)], sectionName: "Filling")], []));

        (await SubAssemblyNamesAsync()).Should().Equal("Ensaymada Filling", "Ensaymada Filling 2", "Ensaymada Filling 3");
        (await GetSubAssemblyAsync(firstId)).Name.Should().Be("Ensaymada Filling", "the one the recipe no longer uses is kept as it was");
    }

    [Fact]
    public async Task A_section_left_out_of_the_recipe_leaves_its_sub_assembly_and_lines_on_the_Sub_Assemblies_screen()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await SaveRecipeAsync(productId, new RecipeDraft([], []));

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(s => s.Id == custardId, Cancellation)).Should().BeTrue();
        (await dbContext.SubAssemblyLines.AnyAsync(l => l.SubAssemblyId == custardId, Cancellation)).Should().BeTrue();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Another_product_can_link_a_sub_assembly_at_its_own_piece_size()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var smallId = await AddProductAsync("Ensaymada Small");

        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var usage = (await CreateUsageRepository().GetForProductAsync(smallId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(doughId);
        usage.AuthoredValue.Should().Be(30m);
        usage.SubAssembly!.Name.Should().Be("Ensaymada Big Dough");
        (await CreateSubAssemblyRepository().GetStandaloneLinkedByAsync(Cancellation))[doughId]
            .Select(p => p.Name).Should().Equal("Ensaymada Big", "Ensaymada Small");
    }

    [Fact]
    public async Task Linking_a_nonexistent_sub_assembly_is_rejected()
    {
        var productId = await AddProductAsync("Pandesal");

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(999_999, UsageAuthoredField.WeightPerPiece, 10m)]));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_recipe_can_have_several_sub_assemblies_in_one_role_new_and_linked_told_apart_by_name()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (_, sweetDoughId) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)]);
        var productId = await AddProductAsync("Pandesal");

        var twoUnnamed = () => SaveRecipeAsync(productId, new RecipeDraft(
            [New(fillingRoleId, [(sugarId, 100m)]), New(fillingRoleId, [(sugarId, 200m)])], []));
        var oneNameTwice = () => SaveRecipeAsync(productId, new RecipeDraft(
            [New(fillingRoleId, [(sugarId, 100m)], sectionName: "Ube"), New(fillingRoleId, [(sugarId, 200m)], sectionName: "ube")], []));

        await twoUnnamed.Should().ThrowAsync<InvalidOperationException>().WithMessage("This recipe has more than one Filling without a name*");
        await oneNameTwice.Should().ThrowAsync<InvalidOperationException>().WithMessage("This recipe has two Filling sub-assemblies called Ube*");

        // A new Dough beside Ensaymada's, which goes by its name; and an unnamed Filling beside one named Cheese.
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                New(doughRoleId, [(flourId, 100m)]),
                New(fillingRoleId, [(sugarId, 100m)]),
                New(fillingRoleId, [(sugarId, 200m)], sectionName: " Cheese "),
            ],
            [new LinkedUsageDraft(sweetDoughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        (await SubAssemblyNamesAsync()).Should().Equal("Ensaymada Dough", "Pandesal Dough", "Pandesal Filling", "Pandesal Filling: Cheese");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.CountAsync(u => u.ProductId == productId, Cancellation)).Should().Be(4);
    }

    [Fact]
    public async Task A_linked_section_saved_from_this_product_goes_by_its_own_name_there()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft([New(fillingRoleId, [(sugarId, 100m)], sectionName: "Ube")], []));
        var ubeId = await SubAssemblyIdAsync(productId, fillingRoleId);

        // "Pandesal Filling: Ube" reads "Filling: Ube" in Pandesal, so a new Filling can't be called Ube too.
        var act = () => SaveRecipeAsync(productId, new RecipeDraft(
            [New(fillingRoleId, [(sugarId, 200m)], sectionName: "Ube")], [new LinkedUsageDraft(ubeId, UsageAuthoredField.WeightPerPiece, 10m)]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("This recipe has two Filling sub-assemblies called Ube*");
    }

    [Fact]
    public async Task Moving_a_sub_assembly_to_a_role_where_a_product_linking_it_has_one_by_its_name_is_refused()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", doughRoleId, [(flourId, 1000m)]);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft(
            [New(fillingRoleId, [(sugarId, 100m)], sectionName: "Ube")], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        // Ensaymada Small's own Filling reads "Filling: Ube" there.
        var act = () => CreateSubAssemblyRepository().SaveStandaloneAsync(new SubAssembly { Id = doughId, Name = "Ube", RoleId = fillingRoleId }, [(flourId, 1000m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ensaymada Small, which links this sub-assembly, has two Filling sub-assemblies called Ube*");
        (await GetSubAssemblyAsync(doughId)).RoleId.Should().Be(doughRoleId);

        await CreateSubAssemblyRepository().SaveStandaloneAsync(new SubAssembly { Id = doughId, Name = "Ube Dough", RoleId = fillingRoleId }, [(flourId, 1000m)], Cancellation);
        (await GetSubAssemblyAsync(doughId)).RoleId.Should().Be(fillingRoleId);
    }

    [Fact]
    public async Task A_packaging_material_on_a_line_is_rejected_and_nothing_is_saved()
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

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([New(roleId, [(sugarId, 100m), (boxId, 1m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'Cake Box' is packaging*");
        (await SubAssemblyNamesAsync()).Should().Equal(["Ensaymada Filling"], "a rejected save rolls back the sub-assembly it was making");
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == custardId);
    }

    [Fact]
    public async Task A_nonexistent_ingredient_is_rejected_and_nothing_is_saved()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        var act = () => SaveRecipeAsync(productId, new RecipeDraft([New(roleId, [(sugarId, 50m), (999_999, 50m)])], []));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await SubAssemblyNamesAsync()).Should().Equal("Ensaymada Filling");
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == custardId);
    }

    [Fact]
    public async Task A_later_part_of_the_save_failing_rolls_back_the_sub_assembly_a_new_section_made()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var productId = await AddProductAsync("Pandesal");
        var repository = CreateProductRepository();
        var product = (await repository.GetByIdAsync(productId, Cancellation))!;

        var act = () => repository.SaveAsync(
            product,
            new RecipeDraft([New(roleId, [(flourId, 1000m)])], []),
            packagingLink: null,
            packagingLines: null,
            new CostingParameters { LaborPercent = -1m },
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't be negative*");
        (await SubAssemblyNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Renaming_the_product_leaves_the_names_of_its_sub_assemblies_alone()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)]);
        var repository = CreateProductRepository();
        var product = (await repository.GetByIdAsync(productId, Cancellation))!;
        product.Name = "Pan de Sal";

        await repository.UpdateAsync(product, Cancellation);

        (await GetSubAssemblyAsync(doughId)).Name.Should().Be("Pandesal Dough");
    }

    [Fact]
    public async Task A_renamed_product_names_the_sections_it_saves_with_the_rename_by_its_new_name()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var productId = await AddProductAsync("Pandesal");
        var repository = CreateProductRepository();
        var product = (await repository.GetByIdAsync(productId, Cancellation))!;
        product.Name = "Pan de Sal";

        await repository.SaveAsync(product, new RecipeDraft([New(roleId, [(flourId, 1000m)])], []), null, null, null, Cancellation);

        (await SubAssemblyNamesAsync()).Should().Equal("Pan de Sal Dough");
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
            New(fillingRoleId, [(sugarId, 60m), (milkId, 50m)]),
            New(toppingRoleId, [(milkId, 200m)]),
            New(finishingRoleId, [(sugarId, 10m)]),
        ], []));
        var custardId = await SubAssemblyIdAsync(productId, fillingRoleId);
        var ubeId = await SubAssemblyIdAsync(productId, toppingRoleId);

        var lines = await CreateLineRepository().GetForSubAssembliesAsync([custardId, ubeId], Cancellation);

        lines.Select(l => l.SubAssemblyId).Should().BeEquivalentTo(new[] { custardId, custardId, ubeId });
        lines.Should().OnlyContain(l => l.Ingredient != null, "a product's recipe shows each line's ingredient");
        (await CreateLineRepository().GetForSubAssembliesAsync([], Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinkableAsync_returns_sub_assemblies_with_lines_by_name_with_their_role()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m)]);
        await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);

        var linkable = await CreateSubAssemblyRepository().GetLinkableAsync(Cancellation);

        linkable.Select(s => (s.Name, s.Role!.Name)).Should().Equal(("Ensaymada Dough", "Dough"), ("Pandesal Dough", "Dough"));
    }

    [Fact]
    public async Task GetByIdsAsync_returns_the_sub_assemblies_with_their_role()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);

        var subAssembly = (await CreateSubAssemblyRepository().GetByIdsAsync([doughId], Cancellation)).Should().ContainSingle().Subject;

        subAssembly.Name.Should().Be("Ensaymada Dough");
        subAssembly.Role!.Name.Should().Be("Dough");
        (await CreateSubAssemblyRepository().GetByIdsAsync([], Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_product_keeps_the_sub_assemblies_it_used_and_their_lines()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await CreateProductRepository().DeleteAsync(productId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(s => s.Id == custardId, Cancellation)).Should().BeTrue();
        (await dbContext.SubAssemblyLines.AnyAsync(l => l.SubAssemblyId == custardId, Cancellation)).Should().BeTrue();
        (await dbContext.ProductSubAssemblyUsages.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_a_product_leaves_another_product_linking_its_sub_assembly_as_it_was()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)]);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        await CreateProductRepository().DeleteAsync(bigId, Cancellation);

        (await CreateProductRepository().GetByIdAsync(bigId, Cancellation)).Should().BeNull();
        (await CreateUsageRepository().GetForProductAsync(smallId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == doughId);
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
    public async Task SubAssemblyRoleRepository_UpdateAsync_renames_a_role_and_keeps_its_place()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 1);
        var roleRepository = CreateRoleRepository();

        await roleRepository.UpdateAsync(new SubAssemblyRole { Id = roleId, Name = "Sourdough", SortOrder = 9 }, Cancellation);

        var updated = (await roleRepository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Sourdough");
        updated.SortOrder.Should().Be(1, "only Move Up and Move Down change a role's place");
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_AddAsync_puts_a_new_role_last()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 4);
        var roleRepository = CreateRoleRepository();

        await roleRepository.AddAsync(new SubAssemblyRole { Name = "Glaze", SortOrder = 2 }, Cancellation);

        var all = await roleRepository.GetAllAsync(Cancellation);
        all.Select(r => (r.Name, r.SortOrder)).Should().Equal(("Dough", 1), ("Filling", 4), ("Glaze", 5));
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_AddAsync_numbers_the_first_role_1()
    {
        await CreateRoleRepository().AddAsync(new SubAssemblyRole { Name = "Glaze" }, Cancellation);

        (await CreateRoleRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_MoveAsync_swaps_a_role_with_its_neighbour_and_numbers_them_all_again()
    {
        await AddRoleAsync("Dough", sortOrder: 2);
        var fillingId = await AddRoleAsync("Filling", sortOrder: 5);
        await AddRoleAsync("Topping", sortOrder: 9);
        var roleRepository = CreateRoleRepository();

        await roleRepository.MoveAsync(fillingId, up: true, Cancellation);
        (await roleRepository.GetAllAsync(Cancellation)).Select(r => (r.Name, r.SortOrder))
            .Should().Equal(("Filling", 1), ("Dough", 2), ("Topping", 3));

        await roleRepository.MoveAsync(fillingId, up: false, Cancellation);
        await roleRepository.MoveAsync(fillingId, up: false, Cancellation);
        (await roleRepository.GetAllAsync(Cancellation)).Select(r => (r.Name, r.SortOrder))
            .Should().Equal(("Dough", 1), ("Topping", 2), ("Filling", 3));
    }

    [Fact]
    public async Task SubAssemblyRoleRepository_MoveAsync_leaves_the_first_and_last_where_they_are()
    {
        var doughId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingId = await AddRoleAsync("Filling", sortOrder: 2);
        var roleRepository = CreateRoleRepository();

        await roleRepository.MoveAsync(doughId, up: true, Cancellation);
        await roleRepository.MoveAsync(fillingId, up: false, Cancellation);

        (await roleRepository.GetAllAsync(Cancellation)).Select(r => r.Name).Should().Equal("Dough", "Filling");
    }

    [Fact]
    public async Task Deleting_a_role_numbers_the_rest_again_without_a_gap()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingId = await AddRoleAsync("Filling", sortOrder: 2);
        await AddRoleAsync("Topping", sortOrder: 3);
        var roleRepository = CreateRoleRepository();

        await roleRepository.DeleteAsync(fillingId, Cancellation);

        (await roleRepository.GetAllAsync(Cancellation)).Select(r => (r.Name, r.SortOrder))
            .Should().Equal(("Dough", 1), ("Topping", 2));
    }
}
