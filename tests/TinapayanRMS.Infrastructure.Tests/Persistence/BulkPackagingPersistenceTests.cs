using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A product's bulk packaging (PackagingLine.IsBulk, Product.BulkPackagingQuantity): what one bulk pack takes, such as a box
/// several products share and its tape, each quantity per pack, and how many products the pack holds. Its own whatever its
/// packaging links, kept apart from the lines for each unit.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class BulkPackagingPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private async Task<int> AddPackagingAsync(string name, decimal cost)
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync("pc", "pc");

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes", Type = RawMaterialCategoryType.Packaging };
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

    /// <summary>A wrapper set (one ₱2 wrapper a unit) and a product linking it.</summary>
    private async Task<(int ProductId, int SetId, int WrapperId)> AddLinkedProductAsync()
    {
        var roleId = await AddRoleAsync("Packaging", isPackaging: true);
        var wrapperId = await AddPackagingAsync("Wrapper", 2m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Wrapper Set", RoleId = roleId }, [(wrapperId, null, 1m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);
        return (productId, setId, wrapperId);
    }

    /// <summary>A packaging set of <paramref name="lines"/>, each a quantity per set.</summary>
    private async Task<int> AddSetAsync(string name, params (int MaterialId, decimal QtyPerSet)[] lines)
    {
        int? existingRoleId;
        await using (var dbContext = CreateDbContext())
        {
            existingRoleId = await dbContext.SubAssemblyRoles.Where(r => r.IsPackaging).Select(r => (int?)r.Id).FirstOrDefaultAsync(Cancellation);
        }

        var roleId = existingRoleId ?? await AddRoleAsync("Packaging", isPackaging: true);
        return await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = name, RoleId = roleId }, [.. lines.Select(l => (l.MaterialId, (int?)null, l.QtyPerSet))], Cancellation);
    }

    /// <summary>A bulk pack of <paramref name="lines"/>, each a quantity per pack, holding <paramref name="quantity"/> products.</summary>
    private static BulkPackagingDraft Bulk(int? quantity, params (int MaterialId, decimal QtyPerPack)[] lines) =>
        new([.. lines.Select(l => (l.MaterialId, (int?)null, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerPack))], quantity);

    private Task<int> SaveBulkAsync(int productId, BulkPackagingDraft bulk) =>
        CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Ensaymada" }, recipe: null, packagingLink: null, packagingLines: null, bulk, costing: null, Cancellation);

    [Test]
    public async Task A_linked_product_keeps_its_bulk_pack_apart_from_the_sets_lines_with_how_many_products_it_holds()
    {
        var (productId, setId, wrapperId) = await AddLinkedProductAsync();
        var boxId = await AddPackagingAsync("Box", 12m);
        var tapeId = await AddPackagingAsync("Tape", 0.15m);

        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m), (tapeId, 4m)));

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Lines[productId].Select(l => l.PackagingMaterialId).Should().Equal([wrapperId], "the set's lines, as before");
        packaging.Bulk[productId].Select(l => (l.PackagingMaterialId, l.QtyPerUnit, l.IsBulk)).Should().Equal([(boxId, 1m, true), (tapeId, 4m, true)],
            "each quantity for one pack, as typed");
        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.BulkPackagingQuantity.Should().Be(6);
        (await CreatePackagingLinkRepository().GetForProductAsync(productId, Cancellation))!.LinkedSubAssemblyId.Should().Be(setId, "the set stays linked");
    }

    [Test]
    public async Task Bulk_lines_need_how_many_products_a_pack_holds_and_none_clears_it()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await AddPackagingAsync("Box", 12m);

        var act = () => SaveBulkAsync(productId, Bulk(null, (boxId, 1m)));
        (await act.Should().ThrowAsync<BusinessRuleException>()).WithMessage("Enter how many products one bulk pack holds*");

        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m)));
        await SaveBulkAsync(productId, Bulk(6));

        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.BulkPackagingQuantity.Should().BeNull("no bulk packaging, no quantity");
        (await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation)).Bulk[productId].Should().BeEmpty();
    }

    [Test]
    public async Task A_material_can_be_both_a_line_for_each_unit_and_the_bulk_packs_and_each_save_leaves_the_other_alone()
    {
        var productId = await AddProductAsync("Ensaymada");
        var tapeId = await AddPackagingAsync("Tape", 0.15m);
        var boxId = await AddPackagingAsync("Box", 12m);
        await CreatePackagingLineRepository().SetLinesAsync(productId, [(tapeId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)], Cancellation);

        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m), (tapeId, 4m)));
        await CreatePackagingLineRepository().SetLinesAsync(productId, [(tapeId, null, PackagingLineAuthoredField.QtyPerUnit, 3m)], Cancellation);

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Lines[productId].Select(l => (l.PackagingMaterialId, l.QtyPerUnit)).Should().Equal([(tapeId, 3m)]);
        packaging.Bulk[productId].Select(l => (l.PackagingMaterialId, l.QtyPerUnit)).Should().Equal([(boxId, 1m), (tapeId, 4m)]);
    }

    [Test]
    public async Task Unlinking_and_Move_to_Sub_Assemblies_leave_the_bulk_packaging_as_it_is()
    {
        var (productId, _, wrapperId) = await AddLinkedProductAsync();
        var boxId = await AddPackagingAsync("Box", 12m);
        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m)));

        await CreatePackagingLinkRepository().RemoveLinkAsync(productId, Cancellation);
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Select(l => l.PackagingMaterialId).Should().Equal(
            [wrapperId], "the set's lines become its own; the box isn't one of them");

        await CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Ensaymada" },
            recipe: null,
            new PackagingLinkDraft.NewSet(),
            [(wrapperId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)],
            costing: null,
            Cancellation);

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Bulk[productId].Select(l => l.PackagingMaterialId).Should().Equal([boxId]);
        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.BulkPackagingQuantity.Should().Be(6);
    }

    [Test]
    public async Task Lines_left_from_before_a_product_was_linked_are_still_not_read()
    {
        var roleId = await AddRoleAsync("Packaging", isPackaging: true);
        var wrapperId = await AddPackagingAsync("Wrapper", 2m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Wrapper Set", RoleId = roleId }, [(wrapperId, null, 1m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLineRepository().SetLinesAsync(productId, [(wrapperId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);

        packaging.Lines[productId].Should().ContainSingle("the set's wrapper only, not the one it owned before it linked the set");
        packaging.Bulk[productId].Should().BeEmpty();
    }

    [Test]
    public async Task Bulk_packaging_can_link_a_packaging_set_whose_lines_are_the_packs_apart_from_the_link_for_each_piece()
    {
        var (productId, setId, wrapperId) = await AddLinkedProductAsync();
        var boxId = await AddPackagingAsync("Box", 12m);
        var tapeId = await AddPackagingAsync("Tape", 0.15m);
        var boxSetId = await AddSetAsync("Box of 6", (boxId, 1m), (tapeId, 4m));

        await SaveBulkAsync(productId, new BulkPackagingDraft([], 6, boxSetId));

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Lines[productId].Select(l => l.PackagingMaterialId).Should().Equal([wrapperId], "the packaging per piece still shows its own set");
        packaging.Bulk[productId].Select(l => (l.PackagingMaterialId, l.QtyPerUnit, l.IsBulk)).Should().Equal([(boxId, 1m, true), (tapeId, 4m, true)],
            "the set's lines, each quantity for one pack");
        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.BulkPackagingQuantity.Should().Be(6, "how many a pack holds stays the product's");
        (await CreatePackagingLinkRepository().GetForProductAsync(productId, Cancellation))!.LinkedSubAssemblyId.Should().Be(setId);
        var links = await CreatePackagingLinkRepository().GetForProductsAsync([productId], Cancellation);
        links.Select(l => (l.IsBulk, l.LinkedSubAssemblyId, l.LinkedSubAssembly?.Name)).Should().BeEquivalentTo(
            [(false, (int?)setId, "Wrapper Set"), (true, (int?)boxSetId, "Box of 6")]);
    }

    [Test]
    public async Task One_set_can_be_a_products_packaging_per_piece_and_its_bulk_packaging_at_once()
    {
        var (productId, setId, wrapperId) = await AddLinkedProductAsync();

        await SaveBulkAsync(productId, new BulkPackagingDraft([], 10, setId));

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Lines[productId].Should().ContainSingle().Which.IsBulk.Should().BeFalse();
        packaging.Bulk[productId].Should().ContainSingle().Which.Should().Match<PackagingLine>(l => l.IsBulk && l.PackagingMaterialId == wrapperId);
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneLinkedByAsync(Cancellation))[setId].Should().ContainSingle(
            "the product is named once however it links the set");
    }

    [Test]
    public async Task Linked_bulk_packaging_needs_how_many_products_a_pack_holds_and_has_no_lines_of_its_own()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await AddPackagingAsync("Box", 12m);
        var boxSetId = await AddSetAsync("Box of 6", (boxId, 1m));

        var noQuantity = () => SaveBulkAsync(productId, new BulkPackagingDraft([], null, boxSetId));
        (await noQuantity.Should().ThrowAsync<BusinessRuleException>()).WithMessage("Enter how many products one bulk pack holds*");

        var withLines = () => SaveBulkAsync(productId, new BulkPackagingDraft([(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], 6, boxSetId));
        (await withLines.Should().ThrowAsync<BusinessRuleException>()).WithMessage("Linked bulk packaging shows its packaging set's lines*");

        (await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation)).Bulk[productId].Should().BeEmpty("nothing was saved");
    }

    [Test]
    public async Task Bulk_packaging_links_only_a_packaging_set()
    {
        var productId = await AddProductAsync("Ensaymada");
        var doughRoleId = await AddRoleAsync("Dough");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblies.Add(new SubAssembly { Name = "Sweet Dough", RoleId = doughRoleId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        int doughId;
        await using (var dbContext = CreateDbContext())
        {
            doughId = await dbContext.SubAssemblies.Where(s => s.Name == "Sweet Dough").Select(s => s.Id).SingleAsync(Cancellation);
        }

        var act = () => SaveBulkAsync(productId, new BulkPackagingDraft([], 6, doughId));
        (await act.Should().ThrowAsync<BusinessRuleException>()).WithMessage("A product's bulk packaging can only link a packaging set*");
    }

    [Test]
    public async Task Unlinking_bulk_packaging_writes_the_lines_the_form_holds_and_leaves_the_link_for_each_piece()
    {
        var (productId, setId, _) = await AddLinkedProductAsync();
        var boxId = await AddPackagingAsync("Box", 12m);
        var boxSetId = await AddSetAsync("Box of 6", (boxId, 1m));
        await SaveBulkAsync(productId, new BulkPackagingDraft([], 6, boxSetId));

        // Make a Copy: the set's lines as the product's own.
        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m)));

        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation);
        packaging.Bulk[productId].Select(l => l.PackagingMaterialId).Should().Equal([boxId]);
        var links = await CreatePackagingLinkRepository().GetForProductsAsync([productId], Cancellation);
        links.Should().ContainSingle("the bulk link went").Which.LinkedSubAssemblyId.Should().Be(setId);

        // Remove Link, from a linked one again: no bulk packaging, and no quantity.
        await SaveBulkAsync(productId, new BulkPackagingDraft([], 6, boxSetId));
        await SaveBulkAsync(productId, Bulk(6));
        (await CreatePackagingLineRepository().GetEffectiveForProductsAsync([productId], Cancellation)).Bulk[productId].Should().BeEmpty();
        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.BulkPackagingQuantity.Should().BeNull();
    }

    [Test]
    public async Task A_set_bulk_packaging_links_cant_be_deleted_and_its_materials_reach_the_product()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await AddPackagingAsync("Box", 12m);
        var boxSetId = await AddSetAsync("Box of 6", (boxId, 1m));
        await SaveBulkAsync(productId, new BulkPackagingDraft([], 6, boxSetId));

        var delete = () => new SubAssemblyRepository(CreateDbContextFactory()).DeleteStandaloneAsync(boxSetId, Cancellation);
        (await delete.Should().ThrowAsync<BusinessRuleException>()).WithMessage("*Ensaymada*");

        (await new RawMaterialRepository(CreateDbContextFactory()).GetDependentsAsync(boxId, Cancellation)).Select(d => d.Id).Should().Equal(
            [productId], "a price change to the box moves the product's cost");
    }

    [Test]
    public async Task Bulk_lines_of_a_product_whose_packaging_per_piece_is_linked_reach_it_but_not_products_linking_its_packaging()
    {
        var (productId, _, _) = await AddLinkedProductAsync();
        var otherId = await AddProductAsync("Ensaymada Mini");
        var boxId = await AddPackagingAsync("Box", 12m);
        await SaveBulkAsync(productId, Bulk(6, (boxId, 1m)));
        await CreatePackagingLinkRepository().SetLinkAsync(otherId, productId, Cancellation);

        (await new RawMaterialRepository(CreateDbContextFactory()).GetDependentsAsync(boxId, Cancellation)).Select(d => d.Id).Should().Equal(
            [productId], "its bulk lines are its own cost, and a product linking its packaging reads only the lines for each piece");
    }

    [Test]
    public async Task A_clone_links_the_set_the_sources_bulk_packaging_links()
    {
        var sourceId = await AddProductAsync("Ensaymada");
        var boxId = await AddPackagingAsync("Box", 12m);
        var boxSetId = await AddSetAsync("Box of 6", (boxId, 1m));
        await SaveBulkAsync(sourceId, new BulkPackagingDraft([], 6, boxSetId));

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Ensaymada Copy", Cancellation);

        var links = await CreatePackagingLinkRepository().GetForProductsAsync([cloneId], Cancellation);
        links.Should().ContainSingle().Which.Should().Match<PackagingLink>(l => l.IsBulk && l.LinkedSubAssemblyId == boxSetId);
        (await CreateProductRepository().GetByIdAsync(cloneId, Cancellation))!.BulkPackagingQuantity.Should().Be(6);
    }

    [Test]
    public async Task A_clone_gets_the_sources_bulk_packaging_and_a_variant_none()
    {
        var (sourceId, setId, _) = await AddLinkedProductAsync();
        var boxId = await AddPackagingAsync("Box", 12m);
        await SaveBulkAsync(sourceId, Bulk(6, (boxId, 1m)));
        var cloner = new ProductCloner(CreateDbContextFactory());

        var cloneId = await cloner.CloneAsync(sourceId, "Ensaymada Copy", Cancellation);
        var variantId = await cloner.CreateVariantAsync(sourceId, "Ensaymada Small", Cancellation);

        (await CreatePackagingLinkRepository().GetForProductAsync(cloneId, Cancellation))!.LinkedSubAssemblyId.Should().Be(setId);
        var packaging = await CreatePackagingLineRepository().GetEffectiveForProductsAsync([cloneId, variantId], Cancellation);
        packaging.Bulk[cloneId].Select(l => (l.PackagingMaterialId, l.QtyPerUnit)).Should().Equal([(boxId, 1m)]);
        (await CreateProductRepository().GetByIdAsync(cloneId, Cancellation))!.BulkPackagingQuantity.Should().Be(6);
        packaging.Bulk[variantId].Should().BeEmpty("a variant has no packaging");
        (await CreateProductRepository().GetByIdAsync(variantId, Cancellation))!.BulkPackagingQuantity.Should().BeNull();
    }
}
