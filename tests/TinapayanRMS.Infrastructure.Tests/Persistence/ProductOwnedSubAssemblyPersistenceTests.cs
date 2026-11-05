using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Sub-assemblies in a product-owned role (SubAssemblyRole.IsProductOwned): a section made in a product is saved as
/// its own, edited in its recipe and gone with it; other products copy it but can't link it; a role made product-owned
/// hands each of its sub-assemblies one product uses to that product, and one made not product-owned hands them back
/// to the Sub-Assemblies screen; clones and variants get copies of their own.
/// </summary>
[Collection(DatabaseCollection.B)]
public class ProductOwnedSubAssemblyPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    private SubAssemblyRoleRepository CreateRoleRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private static NewSubAssemblyDraft Own(
        int roleId, IReadOnlyList<RecipeLine> lines, int id = 0, string? sectionName = null, decimal weightPerPiece = 10m, SectionPlacement? placement = null) =>
        new(roleId, lines, UsageAuthoredField.WeightPerPiece, weightPerPiece, sectionName) { Id = id, Placement = placement ?? new SectionPlacement.ByRole() };

    private async Task<SubAssembly?> FindAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, Cancellation);
    }

    private async Task<List<(string Name, int? OwnerProductId)>> SubAssembliesAsync()
    {
        await using var dbContext = CreateDbContext();
        return [.. (await dbContext.SubAssemblies.OrderBy(s => s.Id).Select(s => new { s.Name, s.OwnerProductId }).ToListAsync(Cancellation))
            .Select(s => (s.Name, s.OwnerProductId))];
    }

    private async Task<SubAssemblyRole> RoleAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyRoles.AsNoTracking().SingleAsync(r => r.Id == id, Cancellation);
    }

    private async Task SetProductOwnedAsync(int roleId, bool isProductOwned)
    {
        var role = await RoleAsync(roleId);
        role.IsProductOwned = isProductOwned;
        await CreateRoleRepository().UpdateAsync(role, Cancellation);
    }

    [Fact]
    public async Task A_new_section_in_a_product_owned_role_is_the_products_own_and_not_on_the_Sub_Assemblies_screen()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");

        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(doughRoleId, [(flourId, 1000m)]), Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Dough", null), ("Cheese Roll Filling: Cheese", productId));
        (await CreateSubAssemblyRepository().GetStandaloneAsync(Cancellation)).Select(s => s.Name).Should().Equal("Cheese Roll Dough");
        var filling = (await CreateSubAssemblyRepository().GetLinkableAsync(Cancellation)).Single(s => s.RoleId == fillingRoleId);
        filling.OwnerProduct!.Name.Should().Be("Cheese Roll", "it's offered to copy, with whose it is");
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task An_own_section_is_rewritten_in_place_and_taking_it_out_deletes_it()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)])], []));
        var fillingId = await SubAssemblyIdAsync(productId, fillingRoleId);

        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m), (milkId, 50m)], id: fillingId, weightPerPiece: 15m)], []));

        var filling = (await FindAsync(fillingId))!;
        filling.TotalWeight.Should().Be(150m);
        filling.TotalCost.Should().Be(45m);
        filling.OwnerProductId.Should().Be(productId);
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Single().AuthoredValue.Should().Be(15m);

        await SaveRecipeAsync(productId, new RecipeDraft([], []));

        (await FindAsync(fillingId)).Should().BeNull("nothing but its product uses its own");
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Own_sections_names_follow_their_names_and_two_can_swap_in_one_save()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var ubeId = await AddIngredientAsync("Ube");
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Twin Bun");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(fillingRoleId, [(ubeId, 100m)], sectionName: "Ube"), Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));
        Dictionary<string, int> byName;
        await using (var before = CreateDbContext())
        {
            byName = await before.SubAssemblies.ToDictionaryAsync(s => s.Name, s => s.Id, Cancellation);
        }

        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                Own(fillingRoleId, [(ubeId, 100m)], id: byName["Twin Bun Filling: Ube"], sectionName: "Cheese"),
                Own(fillingRoleId, [(cheeseId, 100m)], id: byName["Twin Bun Filling: Cheese"], sectionName: "Ube"),
            ],
            []));

        (await FindAsync(byName["Twin Bun Filling: Ube"]))!.Name.Should().Be("Twin Bun Filling: Cheese");
        (await FindAsync(byName["Twin Bun Filling: Cheese"]))!.Name.Should().Be("Twin Bun Filling: Ube");
        (await SubAssembliesAsync()).Should().HaveCount(2, "the same two, renamed");
    }

    [Fact]
    public async Task An_own_section_moved_to_a_role_that_isnt_product_owned_goes_to_the_Sub_Assemblies_screen_linked()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)])], []));
        var id = await SubAssemblyIdAsync(productId, fillingRoleId);

        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(cheeseId, 100m)], id: id)], []));

        var moved = (await FindAsync(id))!;
        moved.OwnerProductId.Should().BeNull();
        moved.Name.Should().Be("Cheese Roll Dough");
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Single().SubAssemblyId.Should().Be(id);
    }

    [Fact]
    public async Task An_unlinked_section_is_the_products_own_in_a_role_that_isnt_product_owned_and_stays_so_wherever_it_moves()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var houseDoughId = await CreateSubAssemblyRepository().SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(houseDoughId, UsageAuthoredField.WeightPerPiece, 50m)]));

        // Unlink: the linked dough's lines, as a new section of the product's own.
        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m, placement: new SectionPlacement.ProductsOwn())], []));

        (await SubAssembliesAsync()).Should().Equal(("House Dough", null), ("Cheese Roll Dough", productId));
        (await CreateSubAssemblyRepository().GetStandaloneAsync(Cancellation)).Select(s => s.Name).Should().Equal(["House Dough"], "the copy isn't on the Sub-Assemblies screen");
        (await CreateSubAssemblyRepository().GetStandaloneLinkedByAsync(Cancellation)).Should().NotContainKey(houseDoughId, "the product doesn't link it any more");
        var ownId = await SubAssemblyIdAsync(productId, doughRoleId);

        var otherId = await AddProductAsync("Cheese Bun");
        var link = () => SaveRecipeAsync(otherId, new RecipeDraft([], [new LinkedUsageDraft(ownId, UsageAuthoredField.WeightPerPiece, 10m)]));
        await link.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is Cheese Roll's own, so it can't be linked*");

        // Saved again with its id, as the Products form does, and in another role that isn't product-owned: still its own.
        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 900m)], id: ownId, placement: new SectionPlacement.ProductsOwn())], []));
        (await FindAsync(ownId))!.OwnerProductId.Should().Be(productId);

        await SaveRecipeAsync(productId, new RecipeDraft([Own(toppingRoleId, [(flourId, 900m)], id: ownId, placement: new SectionPlacement.ProductsOwn())], []));
        var moved = (await FindAsync(ownId))!;
        moved.OwnerProductId.Should().Be(productId, "it was never the role's, so moving it doesn't release it");
        moved.Name.Should().Be("Cheese Roll Topping");
    }

    [Fact]
    public async Task An_own_section_moved_to_the_Sub_Assemblies_screen_keeps_its_name_and_the_product_links_it()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese", weightPerPiece: 12m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, fillingRoleId);

        // Moved with a line typed in the same save: the lines are written as they show, then it's released.
        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(fillingRoleId, [(cheeseId, 100m), (milkId, 50m)], id: fillingId, sectionName: "Cheese", weightPerPiece: 12m, placement: new SectionPlacement.MoveToSubAssemblies())], []));

        var moved = (await FindAsync(fillingId))!;
        moved.OwnerProductId.Should().BeNull("it's on the Sub-Assemblies screen now, even in a product-owned role");
        moved.Name.Should().Be("Cheese Roll Filling: Cheese");
        moved.TotalWeight.Should().Be(150m);
        (await CreateSubAssemblyRepository().GetStandaloneAsync(Cancellation)).Select(s => s.Id).Should().Equal(fillingId);
        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Single();
        usage.SubAssemblyId.Should().Be(fillingId);
        usage.AuthoredValue.Should().Be(12m);

        // Other products can link it now.
        var otherId = await AddProductAsync("Cheese Bun");
        await SaveRecipeAsync(otherId, new RecipeDraft([], [new LinkedUsageDraft(fillingId, UsageAuthoredField.WeightPerPiece, 10m)]));
        (await CreateSubAssemblyRepository().GetStandaloneLinkedByAsync(Cancellation))[fillingId].Select(p => p.Name).Should()
            .BeEquivalentTo(["Cheese Roll", "Cheese Bun"]);

        // And the product that moved it saves it as a link from now on, keeping it there.
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(fillingId, UsageAuthoredField.WeightPerPiece, 12m)]));
        (await FindAsync(fillingId))!.OwnerProductId.Should().BeNull();
    }

    [Fact]
    public async Task An_unlinked_section_moved_back_to_the_Sub_Assemblies_screen_is_linked_there()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)], placement: new SectionPlacement.ProductsOwn())], []));
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);

        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)], id: doughId, placement: new SectionPlacement.MoveToSubAssemblies())], []));

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Dough", null));
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Single().SubAssemblyId.Should().Be(doughId);
    }

    [Fact]
    public async Task A_new_section_moved_to_the_Sub_Assemblies_screen_goes_there_in_a_product_owned_role()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");

        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], placement: new SectionPlacement.MoveToSubAssemblies())], []));

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Filling", null));
    }

    [Fact]
    public async Task A_clone_gets_its_own_copy_of_a_section_the_source_unlinked()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var sourceId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(sourceId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)], placement: new SectionPlacement.ProductsOwn())], []));
        var sourceDoughId = await SubAssemblyIdAsync(sourceId, doughRoleId);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Cheese Roll Mini", Cancellation);

        var dough = (await CreateUsageRepository().GetForProductAsync(cloneId, Cancellation)).Single().SubAssembly!;
        dough.Id.Should().NotBe(sourceDoughId, "another product's own is copied, never linked");
        dough.Name.Should().Be("Cheese Roll Mini Dough");
        dough.OwnerProductId.Should().Be(cloneId);
    }

    [Fact]
    public async Task Renaming_a_role_that_isnt_product_owned_renames_the_own_ones_named_after_it()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)], placement: new SectionPlacement.ProductsOwn())], []));
        var role = await RoleAsync(doughRoleId);

        role.Name = "Bread Dough";
        await CreateRoleRepository().UpdateAsync(role, Cancellation);

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Bread Dough", productId));
    }

    [Fact]
    public async Task Another_products_own_can_be_copied_but_not_linked()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var ownerId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(ownerId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));
        var fillingId = await SubAssemblyIdAsync(ownerId, fillingRoleId);
        var otherId = await AddProductAsync("Cheese Bun");

        var link = () => SaveRecipeAsync(otherId, new RecipeDraft([], [new LinkedUsageDraft(fillingId, UsageAuthoredField.WeightPerPiece, 10m)]));
        var claim = () => SaveRecipeAsync(otherId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], id: fillingId)], []));

        await link.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cheese Roll Filling: Cheese is Cheese Roll's own, so it can't be linked*");
        await claim.Should().ThrowAsync<InvalidOperationException>().WithMessage("*isn't one of this product's own*");

        // A copy is Cheese Bun's own.
        await SaveRecipeAsync(otherId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)])], []));
        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Filling: Cheese", ownerId), ("Cheese Bun Filling", otherId));
    }

    [Fact]
    public async Task The_Sub_Assemblies_screens_save_and_delete_refuse_a_products_own()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)])], []));
        var id = await SubAssemblyIdAsync(productId, fillingRoleId);

        var save = () => CreateSubAssemblyRepository().SaveStandaloneAsync(new SubAssembly { Id = id, Name = "Cheese", RoleId = fillingRoleId }, [(cheeseId, 50m)], Cancellation);
        var delete = () => CreateSubAssemblyRepository().DeleteStandaloneAsync(id, Cancellation);

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is Cheese Roll's own*");
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is Cheese Roll's own*");
    }

    [Fact]
    public async Task Deleting_a_product_permanently_takes_its_own_and_leaves_what_it_links()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour");
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(doughRoleId, [(flourId, 1000m)]), Own(fillingRoleId, [(cheeseId, 100m)])], []));

        await CreateProductRepository().DeletePermanentlyAsync(productId, Cancellation);

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Dough", null));
    }

    [Fact]
    public async Task Renaming_a_product_renames_its_own_but_not_what_it_links()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour");
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(doughRoleId, [(flourId, 1000m)]), Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));
        var repository = CreateProductRepository();
        var product = (await repository.GetByIdAsync(productId, Cancellation))!;

        product.Name = "Cheese Bun";
        await repository.SaveAsync(product, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await SubAssembliesAsync()).Should().Equal(("Cheese Roll Dough", null), ("Cheese Bun Filling: Cheese", productId));

        product.Name = "Cheese Loaf";
        await repository.UpdateAsync(product, Cancellation);

        (await SubAssembliesAsync()).Select(s => s.Name).Should().Equal("Cheese Roll Dough", "Cheese Loaf Filling: Cheese");

        // The Products list's Rename, which writes the name alone.
        await repository.RenameAsync(productId, "Cheese Twist", Cancellation);

        (await SubAssembliesAsync()).Select(s => s.Name).Should().Equal("Cheese Roll Dough", "Cheese Twist Filling: Cheese");
        (await repository.GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Cheese Twist");
    }

    [Fact]
    public async Task A_products_own_keeps_its_section_name_and_a_numbered_name_follows_a_rename()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 2);
        var cheeseId = await AddIngredientAsync("Cheese");

        // "Cheese Roll Filling" is taken on the Sub-Assemblies screen, so the product's own plain one is numbered.
        await CreateSubAssemblyRepository().SaveStandaloneAsync(
            new SubAssembly { Name = "Cheese Roll Filling", RoleId = doughRoleId }, [(cheeseId, 50m)], Cancellation);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(fillingRoleId, [(cheeseId, 100m)]), Own(fillingRoleId, [(cheeseId, 80m)], sectionName: "Ube")], []));

        (await OwnAsync(productId)).Select(s => (s.Name, s.SectionName)).Should().Equal(
            ("Cheese Roll Filling 2", null), ("Cheese Roll Filling: Ube", "Ube"));

        await CreateProductRepository().RenameAsync(productId, "Cheese Bun", Cancellation);

        var own = await OwnAsync(productId);
        own.Select(s => (s.Name, s.SectionName)).Should().Equal(("Cheese Bun Filling", (string?)null), ("Cheese Bun Filling: Ube", "Ube"));

        // A label changed in the recipe renames the section after it.
        await SaveRecipeAsync(productId, new RecipeDraft(
            [Own(fillingRoleId, [(cheeseId, 100m)], id: own[0].Id), Own(fillingRoleId, [(cheeseId, 80m)], id: own[1].Id, sectionName: "Purple Yam")], []));

        (await OwnAsync(productId)).Select(s => (s.Name, s.SectionName)).Should().Equal(
            ("Cheese Bun Filling", (string?)null), ("Cheese Bun Filling: Purple Yam", "Purple Yam"));
    }

    [Fact]
    public async Task A_role_made_product_owned_and_back_takes_and_gives_the_section_name_its_name_has()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));
        var id = await SubAssemblyIdAsync(productId, fillingRoleId);

        await SetProductOwnedAsync(fillingRoleId, false);

        var released = (await FindAsync(id))!;
        (released.OwnerProductId, released.SectionName, released.Name).Should().Be(((int?)null, (string?)null, "Cheese Roll Filling: Cheese"));

        await SetProductOwnedAsync(fillingRoleId, true);

        var adopted = (await FindAsync(id))!;
        (adopted.OwnerProductId, adopted.SectionName).Should().Be(((int?)productId, (string?)"Cheese"));
    }

    private async Task<List<SubAssembly>> OwnAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().Where(s => s.OwnerProductId == productId).OrderBy(s => s.Id).ToListAsync(Cancellation);
    }

    [Fact]
    public async Task Making_a_role_product_owned_gives_each_product_the_ones_only_it_uses_with_its_add_ins_merged()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var cornId = await AddIngredientAsync("Corn", costPerGram: 0.1m);
        var (rollId, rollFillingId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(cheeseId, 100m)]);
        var (_, sharedId) = await AddProductWithSubAssemblyAsync("Milky Bread", fillingRoleId, [(cheeseId, 200m)]);
        var otherId = await AddProductAsync("Milky Bread x 12");
        await SaveRecipeAsync(otherId, new RecipeDraft([], [new LinkedUsageDraft(sharedId, UsageAuthoredField.WeightPerPiece, 10m)]));
        var unusedId = await CreateSubAssemblyRepository().SaveStandaloneAsync(new SubAssembly { Name = "Pan de Coco", RoleId = fillingRoleId }, [(cornId, 50m)], Cancellation);
        await SaveRecipeAsync(rollId, new RecipeDraft(
            [], [new LinkedUsageDraft(rollFillingId, UsageAuthoredField.WeightPerPiece, 10m) { AddIns = [(cornId, 20m)] }]));

        (await CreateRoleRepository().CountOwnershipChangesAsync(fillingRoleId, makeOwned: true, Cancellation)).Should().Be(1);
        await SetProductOwnedAsync(fillingRoleId, true);

        var adopted = (await FindAsync(rollFillingId))!;
        adopted.OwnerProductId.Should().Be(rollId);
        adopted.Name.Should().Be("Cheese Roll Filling", "its name is kept");
        adopted.TotalWeight.Should().Be(120m, "the corn added on top of it is in its lines now");
        adopted.TotalCost.Should().Be(42m);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == rollFillingId).OrderBy(l => l.SortOrder).Select(l => l.IngredientId).ToListAsync(Cancellation))
                .Should().Equal(cheeseId, cornId);
            (await dbContext.UsageAddInLines.AnyAsync(Cancellation)).Should().BeFalse();
        }

        (await FindAsync(sharedId))!.OwnerProductId.Should().BeNull("two products use it, so it stays on the Sub-Assemblies screen");
        (await FindAsync(unusedId))!.OwnerProductId.Should().BeNull("no product uses it");
        (await RoleAsync(fillingRoleId)).IsProductOwned.Should().BeTrue();
    }

    [Fact]
    public async Task Making_a_role_no_longer_product_owned_sends_its_own_ones_to_the_Sub_Assemblies_screen_still_used()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)])], []));
        var id = await SubAssemblyIdAsync(productId, fillingRoleId);

        (await CreateRoleRepository().CountOwnershipChangesAsync(fillingRoleId, makeOwned: false, Cancellation)).Should().Be(1);
        await SetProductOwnedAsync(fillingRoleId, false);

        (await FindAsync(id))!.OwnerProductId.Should().BeNull();
        (await CreateSubAssemblyRepository().GetStandaloneAsync(Cancellation)).Select(s => s.Id).Should().Equal(id);
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Single().SubAssemblyId.Should().Be(id);
    }

    [Fact]
    public async Task Renaming_a_product_owned_role_renames_the_ones_named_after_it()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese")], []));
        var role = await RoleAsync(fillingRoleId);

        role.Name = "Stuffing";
        await CreateRoleRepository().UpdateAsync(role, Cancellation);

        (await SubAssembliesAsync()).Select(s => s.Name).Should().Equal("Cheese Roll Stuffing: Cheese");
    }

    [Fact]
    public async Task The_Packaging_role_cant_be_product_owned()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var role = await RoleAsync(packagingRoleId);
        role.IsProductOwned = true;

        var update = () => CreateRoleRepository().UpdateAsync(role, Cancellation);
        var add = () => CreateRoleRepository().AddAsync(new SubAssemblyRole { Name = "Boxes", IsPackaging = true, IsProductOwned = true }, Cancellation);

        await update.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't be product-owned*");
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't be product-owned*");
        (await RoleAsync(packagingRoleId)).IsProductOwned.Should().BeFalse();
    }

    // A variant links the source's own instead (ProductVariantPersistenceTests).
    [Fact]
    public async Task A_clone_gets_its_own_copy_of_the_sources_own_and_links_what_it_links()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var sourceId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [
                Own(doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m),
                Own(fillingRoleId, [(cheeseId, 100m)], sectionName: "Cheese", weightPerPiece: 12m),
            ],
            []));
        var doughId = await SubAssemblyIdAsync(sourceId, doughRoleId);
        var sourceFillingId = await SubAssemblyIdAsync(sourceId, fillingRoleId);
        var newId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Cheese Roll Mini", Cancellation);

        var usages = await CreateUsageRepository().GetForProductAsync(newId, Cancellation);
        usages.Single(u => u.SubAssembly!.RoleId == doughRoleId).SubAssemblyId.Should().Be(doughId, "the dough is on the Sub-Assemblies screen, so it's linked");
        var filling = usages.Single(u => u.SubAssembly!.RoleId == fillingRoleId);
        filling.SubAssemblyId.Should().NotBe(sourceFillingId);
        filling.SubAssembly!.Name.Should().Be("Cheese Roll Mini Filling: Cheese");
        filling.SubAssembly.OwnerProductId.Should().Be(newId);
        filling.SubAssembly.TotalWeight.Should().Be(100m);
        filling.AuthoredValue.Should().Be(12m);
        (await FindAsync(sourceFillingId))!.OwnerProductId.Should().Be(sourceId, "the source's own is untouched");
    }
}
