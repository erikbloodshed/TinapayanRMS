using System.IO;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// Settings' Database tab against real backups and restores, of a database of each test's own (ThrowawayDatabase), so
/// a restore never touches the database the other UI tests share.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseBackupsViewModelTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));

    private readonly List<string> _restarts = [];
    private ThrowawayDatabase _database = null!;
    private DatabaseBackups _backups = null!;

    public async ValueTask InitializeAsync()
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

    [Fact]
    public async Task Back_Up_Now_lists_the_new_backup_picked_and_says_so()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.Backups.Should().BeEmpty();

        await viewModel.BackUpNowCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var row = viewModel.Backups.Should().ContainSingle().Subject;
        row.Kind.Should().Be("Manual");
        row.SizeMegabytes.Should().BeGreaterThan(0);
        viewModel.SelectedBackup.Should().BeSameAs(row);
        viewModel.SuccessMessage.Should().Be($"Backed up to {Path.GetFileName(row.Backup.Path)}.");
        viewModel.BusyText.Should().BeNull();
    }

    [Fact]
    public async Task Copy_to_puts_the_picked_backup_where_the_user_chose()
    {
        var viewModel = CreateViewModel();
        await viewModel.BackUpNowCommand.Execute();
        var destination = Path.Combine(Path.GetTempPath(), $"stick {Guid.NewGuid():N}.backup");
        string? suggested = null;
        viewModel.ChooseCopyDestination.RegisterHandler(interaction =>
        {
            suggested = interaction.Input;
            interaction.SetOutput(destination);
        });
        try
        {
            await viewModel.CopyToCommand.Execute();

            suggested.Should().Be(Path.GetFileName(viewModel.SelectedBackup!.Backup.Path));
            new FileInfo(destination).Length.Should().Be(viewModel.SelectedBackup.Backup.SizeBytes);
            viewModel.SuccessMessage.Should().Be($"Copied the backup to {destination}.");
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [Fact]
    public async Task A_restore_the_user_declines_changes_nothing()
    {
        var viewModel = CreateViewModel();
        await AddCategoryAsync("Breads");
        await viewModel.BackUpNowCommand.Execute();
        await AddCategoryAsync("Cakes");
        string? asked = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(false);
        });

        await viewModel.RestoreCommand.Execute();

        asked.Should().StartWith("Restore the backup taken ").And.Contain("A safety backup of the data as it is now is taken first.");
        (await CategoryNamesAsync()).Should().Equal("Breads", "Cakes");
        _restarts.Should().BeEmpty();
        _backups.List().Should().ContainSingle("no safety backup is taken for a restore that doesn't happen");
    }

    [Fact]
    public async Task A_restore_puts_the_backup_back_and_restarts_the_app()
    {
        var viewModel = CreateViewModel();
        await AddCategoryAsync("Breads");
        await viewModel.BackUpNowCommand.Execute();
        await AddCategoryAsync("Cakes");
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));

        await viewModel.RestoreCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryNamesAsync()).Should().Equal("Breads");
        _restarts.Should().ContainSingle().Which.Should().StartWith("Restored the backup taken ").And.EndWith("TinapayanRMS restarts now.");
    }

    [Fact]
    public async Task A_file_that_isnt_a_backup_is_refused_and_its_copy_removed()
    {
        var viewModel = CreateViewModel();
        var notABackup = Path.Combine(Path.GetTempPath(), $"prices {Guid.NewGuid():N}.backup");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        viewModel.ChooseBackupFile.RegisterHandler(interaction => interaction.SetOutput(notABackup));
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        try
        {
            await viewModel.RestoreFromFileCommand.Execute();

            viewModel.ErrorMessage.Should().Be(BackupRules.NotADatabaseBackupText);
            _backups.List().Should().BeEmpty("the copy brought in to restore goes again");
            _restarts.Should().BeEmpty();
        }
        finally
        {
            File.Delete(notABackup);
        }
    }

    [Fact]
    public async Task Choosing_no_file_does_nothing()
    {
        var viewModel = CreateViewModel();
        viewModel.ChooseBackupFile.RegisterHandler(interaction => interaction.SetOutput(null));

        await viewModel.RestoreFromFileCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        _backups.List().Should().BeEmpty();
    }

    private DatabaseBackupsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        return new DatabaseBackupsViewModel(_backups, _restarts.Add, new LoggerConfiguration().CreateLogger());
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
