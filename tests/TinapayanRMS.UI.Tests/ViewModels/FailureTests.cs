using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// What a screen's error notice says for each kind of failure, from real database errors (2026-10-03). Until then every
/// DbUpdateException read "That name is already in use." and every SqlException "This item is currently in use elsewhere
/// and can't be deleted.", whatever had happened, and a database that couldn't be reached among them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class FailureTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public void A_rule_is_shown_in_its_own_words()
    {
        Failure.Of(new BusinessRuleException("Pick a unit for every alternative unit, or remove the empty row."))
            .Should().Be(new Failure("Pick a unit for every alternative unit, or remove the empty row.", IsRejection: true));
    }

    [Fact]
    public async Task A_duplicate_name_says_the_name_is_in_use()
    {
        await AddCategoryAsync("Breads");

        var failure = await FailureOfAsync(() => AddCategoryAsync("Breads"));

        failure.Should().Be(new Failure(Failure.DuplicateNameText, IsRejection: true));
    }

    [Fact]
    public async Task A_duplicate_abbreviation_says_the_abbreviation_is_in_use()
    {
        await SeedUnitOfMeasureAsync("kilogram", "kg");

        var failure = await FailureOfAsync(() => SeedUnitOfMeasureAsync("kilo", "kg"));

        failure.Text.Should().Be("That abbreviation is already in use.");
    }

    [Fact]
    public async Task Deleting_what_something_still_uses_says_it_is_in_use()
    {
        var flourId = await AddIngredientAsync("Flour");
        int categoryId;
        await using (var dbContext = CreateDbContext())
        {
            categoryId = await dbContext.RawMaterials.Where(m => m.Id == flourId).Select(m => m.CategoryId).SingleAsync(Cancellation);
        }

        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            await dbContext.RawMaterialCategories.Where(c => c.Id == categoryId).ExecuteDeleteAsync(Cancellation);
        });

        failure.Should().Be(new Failure(Failure.InUseText, IsRejection: true));
    }

    [Fact]
    public async Task Saving_what_refers_to_something_gone_says_so()
    {
        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            dbContext.RawMaterials.Add(new RawMaterial { Name = "Flour", CategoryId = 999_999 });
            await dbContext.SaveChangesAsync(Cancellation);
        });

        failure.Text.Should().Be("Something this refers to no longer exists. It may have been deleted on another screen.");
        failure.IsRejection.Should().BeTrue();
    }

    [Fact]
    public async Task A_value_a_check_constraint_refuses_says_so()
    {
        var flourId = await AddIngredientAsync("Flour");

        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            dbContext.RawMaterialBrandPrices.Add(new RawMaterialBrandPrice { RawMaterialId = flourId, Price = -1m });
            await dbContext.SaveChangesAsync(Cancellation);
        });

        failure.Text.Should().Be("One of the values isn't allowed, so the change wasn't saved.");
    }

    [Fact]
    public async Task A_number_too_large_for_its_column_says_so_rather_than_that_the_name_is_taken()
    {
        var flourId = await AddIngredientAsync("Flour");

        // A 16-digit price: more than decimal(18,4) holds.
        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            dbContext.RawMaterialBrandPrices.Add(new RawMaterialBrandPrice { RawMaterialId = flourId, Price = 1_000_000_000_000_000m });
            await dbContext.SaveChangesAsync(Cancellation);
        });

        failure.Text.Should().Be("A number is too large to save.");
    }

    [Fact]
    public async Task A_database_that_cant_be_reached_says_so()
    {
        var failure = await FailureOfAsync(async () =>
        {
            await using var connection = new SqlConnection(
                @"Server=.\NO_SUCH_INSTANCE;Database=TinapayanRMS;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=2");
            await connection.OpenAsync(Cancellation);
        });

        failure.Should().Be(new Failure(Failure.UnreachableText, IsRejection: false));
    }

    [Fact]
    public void A_timeout_says_the_database_didnt_answer()
    {
        Failure.Of(new TimeoutException("Execution Timeout Expired.")).Should().Be(new Failure(Failure.TimedOutText, IsRejection: false));
    }

    [Fact]
    public void Anything_else_is_a_bug_that_still_says_what_went_wrong()
    {
        var failure = Failure.Of(new InvalidOperationException("Sequence contains no elements" + Environment.NewLine + "   at Somewhere()"));

        failure.Text.Should().Be("Something went wrong, and the details went to the log: Sequence contains no elements");
        failure.IsRejection.Should().BeFalse("it's logged as an error, with its stack");
    }

    private async Task AddCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name, Type = RawMaterialType.Ingredient });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private static async Task<Failure> FailureOfAsync(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (Exception ex)
        {
            return Failure.Of(ex);
        }

        throw new InvalidOperationException("It didn't fail.");
    }
}
