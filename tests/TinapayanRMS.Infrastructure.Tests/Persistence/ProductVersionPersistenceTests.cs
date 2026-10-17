using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A product's version history (ProductVersion): numbered per product, recorded only when the recipe or price differs
/// from the latest, with a note that can be changed, and gone with the product when it's deleted for good.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductVersionPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductVersionRepository CreateRepository() => new(CreateDbContextFactory());

    private static ProductVersionContent Recipe(decimal flourGrams, decimal? currentPrice = 5m) => new()
    {
        ProductName = "Pandesal",
        CurrentPrice = currentPrice,
        Sections =
        [
            new VersionSection(
                1,
                "Pandesal Dough",
                false,
                new VersionRole(1, "Dough", 1, false, false),
                UsageAuthoredField.WeightPerPiece,
                60m,
                0m,
                [new VersionLine(1, flourGrams, true, null)],
                []),
        ],
        Materials = [new VersionMaterial(1, "Bread Flour", RawMaterialType.Ingredient, null, 0.05m, 0.055m, [])],
    };

    [Fact]
    public async Task Versions_are_numbered_per_product_and_read_newest_first()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();

        await repository.AddAsync(pandesalId, Recipe(500m), new DateTime(2026, 10, 1), null, Cancellation);
        await repository.AddAsync(ensaymadaId, Recipe(500m), new DateTime(2026, 10, 1), null, Cancellation);
        await repository.AddAsync(pandesalId, Recipe(450m), new DateTime(2026, 10, 2), "Less flour", Cancellation);

        var versions = await repository.GetForProductAsync(pandesalId, Cancellation);
        versions.Select(v => v.Number).Should().Equal(2, 1);
        versions[0].Note.Should().Be("Less flour");
        versions[0].SavedAt.Should().Be(new DateTime(2026, 10, 2));
        ProductVersionContent.FromJson(versions[0].Content).Sections.Single().Lines.Single().Grams.Should().Be(450m);
        (await repository.GetForProductAsync(ensaymadaId, Cancellation)).Single().Number.Should().Be(1);
        (await repository.HasAnyAsync(pandesalId, Cancellation)).Should().BeTrue();
    }

    [Fact]
    public async Task The_same_recipe_and_price_as_the_latest_records_nothing()
    {
        var productId = await AddProductAsync("Pandesal");
        var repository = CreateRepository();
        await repository.AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation);

        var same = await repository.AddAsync(productId, Recipe(500m) with { ProductName = "Pandesal Classic" }, DateTime.Now, null, Cancellation);
        var repriced = await repository.AddAsync(productId, Recipe(500m, currentPrice: 6m), DateTime.Now, null, Cancellation);

        same.Should().BeNull("a rename alone isn't a change of recipe or price");
        repriced!.Number.Should().Be(2);
    }

    [Fact]
    public async Task A_product_with_no_versions_has_none()
    {
        var productId = await AddProductAsync("Pandesal");

        (await CreateRepository().HasAnyAsync(productId, Cancellation)).Should().BeFalse();
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_note_is_changed_trimmed_and_taken_away()
    {
        var productId = await AddProductAsync("Pandesal");
        var version = (await CreateRepository().AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation))!;

        await CreateRepository().SetNoteAsync(version.Id, "  Cheaper butter  ", Cancellation);
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Single().Note.Should().Be("Cheaper butter");

        await CreateRepository().SetNoteAsync(version.Id, " ", Cancellation);
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Single().Note.Should().BeNull();
    }

    [Fact]
    public async Task A_note_too_long_is_refused()
    {
        var productId = await AddProductAsync("Pandesal");
        var version = (await CreateRepository().AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation))!;

        var act = () => CreateRepository().SetNoteAsync(version.Id, new string('a', ProductVersionRules.MaxNoteLength + 1), Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_version_of_a_product_that_doesnt_exist_is_refused()
    {
        var act = () => CreateRepository().AddAsync(9999, Recipe(500m), DateTime.Now, null, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_product_in_the_Deleted_folder_keeps_its_history_and_one_deleted_for_good_takes_it()
    {
        var productId = await AddProductAsync("Pandesal");
        await CreateRepository().AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation);
        var products = new ProductRepository(CreateDbContextFactory());

        await products.DeleteAsync(productId, Cancellation);
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle();

        await products.DeletePermanentlyAsync(productId, Cancellation);
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductVersions.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Two_versions_of_a_product_cant_share_a_number()
    {
        var productId = await AddProductAsync("Pandesal");
        await using var dbContext = CreateDbContext();
        dbContext.ProductVersions.Add(new ProductVersion { ProductId = productId, Number = 1, SavedAt = DateTime.Now, Content = "{}" });
        dbContext.ProductVersions.Add(new ProductVersion { ProductId = productId, Number = 1, SavedAt = DateTime.Now, Content = "{}" });

        var act = () => dbContext.SaveChangesAsync(Cancellation);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<SqlException>();
    }
}
