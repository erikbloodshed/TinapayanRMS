using System.IO;
using AwesomeAssertions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Infrastructure.Tests.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// What a screen's error notice says for each kind of failure, from real database errors (2026-10-03). Until then every
/// DbUpdateException read "That name is already in use." and every driver exception "This item is currently in use elsewhere
/// and can't be deleted.", whatever had happened, and a database that couldn't be reached among them.
/// </summary>
[Collection(DatabaseCollection.C)]
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
            // Nothing listens on port 1, so the connection is refused. Windows tries a refused connection again for about two
            // seconds before it gives up, so the timeout leaves it longer than that.
            await using var connection = new NpgsqlConnection("Host=localhost;Port=1;Database=tinapayanrms;Username=tinapayanrms;Timeout=10");
            await connection.OpenAsync(Cancellation);
        });

        failure.Should().Be(new Failure(Failure.UnreachableText, IsRejection: false));
    }

    [Fact]
    public async Task A_database_that_cant_be_reached_by_a_query_says_so()
    {
        // Through EF Core, whose execution strategy wrapped a transient failure in an InvalidOperationException until
        // Npgsql 11, which read as a bug.
        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = new TinapayanRMSDbContext(TinapayanRMSDbContext.CreateOptions(
                "Host=localhost;Port=1;Database=tinapayanrms;Username=tinapayanrms;Timeout=10"));
            await dbContext.ProductCategories.CountAsync(Cancellation);
        });

        failure.Should().Be(new Failure(Failure.UnreachableText, IsRejection: false));
    }

    [Fact]
    public async Task A_save_that_clashes_with_another_says_the_database_was_busy()
    {
        await AddCategoryAsync("Breads");

        // A serializable transaction reads the category, another change renames it and commits, and then the first renames
        // it too: PostgreSQL refuses that update (40001), which Npgsql 11 throws as a DbUpdateConcurrencyException.
        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, Cancellation);
            var category = await dbContext.RawMaterialCategories.SingleAsync(c => c.Name == "Breads", Cancellation);

            await using (var other = CreateDbContext())
            {
                await other.RawMaterialCategories.ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "Rolls"), Cancellation);
            }

            category.Name = "Pastries";
            await dbContext.SaveChangesAsync(Cancellation);
        });

        failure.Should().Be(new Failure("The database was busy with another change. Try again.", IsRejection: false));
    }

    [Fact]
    public void A_deadlock_in_a_save_says_the_database_was_busy()
    {
        var deadlock = new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);

        Failure.Of(new DbUpdateConcurrencyException("An error occurred while saving the entity changes.", deadlock))
            .Should().Be(new Failure("The database was busy with another change. Try again.", IsRejection: false));
    }

    [Fact]
    public async Task Saving_what_another_screen_deleted_says_to_open_it_again()
    {
        await AddCategoryAsync("Breads");

        var failure = await FailureOfAsync(async () =>
        {
            await using var dbContext = CreateDbContext();
            var category = await dbContext.RawMaterialCategories.SingleAsync(c => c.Name == "Breads", Cancellation);

            await using (var other = CreateDbContext())
            {
                await other.RawMaterialCategories.ExecuteDeleteAsync(Cancellation);
            }

            category.Name = "Rolls";
            await dbContext.SaveChangesAsync(Cancellation);
        });

        failure.Should().Be(new Failure(
            "It was changed or deleted on another screen in the meantime. Open it again, then make the change.", IsRejection: true));
    }

    [Fact]
    public void A_timeout_says_the_database_didnt_answer()
    {
        Failure.Of(new TimeoutException("Execution Timeout Expired.")).Should().Be(new Failure(Failure.TimedOutText, IsRejection: false));
        Failure.Of(new NpgsqlException("Exception while reading from stream", new TimeoutException("Timeout during reading attempt")))
            .Should().Be(new Failure(Failure.TimedOutText, IsRejection: false), "Npgsql wraps a command's timeout in its own exception");
    }

    [Fact]
    public async Task A_file_open_in_another_program_says_to_close_it_there()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TinapayanRMS failure {Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "open in Excel", Cancellation);
        try
        {
            Failure failure;
            await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                failure = await FailureOfAsync(() => File.WriteAllBytesAsync(path, [1, 2, 3], Cancellation));
            }

            failure.Should().Be(new Failure(Failure.FileInUseText, IsRejection: true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_file_or_folder_that_isnt_there_says_so_and_not_to_close_it_elsewhere()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), $"TinapayanRMS missing {Guid.NewGuid():N}.backup");
        var inMissingFolder = Path.Combine(Path.GetTempPath(), $"TinapayanRMS missing {Guid.NewGuid():N}", "report.xlsx");

        var file = await FailureOfAsync(() => File.ReadAllBytesAsync(missingFile, Cancellation));
        var folder = await FailureOfAsync(() => File.WriteAllBytesAsync(inMissingFolder, [1, 2, 3], Cancellation));

        file.Should().Be(new Failure(Failure.FileMissingText, IsRejection: true));
        folder.Should().Be(new Failure(Failure.FileMissingText, IsRejection: true));
    }

    [Fact]
    public void Any_other_trouble_with_a_file_says_what_windows_said_and_not_to_close_it_elsewhere()
    {
        var failure = Failure.Of(new IOException("There is not enough space on the disk." + Environment.NewLine + "   at Somewhere()"));

        failure.Should().Be(new Failure("Couldn't read or write the file: There is not enough space on the disk.", IsRejection: true));
    }

    [Fact]
    public void A_backup_tool_that_stopped_says_why_in_its_own_words()
    {
        var failure = Failure.Of(new PostgresToolException(
            "pg_dump", "pg_dump: error: connection to server failed: fe_sendauth: no password supplied" + Environment.NewLine + "pg_dump: more"));

        failure.Should().Be(new Failure(
            "PostgreSQL's pg_dump reported a problem: pg_dump: error: connection to server failed: fe_sendauth: no password supplied", IsRejection: false));
    }

    [Fact]
    public void A_place_windows_wont_let_the_app_use_says_to_pick_another()
    {
        Failure.Of(new UnauthorizedAccessException(@"Access to the path 'C:\Windows\report.xlsx' is denied."))
            .Should().Be(new Failure(Failure.NoAccessText, IsRejection: true));
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
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name, Type = RawMaterialCategoryType.Ingredient });
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
