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
/// A deleted product (Product.DeletedAt): Delete moves it to the Products list's Deleted folder whole, Restore brings it
/// back, and Delete Permanently deletes it for good. While it's deleted it still holds what it uses, and is named as
/// deleted wherever it's in the way.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class DeletedProductPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private Task LinkAsync(int productId, int subAssemblyId) =>
        SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(subAssemblyId, UsageAuthoredField.WeightPerPiece, 30m)]));

    [Test]
    public async Task Delete_moves_a_product_to_the_Deleted_folder_whole_and_Restore_brings_it_back()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);
        await AddProductAsync("Pandesal");
        await new CostingParametersRepository(CreateDbContextFactory())
            .SetAsync(productId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, Cancellation);
        var repository = CreateRepository();
        var updatedAt = (await repository.GetByIdAsync(productId, Cancellation))!.UpdatedAt;

        await repository.DeleteAsync(productId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Select(p => p.Name).Should().Equal("Pandesal");
        (await repository.GetDeletedAsync(Cancellation)).Should().ContainSingle()
            .Which.Should().Match<Product>(p => p.Name == "Ensaymada" && p.IsDeleted);
        var deleted = (await repository.GetByIdAsync(productId, Cancellation))!;
        deleted.DeletedAt.Should().NotBeNull("it's still read by its id");
        (await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(productId, Cancellation))
            .Should().ContainSingle(u => u.SubAssemblyId == doughId, "nothing of it goes");
        (await new CostingParametersRepository(CreateDbContextFactory()).GetForProductAsync(productId, Cancellation)).Should().NotBeNull();

        await repository.RestoreAsync(productId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Select(p => p.Name).Should().Equal("Ensaymada", "Pandesal");
        (await repository.GetDeletedAsync(Cancellation)).Should().BeEmpty();
        var restored = (await repository.GetByIdAsync(productId, Cancellation))!;
        restored.DeletedAt.Should().BeNull();
        restored.UpdatedAt.Should().Be(updatedAt, "a delete and a restore aren't edits");
    }

    [Test]
    public async Task A_deleted_product_comes_back_uncategorized_when_its_category_has_gone()
    {
        var cakes = new ProductCategory { Name = "Cakes" };
        await new ProductCategoryRepository(CreateDbContextFactory()).AddAsync(cakes, Cancellation);
        var productId = await AddProductAsync("Mamon");
        await CreateRepository().MoveAsync([new ProductMove(productId, ProductStage.Production, cakes.Id)], Cancellation);
        await CreateRepository().DeleteAsync(productId, Cancellation);
        await new ProductCategoryRepository(CreateDbContextFactory()).DeleteAsync(cakes.Id, Cancellation);

        await CreateRepository().RestoreAsync(productId, Cancellation);

        var restored = (await CreateRepository().GetByIdAsync(productId, Cancellation))!;
        restored.Should().Match<Product>(p => p.Stage == ProductStage.Production && p.CategoryId == null && !p.IsDeleted);
    }

    [Test]
    public async Task Delete_Permanently_deletes_a_deleted_product_for_good()
    {
        var productId = await AddProductAsync("Ensaymada");
        await CreateRepository().DeleteAsync(productId, Cancellation);

        await CreateRepository().DeletePermanentlyAsync(productId, Cancellation);

        (await CreateRepository().GetByIdAsync(productId, Cancellation)).Should().BeNull();
        (await CreateRepository().GetDeletedAsync(Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_deleted_products_name_stays_taken_and_saying_so_points_to_the_Deleted_folder()
    {
        var mamonId = await AddProductAsync("Mamon");
        var pandesalId = await AddProductAsync("Pandesal");
        await CreateRepository().DeleteAsync(mamonId, Cancellation);

        var add = () => CreateRepository().AddAsync(new Product { Name = "Mamon" }, Cancellation);
        var rename = () => CreateRepository().RenameAsync(pandesalId, "Mamon", Cancellation);
        var save = () => CreateRepository().SaveAsync(
            new Product { Id = pandesalId, Name = "Mamon" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        const string message = "A deleted product is named Mamon. Restore it from the Deleted folder, or delete it permanently there first.";
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await rename.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public async Task A_deleted_product_still_holds_the_sub_assembly_it_links_named_as_deleted()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var glazeId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var creamLoafId = await AddProductAsync("Cream Loaf");
        await LinkAsync(creamLoafId, glazeId);

        await CreateRepository().DeleteAsync(creamLoafId, Cancellation);

        (await subAssemblies.GetStandaloneLinkedByAsync(Cancellation))[glazeId]
            .Should().ContainSingle().Which.Should().Be(new LinkingProduct(creamLoafId, "Cream Loaf (deleted)", null));
        var deleteGlaze = () => subAssemblies.DeleteStandaloneAsync(glazeId, Cancellation);
        await deleteGlaze.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cream Loaf (deleted) links Glaze.*");

        await CreateRepository().DeletePermanentlyAsync(creamLoafId, Cancellation);
        await subAssemblies.DeleteStandaloneAsync(glazeId, Cancellation);
    }

    [Test]
    public async Task A_raw_materials_Used_By_names_a_deleted_product_as_deleted()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)]);

        await CreateRepository().DeleteAsync(productId, Cancellation);

        (await new RawMaterialRepository(CreateDbContextFactory()).GetConsumersAsync(flourId, Cancellation))
            .Should().Equal(new LinkingProduct(productId, "Ensaymada (deleted)", null));
    }

    [Test]
    public async Task A_product_whose_packaging_another_links_cant_be_deleted_even_by_a_deleted_one()
    {
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await new PackagingLinkRepository(CreateDbContextFactory()).SetLinkAsync(smallId, bigId, Cancellation);
        await CreateRepository().DeleteAsync(smallId, Cancellation);

        var deleteBig = () => CreateRepository().DeleteAsync(bigId, Cancellation);

        await deleteBig.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Small (deleted) links this product's packaging. Unlink it there first.");
    }

    [Test]
    public async Task A_deleted_product_cant_be_linked_or_put_in_a_composite()
    {
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        await CreateRepository().DeleteAsync(bigId, Cancellation);

        var link = () => new PackagingLinkRepository(CreateDbContextFactory()).SetLinkAsync(smallId, bigId, Cancellation);
        var hold = () => CreateRepository().SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (bigId, 4)] },
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);

        await link.Should().ThrowAsync<InvalidOperationException>().WithMessage("Big is deleted. Restore it from the Deleted folder to link its packaging.");
        await hold.Should().ThrowAsync<InvalidOperationException>().WithMessage("Big is deleted. Restore it from the Deleted folder to put it in Pasalubong Box.");
    }

    [Test]
    public async Task Deleting_a_missing_product_or_restoring_one_is_refused()
    {
        var delete = () => CreateRepository().DeleteAsync(999, Cancellation);
        var restore = () => CreateRepository().RestoreAsync(999, Cancellation);

        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("Product 999 does not exist.");
        await restore.Should().ThrowAsync<InvalidOperationException>().WithMessage("Product 999 does not exist.");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.CountAsync(Cancellation)).Should().Be(0);
    }
}
