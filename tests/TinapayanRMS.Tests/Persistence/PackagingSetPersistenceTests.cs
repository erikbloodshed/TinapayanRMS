using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// Packaging sets: standalone sub-assemblies in the Packaging role, whose lines are packaging materials per set.
/// Saved and deleted on the Sub-Assemblies screen, kept apart from recipes, and linked by a product's packaging,
/// one set per piece.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class PackagingSetPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRepository CreateRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory(), CreatePackagingLineRepository());

    private Task<int> SaveSetAsync(string name, int roleId, IReadOnlyList<(int PackagingMaterialId, decimal QtyPerSet)> lines, int id = 0) =>
        CreateRepository().SaveStandalonePackagingAsync(new SubAssembly { Id = id, Name = name, RoleId = roleId }, lines, Cancellation);

    /// <summary>A packaging material with <paramref name="baseUnit"/> as its base unit, costing <paramref name="cost"/> a base unit.</summary>
    private async Task<int> AddPackagingAsync(string name, decimal cost, string baseUnit = "pc")
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync(baseUnit, baseUnit);

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes" };
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            Category = category,
            BaseUnitId = unitId,
            CostPerBaseUnit = cost,
            BufferedCostPerBaseUnit = cost,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Fact]
    public async Task Product_Save_writes_the_packaging_link_with_the_rest_and_None_takes_it_away()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var bigId = await AddProductAsync("Big");
        var repository = new ProductRepository(CreateDbContextFactory());

        var id = await repository.SaveAsync(
            new Product { Name = "Small" }, recipe: null, new PackagingLinkDraft(null, setId), packagingLines: null, packs: null, costing: null, Cancellation);
        (await CreatePackagingLinkRepository().GetForProductAsync(id, Cancellation))!.LinkedSubAssemblyId.Should().Be(setId, "a new product can link on its first save");

        await repository.SaveAsync(
            new Product { Id = id, Name = "Small" }, recipe: null, new PackagingLinkDraft(bigId, null), packagingLines: null, packs: null, costing: null, Cancellation);
        (await CreatePackagingLinkRepository().GetForProductAsync(id, Cancellation))!.LinkedProductId.Should().Be(bigId);

        await repository.SaveAsync(
            new Product { Id = id, Name = "Small" },
            recipe: null,
            PackagingLinkDraft.None,
            [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)],
            packs: null,
            costing: null,
            Cancellation);
        (await CreatePackagingLinkRepository().GetForProductAsync(id, Cancellation)).Should().BeNull();
        (await CreatePackagingLineRepository().GetForProductAsync(id, Cancellation)).Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m,
            "unlinked in the same save as its own lines are written");
    }

    [Fact]
    public async Task Product_Save_refuses_a_circular_packaging_link_and_saves_nothing()
    {
        var aId = await AddProductAsync("A");
        var bId = await AddProductAsync("B");
        await CreatePackagingLinkRepository().SetLinkAsync(bId, aId, Cancellation);

        var act = () => new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Id = aId, Name = "A renamed" }, recipe: null, new PackagingLinkDraft(bId, null), packagingLines: null, packs: null, costing: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*circular*");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(p => p.Id == aId, Cancellation)).Name.Should().Be("A", "nothing is left half saved");
    }

    [Fact]
    public async Task Saving_a_set_stores_its_lines_per_set_and_leaves_its_recipe_totals_at_zero()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");

        var id = await SaveSetAsync(" Gift Box Set ", packagingRoleId, [(boxId, 1m), (ribbonId, 30m)]);

        await using var dbContext = CreateDbContext();
        var set = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation);
        set.Name.Should().Be("Gift Box Set");
        set.OwnerProductId.Should().BeNull();
        set.TotalWeight.Should().Be(0m);
        set.TotalCost.Should().Be(0m);
        var lines = await CreateRepository().GetPackagingLinesAsync(id, Cancellation);
        lines.Select(l => (l.PackagingMaterialId, l.QtyPerSet)).Should().Equal((boxId, 1m), (ribbonId, 30m));
        lines.Should().OnlyContain(l => l.PackagingMaterial!.BaseUnit != null);
    }

    [Fact]
    public async Task Resaving_a_set_replaces_its_lines()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        var id = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m), (ribbonId, 30m)]);

        await SaveSetAsync("Gift Box Set", packagingRoleId, [(ribbonId, 45m)], id);

        (await CreateRepository().GetPackagingLinesAsync(id, Cancellation))
            .Should().ContainSingle().Which.QtyPerSet.Should().Be(45m);
    }

    [Fact]
    public async Task A_set_needs_the_packaging_role_and_packaging_materials()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var glazeRoleId = await AddRoleAsync("Glaze");
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var sugarId = await AddIngredientAsync("Sugar");

        var inRecipeRole = () => SaveSetAsync("Gift Box Set", glazeRoleId, [(boxId, 1m)]);
        var withIngredient = () => SaveSetAsync("Gift Box Set", packagingRoleId, [(sugarId, 1m)]);

        await inRecipeRole.Should().ThrowAsync<InvalidOperationException>().WithMessage("*goes in the Packaging role*");
        await withIngredient.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Sugar*");
        (await CreateDbContext().SubAssemblies.CountAsync(Cancellation)).Should().Be(0, "nothing is left half saved");
    }

    [Fact]
    public async Task A_recipe_cant_be_saved_in_the_packaging_role_and_neither_kind_can_become_the_other()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var glazeRoleId = await AddRoleAsync("Glaze");
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var sugarId = await AddIngredientAsync("Sugar", 0.09m);
        var repository = CreateRepository();
        var glazeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = glazeRoleId }, [(sugarId, 100m)], Cancellation);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);

        var newRecipeAsPackaging = () => repository.SaveStandaloneAsync(new SubAssembly { Name = "Syrup", RoleId = packagingRoleId }, [(sugarId, 100m)], Cancellation);
        var recipeToSet = () => SaveSetAsync("Glaze", packagingRoleId, [(boxId, 1m)], glazeId);
        var setToRecipe = () => repository.SaveStandaloneAsync(new SubAssembly { Id = setId, Name = "Gift Box Set", RoleId = glazeRoleId }, [(sugarId, 100m)], Cancellation);

        await newRecipeAsPackaging.Should().ThrowAsync<InvalidOperationException>().WithMessage("*for packaging sets*");
        await recipeToSet.Should().ThrowAsync<InvalidOperationException>().WithMessage("*recipe can't move to the Packaging role*");
        await setToRecipe.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stays in the Packaging role*");
    }

    [Fact]
    public async Task A_products_own_sub_assembly_cant_take_the_packaging_role()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var sugarId = await AddIngredientAsync("Sugar");
        var productId = await AddProductAsync("Cream Loaf");

        var act = () => SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, packagingRoleId, [(sugarId, 100m)], UsageAuthoredField.Yield, 10m)], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*for packaging sets*");
    }

    [Fact]
    public async Task A_products_packaging_linked_to_a_set_shows_its_lines_one_set_per_piece_and_follows_it()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m), (ribbonId, 30m)]);
        var productId = await AddProductAsync("Ensaymada");

        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);

        var lines = await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation);
        lines.Select(l => (l.PackagingMaterialId, l.QtyPerUnit, l.AuthoredField)).Should().Equal(
            (boxId, 1m, PackagingLineAuthoredField.QtyPerUnit),
            (ribbonId, 30m, PackagingLineAuthoredField.QtyPerUnit));
        lines.Should().OnlyContain(l => l.ProductId == productId && l.PackId == null && l.UnitId == null && l.PackagingMaterial != null);
        var link = await CreatePackagingLinkRepository().GetForProductAsync(productId, Cancellation);
        link!.LinkedSubAssembly!.Name.Should().Be("Gift Box Set");
        link.LinkedProductId.Should().BeNull();

        await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 2m)], setId);

        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation))
            .Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m, "a link follows every change saved to the set");
    }

    [Fact]
    public async Task A_set_is_reached_through_a_chain_of_product_links()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var linkRepository = CreatePackagingLinkRepository();
        await linkRepository.SetSubAssemblyLinkAsync(bigId, setId, Cancellation);

        await linkRepository.SetLinkAsync(smallId, bigId, Cancellation);

        var line = (await CreatePackagingLineRepository().GetEffectiveForProductAsync(smallId, Cancellation)).Should().ContainSingle().Subject;
        line.PackagingMaterialId.Should().Be(boxId);
        line.ProductId.Should().Be(bigId, "the set's lines are shown as the product linking it");
    }

    [Fact]
    public async Task Relinking_switches_between_a_product_and_a_set()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var linkRepository = CreatePackagingLinkRepository();

        await linkRepository.SetSubAssemblyLinkAsync(smallId, setId, Cancellation);
        await linkRepository.SetLinkAsync(smallId, bigId, Cancellation);
        var toProduct = await linkRepository.GetForProductAsync(smallId, Cancellation);
        await linkRepository.SetSubAssemblyLinkAsync(smallId, setId, Cancellation);
        var toSet = await linkRepository.GetForProductAsync(smallId, Cancellation);

        (toProduct!.LinkedProductId, toProduct.LinkedSubAssemblyId).Should().Be((bigId, null));
        (toSet!.LinkedProductId, toSet.LinkedSubAssemblyId).Should().Be((null, setId));
    }

    [Fact]
    public async Task Only_a_standalone_packaging_set_can_be_linked_as_packaging()
    {
        var glazeRoleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await CreateRepository().SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = glazeRoleId }, [(sugarId, 100m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");

        var act = () => CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, glazeId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only link a packaging set*");
    }

    [Fact]
    public async Task Unlinking_a_set_copies_its_lines_into_the_products_own()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m), (ribbonId, 30m)]);
        var productId = await AddProductAsync("Ensaymada");
        var linkRepository = CreatePackagingLinkRepository();
        await linkRepository.SetSubAssemblyLinkAsync(productId, setId, Cancellation);

        await linkRepository.RemoveLinkAsync(productId, Cancellation);

        (await linkRepository.GetForProductAsync(productId, Cancellation)).Should().BeNull();
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation))
            .Select(l => (l.PackagingMaterialId, l.QtyPerUnit)).Should().BeEquivalentTo([(boxId, 1m), (ribbonId, 30m)]);
    }

    [Fact]
    public async Task A_clone_keeps_a_set_link_its_source_makes_directly()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Ensaymada (copy)", Cancellation);

        var link = await CreatePackagingLinkRepository().GetForProductAsync(cloneId, Cancellation);
        link!.LinkedSubAssemblyId.Should().Be(setId);
        (await CreatePackagingLineRepository().GetForProductAsync(cloneId, Cancellation)).Should().BeEmpty("the clone reads the set, it doesn't copy it");
    }

    [Fact]
    public async Task A_clone_copies_a_set_it_reaches_through_another_products_link()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var linkRepository = CreatePackagingLinkRepository();
        await linkRepository.SetSubAssemblyLinkAsync(bigId, setId, Cancellation);
        await linkRepository.SetLinkAsync(smallId, bigId, Cancellation);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(smallId, "Small (copy)", Cancellation);

        (await linkRepository.GetForProductAsync(cloneId, Cancellation)).Should().BeNull();
        (await CreatePackagingLineRepository().GetForProductAsync(cloneId, Cancellation)).Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
    }

    [Fact]
    public async Task A_linked_set_cant_be_deleted_and_says_which_products_link_it()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);

        (await CreateRepository().GetStandaloneLinkedByAsync(Cancellation))[setId].Should().Equal("Ensaymada");
        var act = () => CreateRepository().DeleteStandaloneAsync(setId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada links Gift Box Set*");
        await CreatePackagingLinkRepository().RemoveLinkAsync(productId, Cancellation);
        await CreateRepository().DeleteStandaloneAsync(setId, Cancellation);
        (await CreateDbContext().SubAssemblyPackagingLines.CountAsync(Cancellation)).Should().Be(0, "its lines go with it");
    }

    [Fact]
    public async Task Sets_arent_offered_as_recipe_sub_assemblies_to_link_or_copy()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var glazeRoleId = await AddRoleAsync("Glaze");
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var sugarId = await AddIngredientAsync("Sugar");
        await SaveSetAsync("Gift Box Set", packagingRoleId, [(boxId, 1m)]);
        await CreateRepository().SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = glazeRoleId }, [(sugarId, 100m)], Cancellation);

        (await CreateRepository().GetLinkableAsync(Cancellation)).Should().ContainSingle().Which.Name.Should().Be("Glaze");
    }

    [Fact]
    public async Task The_packaging_role_cant_be_deleted_but_other_roles_can()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var glazeRoleId = await AddRoleAsync("Glaze");
        var roleRepository = new SubAssemblyRoleRepository(CreateDbContextFactory());

        var act = () => roleRepository.DeleteAsync(packagingRoleId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't be deleted*");
        await roleRepository.DeleteAsync(glazeRoleId, Cancellation);
        (await roleRepository.GetAllAsync(Cancellation)).Should().ContainSingle().Which.IsPackaging.Should().BeTrue();
    }

    [Fact]
    public async Task A_packaging_materials_base_unit_is_fixed_once_a_set_uses_it()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        await SaveSetAsync("Gift Box Set", packagingRoleId, [(ribbonId, 30m)]);
        var meterId = await GetOrCreateUnitOfMeasureAsync("m", "m");
        var repository = new RawMaterialRepository(CreateDbContextFactory(), CreateCostPropagator());
        var ribbon = await repository.GetByIdAsync(ribbonId, Cancellation);

        ribbon!.BaseUnitId = meterId;
        ribbon.BaseUnit = null;
        var act = () => repository.UpdateAsync(ribbon, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
