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
/// A composite product (Product.IsComposite): one made of two or more other products, each at a whole number of its
/// sellable units (ProductComponent), and its own packaging, saved with the rest of the product
/// (IProductRepository.SaveAsync, the recipe's Components), cloned with it, and guarding the products it holds.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CompositeProductPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private ProductCloner CreateCloner() => new(CreateDbContextFactory());

    private static RecipeDraft Holding(params (int ProductId, int Quantity)[] components) =>
        new([], []) { Components = components };

    /// <summary>A composite named <paramref name="name"/>, saved as the Products screen's Save saves a new one.</summary>
    private Task<int> AddCompositeAsync(string name, params (int ProductId, int Quantity)[] components) =>
        CreateRepository().SaveAsync(
            new Product { Name = name, IsComposite = true }, Holding(components), packagingLink: null, packagingLines: null, costing: null, Cancellation);

    /// <summary>Saves <paramref name="compositeId"/> holding <paramref name="components"/> in place of what it held, keeping the rest of it.</summary>
    private async Task SaveComponentsAsync(int compositeId, params (int ProductId, int Quantity)[] components)
    {
        var repository = CreateRepository();
        var composite = (await repository.GetByIdAsync(compositeId, Cancellation))!;
        await repository.SaveAsync(composite, Holding(components), packagingLink: null, packagingLines: null, costing: null, Cancellation);
    }

    private async Task<IReadOnlyList<(string Name, int Quantity)>> HeldAsync(int compositeId) =>
        [.. (await CreateRepository().GetComponentsAsync(compositeId, Cancellation)).Select(c => (c.ComponentProduct!.Name, c.Quantity))];

    private async Task<int> AddPackagingMaterialAsync(string name)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync();

        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            CategoryId = categoryId,
            BaseUnitId = pieceId,
            CostPerBaseUnit = 12m,
            BufferedCostPerBaseUnit = 12m,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Fact]
    public async Task A_composite_saves_with_its_products_in_its_tables_order_and_reads_back_as_a_composite()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var pandesalId = await AddProductAsync("Pandesal");

        var boxId = await AddCompositeAsync("Pasalubong Box", (cheeseRollId, 4), (ensaymadaId, 6), (pandesalId, 10));

        (await CreateRepository().GetByIdAsync(boxId, Cancellation))!.IsComposite.Should().BeTrue();
        (await CreateRepository().GetByIdAsync(ensaymadaId, Cancellation))!.IsComposite.Should().BeFalse();
        (await HeldAsync(boxId)).Should().Equal(("Cheese Roll", 4), ("Ensaymada", 6), ("Pandesal", 10));

        // Dragged into another order, with Pandesal taken out: the order saved is the order read.
        await SaveComponentsAsync(boxId, (ensaymadaId, 6), (cheeseRollId, 3));

        (await HeldAsync(boxId)).Should().Equal(("Ensaymada", 6), ("Cheese Roll", 3));
        var all = await CreateRepository().GetAllComponentsAsync(Cancellation);
        all.Select(c => (c.ProductId, c.ComponentProductId)).Should().Equal((boxId, ensaymadaId), (boxId, cheeseRollId));
    }

    [Fact]
    public async Task A_composite_needs_two_different_products_and_nothing_is_saved_without_them()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");

        var withOne = () => AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6));
        var withNone = () => CreateRepository().SaveAsync(
            new Product { Name = "Party Package", IsComposite = true }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        await withOne.Should().ThrowAsync<InvalidOperationException>().WithMessage("A composite product needs at least two different products.");
        await withNone.Should().ThrowAsync<InvalidOperationException>().WithMessage("A composite product needs at least two different products.");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.CountAsync(Cancellation)).Should().Be(1, "each refused save rolled its new product back");
    }

    [Fact]
    public async Task A_product_goes_on_one_line_at_a_whole_quantity()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");

        var twice = () => AddCompositeAsync("Pasalubong Box", (ensaymadaId, 2), (cheeseRollId, 4), (ensaymadaId, 4));
        var none = () => AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 0));

        await twice.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada is on two lines*");
        await none.Should().ThrowAsync<InvalidOperationException>().WithMessage("Enter how many Cheese Roll go in*");
    }

    [Fact]
    public async Task A_composite_can_hold_another_but_never_end_up_holding_itself()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var cakeId = await AddProductAsync("Chocolate Cake");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var partyId = await AddCompositeAsync("Party Package", (boxId, 2), (cakeId, 1));
        var trayId = await AddCompositeAsync("Office Tray", (partyId, 1), (ensaymadaId, 12));

        var itself = () => SaveComponentsAsync(boxId, (ensaymadaId, 6), (boxId, 1));
        var directly = () => SaveComponentsAsync(boxId, (ensaymadaId, 6), (partyId, 1));
        var throughAnother = () => SaveComponentsAsync(boxId, (ensaymadaId, 6), (trayId, 1));

        await itself.Should().ThrowAsync<InvalidOperationException>().WithMessage("A composite product can't contain itself.");
        await directly.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "Party Package can't go into Pasalubong Box: it already contains Pasalubong Box, so Pasalubong Box would end up containing itself.");
        await throughAnother.Should().ThrowAsync<InvalidOperationException>().WithMessage("Office Tray can't go into Pasalubong Box*");
        (await HeldAsync(boxId)).Should().Equal(("Ensaymada", 6), ("Cheese Roll", 4));
    }

    [Fact]
    public async Task A_composites_sections_are_in_product_owned_roles_and_a_product_of_sub_assemblies_holds_no_products()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Glaze", RoleId = toppingRoleId }, [(icingId, 500m)], Cancellation);

        // Its own topping, new, and a topping from the Sub-Assemblies screen, linked.
        await SaveRecipeAsync(boxId, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(icingId, 500m)], UsageAuthoredField.WeightPerPiece, 100m, SectionName: "Icing")],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 50m)]) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] });

        var sectionInComposite = () => SaveRecipeAsync(boxId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)],
            []) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] });
        var productsInSingle = () => SaveRecipeAsync(ensaymadaId, Holding((cheeseRollId, 1), (boxId, 1)));

        await sectionInComposite.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "Pasalubong Box can only have sections in product-owned roles, such as a topping or filling. Dough isn't one: tick Product-owned for it in Settings ▸ Roles, or take that section out.");
        await productsInSingle.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada is made of sub-assemblies*");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.Where(u => u.ProductId == boxId).Select(u => u.SubAssembly!.RoleId).ToListAsync(Cancellation))
            .Should().Equal([toppingRoleId, toppingRoleId], "the refused save left the two toppings as they were");
        (await dbContext.SubAssemblies.CountAsync(s => s.OwnerProductId == boxId, Cancellation)).Should().Be(1, "its new topping is its own");
        (await HeldAsync(boxId)).Should().Equal(("Ensaymada", 6), ("Cheese Roll", 4));
        (await dbContext.ProductComponents.CountAsync(c => c.ProductId == ensaymadaId, Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_role_a_composite_has_a_section_in_stays_product_owned_and_a_linked_one_keeps_a_product_owned_role()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 4, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var glazeId = await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Name = "House Glaze", RoleId = toppingRoleId }, [(icingId, 500m)], Cancellation);
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        var partyId = await AddCompositeAsync("Party Box", (ensaymadaId, 12), (cheeseRollId, 8));
        foreach (var id in new[] { boxId, partyId })
        {
            await SaveRecipeAsync(id, new RecipeDraft([], [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 50m)])
            {
                Components = [.. (await CreateRepository().GetComponentsAsync(id, Cancellation)).Select(c => (c.ComponentProductId, c.Quantity))],
            });
        }

        var roles = new SubAssemblyRoleRepository(CreateDbContextFactory());
        var untick = () => roles.UpdateAsync(new SubAssemblyRole { Id = toppingRoleId, Name = "Topping", SortOrder = 3, IsProductOwned = false }, Cancellation);
        var toDough = () => subAssemblies.SaveStandaloneAsync(new SubAssembly { Id = glazeId, Name = "House Glaze", RoleId = doughRoleId }, [(icingId, 500m)], Cancellation);

        await untick.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "Party Box, Pasalubong Box have a Topping section, and a composite product can only have sections in product-owned roles. Take it out there first, then untick Product-owned.");
        await toDough.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "Party Box, Pasalubong Box have House Glaze as a section*which Dough isn't*");

        // Another product-owned role is fine, and so is unticking one no composite uses.
        await subAssemblies.SaveStandaloneAsync(new SubAssembly { Id = glazeId, Name = "House Glaze", RoleId = fillingRoleId }, [(icingId, 500m)], Cancellation);
        await roles.UpdateAsync(new SubAssemblyRole { Id = toppingRoleId, Name = "Topping", SortOrder = 3, IsProductOwned = false }, Cancellation);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyRoles.SingleAsync(r => r.Id == toppingRoleId, Cancellation)).IsProductOwned.Should().BeFalse();
    }

    [Fact]
    public async Task Whether_a_product_is_a_composite_is_fixed_once_saved()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));

        var becoming = () => CreateRepository().SaveAsync(
            new Product { Id = ensaymadaId, Name = "Ensaymada", IsComposite = true }, null, null, null, null, Cancellation);
        var leaving = () => CreateRepository().UpdateAsync(new Product { Id = boxId, Name = "Pasalubong Box" }, Cancellation);

        await becoming.Should().ThrowAsync<InvalidOperationException>().WithMessage("A product can't become a composite product*");
        await leaving.Should().ThrowAsync<InvalidOperationException>().WithMessage("A composite product can't become a single product*");
        (await CreateRepository().GetByIdAsync(boxId, Cancellation))!.IsComposite.Should().BeTrue();
    }

    [Fact]
    public async Task A_composite_is_one_sellable_unit_and_only_Save_adds_one()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));

        var twoBoxes = () => CreateRepository().UpdateAsync(
            new Product { Id = boxId, Name = "Pasalubong Box", IsComposite = true, QuantityRequired = 2 }, Cancellation);
        var addedBare = () => CreateRepository().AddAsync(new Product { Name = "Party Package", IsComposite = true }, Cancellation);

        await twoBoxes.Should().ThrowAsync<InvalidOperationException>().WithMessage("A composite product is one sellable unit*");
        await addedBare.Should().ThrowAsync<InvalidOperationException>();

        // The database holds to it too.
        await using var dbContext = CreateDbContext();
        var box = await dbContext.Products.SingleAsync(p => p.Id == boxId, Cancellation);
        box.QuantityRequired = 2;
        var direct = () => dbContext.SaveChangesAsync(Cancellation);
        await direct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Including_the_products_packaging_saves_with_a_composite_is_copied_by_Clone_and_is_refused_for_any_other_product()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));

        var box = (await CreateRepository().GetByIdAsync(boxId, Cancellation))!;
        box.IncludesComponentPackaging.Should().BeFalse("a new composite leaves its products' packaging out");
        box.IncludesComponentPackaging = true;
        await CreateRepository().SaveAsync(box, null, null, null, null, Cancellation);

        (await CreateRepository().GetByIdAsync(boxId, Cancellation))!.IncludesComponentPackaging.Should().BeTrue();
        var cloneId = await CreateCloner().CloneAsync(boxId, "Pasalubong Box Copy", Cancellation);
        (await CreateRepository().GetByIdAsync(cloneId, Cancellation))!.IncludesComponentPackaging.Should().BeTrue();

        var single = () => CreateRepository().SaveAsync(
            new Product { Id = ensaymadaId, Name = "Ensaymada", IncludesComponentPackaging = true }, null, null, null, null, Cancellation);
        await single.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only a composite product can include*");

        // The database holds to it too.
        await using var dbContext = CreateDbContext();
        var ensaymada = await dbContext.Products.SingleAsync(p => p.Id == ensaymadaId, Cancellation);
        ensaymada.IncludesComponentPackaging = true;
        var direct = () => dbContext.SaveChangesAsync(Cancellation);
        await direct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_product_a_composite_holds_cant_be_deleted_until_its_taken_out_and_deleting_the_composite_keeps_its_products()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var boxId = await AddCompositeAsync("Pasalubong Box", (ensaymadaId, 6), (cheeseRollId, 4));
        await AddCompositeAsync("Merienda Set", (ensaymadaId, 2), (boxId, 1));

        var deleteHeld = () => CreateRepository().DeleteAsync(ensaymadaId, Cancellation);

        await deleteHeld.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Merienda Set, Pasalubong Box hold this product. Take it out there first.");

        var deleteBox = () => CreateRepository().DeleteAsync(boxId, Cancellation);
        await deleteBox.Should().ThrowAsync<InvalidOperationException>().WithMessage("Merienda Set holds this product. Take it out there first.");

        // A deleted composite still holds its products, so it can be restored whole, until it's deleted permanently.
        var setId = (await CreateRepository().GetAllAsync(Cancellation)).Single(p => p.Name == "Merienda Set").Id;
        await CreateRepository().DeleteAsync(setId, Cancellation);
        await deleteBox.Should().ThrowAsync<InvalidOperationException>().WithMessage("Merienda Set (deleted) holds this product. Take it out there first.");
        var deleteBoxPermanently = () => CreateRepository().DeletePermanentlyAsync(boxId, Cancellation);
        await deleteBoxPermanently.Should().ThrowAsync<InvalidOperationException>().WithMessage("Merienda Set (deleted) holds this product.*");

        await CreateRepository().DeletePermanentlyAsync(setId, Cancellation);
        await CreateRepository().DeletePermanentlyAsync(boxId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.ProductComponents.CountAsync(Cancellation)).Should().Be(0, "a composite's lines go with it");
        (await dbContext.Products.Select(p => p.Name).ToListAsync(Cancellation)).Should().BeEquivalentTo(["Ensaymada", "Cheese Roll"]);
        await CreateRepository().DeleteAsync(ensaymadaId, Cancellation);
    }

    [Fact]
    public async Task Clone_and_Create_Variant_hold_the_same_products_the_variant_linking_the_packaging()
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        var giftBoxId = await AddPackagingMaterialAsync("Gift Box");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var boxId = await AddCompositeAsync("Pasalubong Box", (cheeseRollId, 4), (ensaymadaId, 6));
        await SaveRecipeAsync(boxId, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(icingId, 500m)], UsageAuthoredField.WeightPerPiece, 100m)],
            []) { Components = [(cheeseRollId, 4), (ensaymadaId, 6)] });
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(boxId, [(giftBoxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

        var cloneId = await CreateCloner().CloneAsync(boxId, "Pasalubong Box Copy", Cancellation);
        var variantId = await CreateCloner().CreateVariantAsync(boxId, "Pasalubong Box Blue", Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            foreach (var id in new[] { cloneId, variantId })
            {
                (await CreateRepository().GetByIdAsync(id, Cancellation))!.IsComposite.Should().BeTrue();
                (await HeldAsync(id)).Should().Equal(("Cheese Roll", 4), ("Ensaymada", 6));

                // Its topping is its own copy, as any product's own section is.
                var topping = await dbContext.ProductSubAssemblyUsages.Where(u => u.ProductId == id).Select(u => u.SubAssembly!).SingleAsync(Cancellation);
                topping.RoleId.Should().Be(toppingRoleId);
                topping.OwnerProductId.Should().Be(id);
            }
        }

        var packagingLines = new PackagingLineRepository(CreateDbContextFactory());
        (await packagingLines.GetForProductAsync(cloneId, Cancellation)).Select(l => l.PackagingMaterialId).Should().Equal(giftBoxId);
        var variantLink = await new PackagingLinkRepository(CreateDbContextFactory()).GetForProductAsync(variantId, Cancellation);
        variantLink!.LinkedProductId.Should().Be(boxId);
    }
}
