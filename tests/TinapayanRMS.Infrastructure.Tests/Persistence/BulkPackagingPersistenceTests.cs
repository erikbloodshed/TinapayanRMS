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

    /// <summary>A bulk pack of <paramref name="lines"/>, each a quantity per pack, holding <paramref name="quantity"/> products.</summary>
    private static BulkPackagingDraft Bulk(int? quantity, params (int MaterialId, decimal QtyPerPack)[] lines) =>
        new([.. lines.Select(l => (l.MaterialId, (int?)null, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerPack))], quantity);

    private Task SaveBulkAsync(int productId, BulkPackagingDraft bulk) =>
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
