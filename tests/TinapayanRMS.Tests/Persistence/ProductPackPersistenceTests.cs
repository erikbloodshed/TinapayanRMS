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
/// A product's sellable packs (IProductPackRepository): the packs themselves, and the packaging each one
/// takes on its own — its box and label, paid once a pack rather than once a piece.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductPackPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductPackRepository CreatePackRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedPackagingAsync(string name, decimal costPerBaseUnit)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            Category = new RawMaterialCategory { Name = $"{name} category" },
            BaseUnitId = pieceId,
            CostPerBaseUnit = costPerBaseUnit,
            BufferedCostPerBaseUnit = costPerBaseUnit,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private static ProductPackDraft Pack(string name, int pieces, params (int MaterialId, decimal Qty)[] lines) =>
        new(0, name, pieces, [.. lines.Select(l => (l.MaterialId, (int?)null, PackagingLineAuthoredField.QtyPerUnit, l.Qty))]);

    private async Task<IReadOnlyList<PackagingLine>> PackLinesAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.PackagingLines
            .AsNoTracking()
            .Where(l => l.ProductId == productId && l.PackId != null)
            .OrderBy(l => l.Id)
            .ToListAsync(Cancellation);
    }

    [Fact]
    public async Task SetPacksAsync_stores_a_products_packs_with_their_own_packaging_lines()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);

        await CreatePackRepository().SetPacksAsync(
            productId,
            [Pack("Pack of 8", 8, (boxId, 1m)), Pack("Pack of 12", 12, (boxId, 1m))],
            Cancellation);

        var packs = await CreatePackRepository().GetForProductAsync(productId, Cancellation);
        packs.Select(p => (p.Name, p.PiecesPerPack)).Should().Equal(("Pack of 8", 8), ("Pack of 12", 12));
        packs.Should().AllSatisfy(p => p.Lines.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId));
    }

    [Fact]
    public async Task SetPacksAsync_keeps_a_packs_id_when_only_its_name_changes()
    {
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12)], Cancellation);
        var savedId = (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Single().Id;

        await CreatePackRepository().SetPacksAsync(
            productId,
            [new ProductPackDraft(savedId, "Dozen Box", 12, [])],
            Cancellation);

        var pack = (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Single();
        pack.Id.Should().Be(savedId, "a pack with an id is updated, not deleted and recreated");
        pack.Name.Should().Be("Dozen Box");
    }

    [Fact]
    public async Task SetPacksAsync_deletes_a_removed_packs_lines_before_the_pack()
    {
        // PackagingLine.PackId is Restrict, so nothing takes the lines with the pack: the repository has to.
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        await CreatePackRepository().SetPacksAsync(productId, [], Cancellation);

        (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await PackLinesAsync(productId)).Should().BeEmpty("the pack's lines go with it");
    }

    [Fact]
    public async Task SetPacksAsync_lets_a_renamed_pack_take_a_name_a_deleted_one_gave_up()
    {
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 8", 8), Pack("Pack of 12", 12)], Cancellation);
        var twelveId = (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Single(p => p.PiecesPerPack == 12).Id;

        // The pack of 8 goes, and the pack of 12 takes its name: only works because removals happen first.
        await CreatePackRepository().SetPacksAsync(productId, [new ProductPackDraft(twelveId, "Pack of 8", 12, [])], Cancellation);

        var pack = (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Single();
        pack.Name.Should().Be("Pack of 8");
        pack.PiecesPerPack.Should().Be(12);
    }

    [Fact]
    public async Task SetPacksAsync_rejects_two_packs_with_the_same_name()
    {
        var productId = await AddProductAsync("Ensaymada");

        var act = () => CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12), Pack("pack of 12", 6)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPacksAsync_rejects_a_pack_that_holds_no_pieces()
    {
        var productId = await AddProductAsync("Ensaymada");

        var act = () => CreatePackRepository().SetPacksAsync(productId, [Pack("Empty Pack", 0)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPacksAsync_rejects_a_pack_belonging_to_another_product()
    {
        var productId = await AddProductAsync("Ensaymada");
        var otherId = await AddProductAsync("Pandesal");
        await CreatePackRepository().SetPacksAsync(otherId, [Pack("Pack of 12", 12)], Cancellation);
        var othersPackId = (await CreatePackRepository().GetForProductAsync(otherId, Cancellation)).Single().Id;

        var act = () => CreatePackRepository().SetPacksAsync(
            productId, [new ProductPackDraft(othersPackId, "Pack of 12", 12, [])], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPacksAsync_rejects_the_same_packaging_material_twice_on_one_pack()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);

        var act = () => CreatePackRepository().SetPacksAsync(
            productId, [Pack("Pack of 12", 12, (boxId, 1m), (boxId, 2m))], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_material_can_wrap_the_piece_and_pack_the_pack()
    {
        // A cello sheet around each piece, and the same sheet shrink-wrapping the pack of 12 as a whole.
        var productId = await AddProductAsync("Ensaymada");
        var celloId = await SeedPackagingAsync("Cello Sheet", 2m);

        await CreatePackagingLineRepository().SetLinesAsync(
            productId, [(celloId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (celloId, 1m))], Cancellation);

        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation))
            .Should().ContainSingle("the per-piece group still has exactly its own one line");
        (await PackLinesAsync(productId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Setting_a_packs_lines_leaves_the_per_piece_lines_alone()
    {
        var productId = await AddProductAsync("Ensaymada");
        var wrapperId = await SeedPackagingAsync("Wrapper", 1m);
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackagingLineRepository().SetLinesAsync(
            productId, [(wrapperId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        var perPiece = await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation);
        perPiece.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(wrapperId);
    }

    [Fact]
    public async Task Setting_the_per_piece_lines_leaves_a_packs_lines_alone()
    {
        var productId = await AddProductAsync("Ensaymada");
        var wrapperId = await SeedPackagingAsync("Wrapper", 1m);
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        await CreatePackagingLineRepository().SetLinesAsync(
            productId, [(wrapperId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

        var packLines = await PackLinesAsync(productId);
        packLines.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
    }

    [Fact]
    public async Task Deleting_a_product_takes_its_packs_and_their_lines_with_it()
    {
        // The pack FK is Restrict, so DeleteAsync has to clear the lines and packs itself before the product.
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        await CreateProductRepository().DeleteAsync(productId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.ProductPacks.AnyAsync(p => p.ProductId == productId, Cancellation)).Should().BeFalse();
        (await dbContext.PackagingLines.AnyAsync(l => l.ProductId == productId, Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_a_packaging_material_used_only_by_a_packs_line_is_blocked()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        var act = () => new RawMaterialRepository(CreateDbContextFactory(), CreateCostPropagator()).DeleteAsync(boxId, Cancellation);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SaveAsync_saves_the_packs_alongside_the_rest_and_leaves_them_alone_when_sent_null()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        var product = new Product { Id = productId, Name = "Ensaymada" };

        await CreateProductRepository().SaveAsync(
            product, null, null, null, [Pack("Pack of 12", 12, (boxId, 1m))], null, Cancellation);
        (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle();

        await CreateProductRepository().SaveAsync(product, null, null, null, null, null, Cancellation);

        (await CreatePackRepository().GetForProductAsync(productId, Cancellation))
            .Should().ContainSingle("null packs means leave them as they are");
    }

    [Fact]
    public async Task ProductCloner_CloneAsync_gives_the_clone_its_own_copy_of_the_packs()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);
        var boxId = await SeedPackagingAsync("Pack Box", 12m);
        await CreatePackRepository().SetPacksAsync(productId, [Pack("Pack of 12", 12, (boxId, 1m))], Cancellation);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Ube Ensaymada", Cancellation);

        var clonePacks = await CreatePackRepository().GetForProductAsync(cloneId, Cancellation);
        var clonePack = clonePacks.Should().ContainSingle().Subject;
        clonePack.Name.Should().Be("Pack of 12");
        clonePack.PiecesPerPack.Should().Be(12);
        clonePack.Lines.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);

        var sourcePackId = (await CreatePackRepository().GetForProductAsync(productId, Cancellation)).Single().Id;
        clonePack.Id.Should().NotBe(sourcePackId, "the clone's packs are its own rows");
    }
}
