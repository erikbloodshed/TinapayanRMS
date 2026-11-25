using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Components.Layout;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.App;

namespace TinapayanRMS.Components.Tests.Layout;

/// <summary>
/// The window's banners about the backups: the reminder to set Also Copy To while every backup is on this computer only,
/// which closes until the app starts again, and the warning while the newest backup wouldn't restore when it was proved.
/// </summary>
public class BackupBannerTests
{
    private static BunitContext NewContext()
    {
        var context = AppServices.NewContext();
        context.Services.AddSingleton<IAccessSettingsRepository>(new InMemoryAccessSettings());
        context.Services.AddSingleton<IDatabaseUpdater>(new StubDatabaseUpdater());
        return context.RenderingAsTheWebView();
    }

    [Test]
    public async Task With_no_second_folder_a_reminder_shows_and_closed_it_stays_closed_on_a_page_loaded_afresh()
    {
        await using var context = NewContext();
        context.Services.GetRequiredService<AppLock>().Unlock();

        var layout = context.Render<MainLayout>();
        layout.Find("#copy-folder-banner").TextContent.Should().Contain("on this computer only");

        await layout.Find("#copy-folder-banner .app-banner-close").ClickAsync(new MouseEventArgs());

        await layout.WaitForAssertionAsync(() => layout.FindAll("#copy-folder-banner").Should().BeEmpty());

        // The page loaded afresh, as after a restore: the old layout goes before the new one is drawn.
        await context.DisposeComponentsAsync();
        context.Render<MainLayout>().FindAll("#copy-folder-banner").Should().BeEmpty("it stays closed until the app starts again");
    }

    [Test]
    public async Task Setting_a_second_folder_takes_the_reminder_away_and_clearing_it_brings_it_back()
    {
        await using var context = NewContext();
        context.Services.GetRequiredService<AppLock>().Unlock();
        var backups = context.Services.GetRequiredService<AutomaticBackups>();
        var layout = context.Render<MainLayout>();

        backups.CopyFolder = @"E:\Backups";
        await layout.WaitForAssertionAsync(() => layout.FindAll("#copy-folder-banner").Should().BeEmpty());

        backups.CopyFolder = null;
        await layout.WaitForAssertionAsync(() => layout.FindAll("#copy-folder-banner").Should().ContainSingle());
    }

    [Test]
    public async Task Choose_a_Folder_opens_the_Database_tab()
    {
        await using var context = NewContext();
        context.Services.GetRequiredService<AppLock>().Unlock();
        var layout = context.Render<MainLayout>();

        await layout.Find("#copy-folder-banner button:not(.app-banner-close)").ClickAsync(new MouseEventArgs());

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/settings/database");
    }

    [Test]
    public async Task A_backup_that_wouldnt_restore_is_said_over_every_screen_until_one_does()
    {
        await using var context = NewContext();
        context.Services.GetRequiredService<AppLock>().Unlock();
        var status = context.Services.GetRequiredService<AppStatus>();
        var layout = context.Render<MainLayout>();
        layout.FindAll("#backup-proof-banner").Should().BeEmpty();

        status.WarnOfBackupProof("The backup of November 25 at 9:00 AM couldn't be restored when it was checked on November 25.");
        await layout.WaitForAssertionAsync(() => layout.Find("#backup-proof-banner").TextContent.Should().Contain("couldn't be restored"));

        status.WarnOfBackupProof(null);
        await layout.WaitForAssertionAsync(() => layout.FindAll("#backup-proof-banner").Should().BeEmpty());
    }

    [Test]
    public async Task Locked_the_window_shows_neither_banner()
    {
        await using var context = NewContext();
        context.Services.GetRequiredService<AppStatus>().WarnOfBackupProof("It couldn't be restored.");

        var layout = context.Render<MainLayout>();

        layout.FindAll("#copy-folder-banner, #backup-proof-banner").Should().BeEmpty("the sign-in panel shows nothing of the app's data");
    }
}
