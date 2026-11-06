using AwesomeAssertions;
using Npgsql;
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
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
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
                [new VersionLine(1, flourGrams, true, null)],
                []),
        ],
        Materials = [new VersionMaterial(1, "Bread Flour", RawMaterialType.Ingredient, null, 0.05m, 0.055m, [])],
    };

    [Test]
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

    [Test]
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

    [Test]
    public async Task Many_products_are_recorded_at_once_each_after_its_own_latest_with_a_baseline_for_one_with_none()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var unchangedId = await AddProductAsync("Spanish Bread");
        var goneId = await AddProductAsync("Cheese Roll");
        var repository = CreateRepository();
        await repository.AddAsync(pandesalId, Recipe(500m), new DateTime(2026, 10, 1), null, Cancellation);
        await repository.AddAsync(unchangedId, Recipe(400m), new DateTime(2026, 10, 1), null, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Products.Where(p => p.Id == goneId).ExecuteDeleteAsync(Cancellation);
        }

        var savedAt = new DateTime(2026, 10, 2);
        ProductVersionDraft Draft(int productId, decimal flourGrams) => new(productId, Recipe(flourGrams), savedAt, "Dough changed")
        {
            Baseline = new(productId, Recipe(flourGrams + 50m), new DateTime(2026, 9, 1), ProductVersionRules.BaselineNote),
        };

        var recorded = await repository.AddRangeAsync(
            [Draft(pandesalId, 450m), Draft(ensaymadaId, 450m), Draft(unchangedId, 400m), Draft(goneId, 450m)], Cancellation);

        recorded.Select(v => v?.Number).Should().Equal(2, 2, null, null);
        var pandesal = await repository.GetForProductAsync(pandesalId, Cancellation);
        pandesal.Select(v => v.Note).Should().Equal(new[] { "Dough changed", null }, "Pandesal has versions, so its baseline isn't wanted");
        var ensaymada = await repository.GetForProductAsync(ensaymadaId, Cancellation);
        ensaymada.Select(v => (v.Number, v.Note, v.SavedAt)).Should().Equal(
            (2, "Dough changed", savedAt), (1, ProductVersionRules.BaselineNote, new DateTime(2026, 9, 1)));
        (await repository.GetForProductAsync(unchangedId, Cancellation)).Should().ContainSingle("its recipe is the latest's");
    }

    [Test]
    public async Task A_note_too_long_refuses_every_draft_with_it()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();

        var add = () => repository.AddRangeAsync(
            [
                new ProductVersionDraft(pandesalId, Recipe(500m), DateTime.Now, null),
                new ProductVersionDraft(ensaymadaId, Recipe(500m), DateTime.Now, new string('x', ProductVersionRules.MaxNoteLength + 1)),
            ],
            Cancellation);

        await add.Should().ThrowAsync<BusinessRuleException>();
        (await repository.HasAnyAsync(pandesalId, Cancellation)).Should().BeFalse("nothing is written unless every draft is");
    }

    [Test]
    public async Task A_product_with_no_versions_has_none()
    {
        var productId = await AddProductAsync("Pandesal");

        (await CreateRepository().HasAnyAsync(productId, Cancellation)).Should().BeFalse();
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_note_is_changed_trimmed_and_taken_away()
    {
        var productId = await AddProductAsync("Pandesal");
        var version = (await CreateRepository().AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation))!;

        await CreateRepository().SetNoteAsync(version.Id, "  Cheaper butter  ", Cancellation);
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Single().Note.Should().Be("Cheaper butter");

        await CreateRepository().SetNoteAsync(version.Id, " ", Cancellation);
        (await CreateRepository().GetForProductAsync(productId, Cancellation)).Single().Note.Should().BeNull();
    }

    [Test]
    public async Task A_note_too_long_is_refused()
    {
        var productId = await AddProductAsync("Pandesal");
        var version = (await CreateRepository().AddAsync(productId, Recipe(500m), DateTime.Now, null, Cancellation))!;

        var act = () => CreateRepository().SetNoteAsync(version.Id, new string('a', ProductVersionRules.MaxNoteLength + 1), Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task A_version_of_a_product_that_doesnt_exist_is_refused()
    {
        var act = () => CreateRepository().AddAsync(9999, Recipe(500m), DateTime.Now, null, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
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

    [Test]
    public async Task Two_versions_of_a_product_cant_share_a_number()
    {
        var productId = await AddProductAsync("Pandesal");
        await using var dbContext = CreateDbContext();
        dbContext.ProductVersions.Add(new ProductVersion { ProductId = productId, Number = 1, SavedAt = DateTime.Now, Content = "{}" });
        dbContext.ProductVersions.Add(new ProductVersion { ProductId = productId, Number = 1, SavedAt = DateTime.Now, Content = "{}" });

        var act = () => dbContext.SaveChangesAsync(Cancellation);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>();
    }

    [Test]
    public async Task The_products_reading_a_sub_assembly_are_those_linking_it_and_those_whose_packaging_reads_theirs()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (ownerId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var linkerId = await AddProductAsync("Pandesal x 8");
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m)]));
        var otherId = await AddProductAsync("Ensaymada");
        var setRoleId = await AddRoleAsync("Packaging", sortOrder: 9, isPackaging: true);

        // Packaging that reads Pandesal x 8's, one that reads that one's, and a set only Box Pack links.
        var packagingReaderId = await AddProductAsync("Pandesal Gift Bag");
        var secondReaderId = await AddProductAsync("Pandesal Gift Box");
        var boxPackId = await AddProductAsync("Box Pack");
        var set = new SubAssembly { Name = "Box Set", RoleId = setRoleId };
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblies.Add(set);
            await dbContext.SaveChangesAsync(Cancellation);
            dbContext.PackagingLinks.AddRange(
                new PackagingLink { ProductId = packagingReaderId, LinkedProductId = linkerId },
                new PackagingLink { ProductId = secondReaderId, LinkedProductId = packagingReaderId },
                new PackagingLink { ProductId = boxPackId, LinkedSubAssemblyId = set.Id });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var products = new ProductRepository(CreateDbContextFactory());
        var setId = set.Id;

        (await products.GetProductsReadingAsync([doughId], [], Cancellation)).Should().BeEquivalentTo(
            [ownerId, linkerId, packagingReaderId, secondReaderId], "Ensaymada and Box Pack read nothing of Pandesal Dough");
        (await products.GetProductsReadingAsync([], [ownerId], Cancellation)).Should().BeEquivalentTo(
            [ownerId, linkerId, packagingReaderId, secondReaderId], "Pandesal's own section is shared with Pandesal x 8");
        (await products.GetProductsReadingAsync([], [packagingReaderId], Cancellation)).Should().BeEquivalentTo(
            [packagingReaderId, secondReaderId], "only packaging reads through Pandesal Gift Bag");
        (await products.GetProductsReadingAsync([setId], [], Cancellation)).Should().BeEquivalentTo([boxPackId]);
        (await products.GetProductsReadingAsync([], [otherId], Cancellation)).Should().Equal(otherId);
        (await products.GetProductsReadingAsync([], [], Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_loop_of_packaging_links_added_outside_the_app_ends_and_still_finds_the_products_reading_it()
    {
        var bagId = await AddProductAsync("Gift Bag");
        var boxId = await AddProductAsync("Gift Box");
        var setId = await AddProductAsync("Gift Set");

        // Gift Bag reads Gift Box, which reads Gift Bag, and Gift Set reads the bag from outside the loop. The app refuses
        // the loop (PackagingLinkRules.EnsureNoCycle), so it goes in past the repository.
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PackagingLinks.AddRange(
                new PackagingLink { ProductId = bagId, LinkedProductId = boxId },
                new PackagingLink { ProductId = boxId, LinkedProductId = bagId },
                new PackagingLink { ProductId = setId, LinkedProductId = bagId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        (await new ProductRepository(CreateDbContextFactory()).GetProductsReadingAsync([], [bagId], Cancellation))
            .Should().BeEquivalentTo([bagId, boxId, setId]);
    }
}
