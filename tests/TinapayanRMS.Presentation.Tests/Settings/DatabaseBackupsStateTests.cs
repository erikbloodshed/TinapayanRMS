using System.Reactive;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>
/// Settings' Database tab against real backups and restores, of a database of each test's own (ThrowawayDatabase), so
/// a restore never touches the database the other UI tests share.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public sealed class DatabaseBackupsStateTests(DatabaseFixture fixture) : IAsyncDisposable
{
    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));

    private readonly List<string> _restarts = [];
    private ThrowawayDatabase _database = null!;
    private DatabaseBackups _backups = null!;

    [Before(Test)]
    public async Task CreateDatabaseAsync()
    {
        _database = await ThrowawayDatabase.CreateAsync(fixture.ConnectionString, Cancellation);
        _backups = new DatabaseBackups(_database.CreateDbContextFactory(), _folder);
    }

    public async ValueTask DisposeAsync()
    {
        _backups.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Test]
    public Task Back_Up_Now_lists_the_new_backup_picked_and_says_so() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Backups.Should().BeEmpty();

        await state.BackUpNowCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var row = state.Backups.Should().ContainSingle().Subject;
        row.Kind.Should().Be("Manual");
        row.SizeMegabytes.Should().BeGreaterThan(0);
        state.SelectedBackup.Should().BeSameAs(row);
        state.SuccessMessage.Should().Be($"Backed up to {Path.GetFileName(row.Backup.Path)}.");
        state.BusyText.Should().BeNull();
    });

    [Test]
    public Task Open_Folder_shows_the_backups_folder_made_before_any_backup_has() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? shown = null;
        var existed = false;
        state.ShowFolder.RegisterHandler(interaction =>
        {
            shown = interaction.Input;
            existed = Directory.Exists(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });

        await state.OpenFolderCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        shown.Should().Be(_folder);
        existed.Should().BeTrue("File Explorer opens somewhere else on a folder that isn't there");
    });

    [Test]
    public Task Copy_to_puts_the_picked_backup_where_the_user_chose() =>UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.BackUpNowCommand.ExecuteAsync();
        var destination = Path.Combine(Path.GetTempPath(), $"stick {Guid.NewGuid():N}.backup");
        string? suggested = null;
        state.ChooseCopyDestination.RegisterHandler(interaction =>
        {
            suggested = interaction.Input;
            interaction.SetOutput(destination);
        });
        try
        {
            await state.CopyToCommand.ExecuteAsync();

            suggested.Should().Be(Path.GetFileName(state.SelectedBackup!.Backup.Path));
            new FileInfo(destination).Length.Should().Be(state.SelectedBackup.Backup.SizeBytes);
            state.SuccessMessage.Should().Be($"Copied the backup to {destination}.");
        }
        finally
        {
            File.Delete(destination);
        }
    });

    [Test]
    public Task A_restore_the_user_declines_changes_nothing() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await AddCategoryAsync("Breads");
        await state.BackUpNowCommand.ExecuteAsync();
        await AddCategoryAsync("Cakes");
        string? asked = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(false);
        });

        await state.RestoreCommand.ExecuteAsync();

        asked.Should().StartWith("Restore the backup taken ").And.Contain("A safety backup of the data as it is now is taken first.");
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
        _restarts.Should().BeEmpty();
        _backups.List().Should().ContainSingle("no safety backup is taken for a restore that doesn't happen");
    });

    [Test]
    public Task A_restore_puts_the_backup_back_and_restarts_the_app() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await AddCategoryAsync("Breads");
        await state.BackUpNowCommand.ExecuteAsync();
        await AddCategoryAsync("Cakes");
        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));

        await state.RestoreCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await CategoryNamesAsync()).Should().Equal("Breads");
        _restarts.Should().ContainSingle().Which.Should().StartWith("Restored the backup taken ").And.EndWith("TinapayanRMS restarts now.");
    });

    [Test]
    public Task A_file_that_isnt_a_backup_is_refused_and_its_copy_removed() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        var notABackup = Path.Combine(Path.GetTempPath(), $"prices {Guid.NewGuid():N}.backup");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        state.ChooseBackupFile.RegisterHandler(interaction => interaction.SetOutput(notABackup));
        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        try
        {
            await state.RestoreFromFileCommand.ExecuteAsync();

            state.ErrorMessage.Should().Be(BackupRules.NotADatabaseBackupText);
            _backups.List().Should().BeEmpty("the copy brought in to restore goes again");
            _restarts.Should().BeEmpty();
        }
        finally
        {
            File.Delete(notABackup);
        }
    });

    [Test]
    public Task Choosing_no_file_does_nothing() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        state.ChooseBackupFile.RegisterHandler(interaction => interaction.SetOutput(null));

        await state.RestoreFromFileCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        _backups.List().Should().BeEmpty();
    });

    private DatabaseBackupsState CreateState()
    {
        return new DatabaseBackupsState(_backups, _restarts.Add, new LoggerConfiguration().CreateLogger());
    }

    private async Task AddCategoryAsync(string name)
    {
        await using var dbContext = _database.CreateDbContext();
        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = name, Type = RawMaterialCategoryType.Ingredient });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<List<string>> CategoryNamesAsync()
    {
        await using var dbContext = _database.CreateDbContext();
        return await dbContext.RawMaterialCategories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(Cancellation);
    }
}
