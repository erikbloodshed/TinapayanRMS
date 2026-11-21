using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Benchmarks.Scenarios;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Presentation.RawMaterials;

namespace TinapayanRMS.Benchmarks.Startup;

/// <summary>
/// What the app does as it opens, without its window, a step at a time: its services built (MauiProgram.CreateMauiApp's,
/// AppServices), the database made ready for this version (DatabaseReadiness, which the sign-in panel waits for), the
/// sign-in panel's load (SignInState), and the screen it opens on, Raw Materials (Pages/Home.razor), loaded until it has
/// settled. The panel is signed in to as the Debug build's TINAPAYANRMS_SIGNED_IN start does, AppLock.Unlock: the
/// password's check is a fixed cost (PasswordRules.Iterations) no refactor moves. The steps after the build run on the UI
/// thread, as the page's do.
/// </summary>
public sealed class AppStart : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    /// <summary>The page's scope, which the screen states are made in, as the web view's page is.</summary>
    private readonly AsyncServiceScope _page;

    private RawMaterialsState? _firstScreen;

    private AppStart(ServiceProvider services)
    {
        _services = services;
        _page = services.CreateAsyncScope();
    }

    /// <summary>The app's services on <paramref name="connectionString"/>.</summary>
    public static AppStart Build(string connectionString) => new(AppServices.Build(connectionString));

    /// <summary>The database made ready for this version: on a database already up to date, read and left as it is.</summary>
    public Task EnsureDatabaseReadyAsync() => _services.GetRequiredService<DatabaseReadiness>().EnsureReadyAsync();

    /// <summary>The sign-in panel loaded (it waits for the database to be ready, as above), then signed in to. On the UI thread.</summary>
    public async Task SignInAsync()
    {
        var signIn = _page.ServiceProvider.GetRequiredService<SignInState>();
        await signIn.LoadCommand.ExecuteAsync();
        await Screens.SettleAsync(signIn);
        Screens.EnsureNoFailure(signIn);
        Screens.Ensure(signIn.IsLoaded, "The sign-in panel didn't load.");

        var appLock = _services.GetRequiredService<AppLock>();
        appLock.SetAutoLockMinutes(0);
        appLock.Unlock();
    }

    /// <summary>Raw Materials, the screen the app opens on, loaded until it has settled. On the UI thread.</summary>
    public async Task OpenFirstScreenAsync()
    {
        _firstScreen = _page.ServiceProvider.GetRequiredService<RawMaterialsState>();
        await _firstScreen.LoadCommand.ExecuteAsync();
        await Screens.SettleAsync(_firstScreen);
    }

    /// <summary>Throws unless the first screen shows its list, and no failure.</summary>
    public void EnsureOpened()
    {
        Screens.Ensure(_firstScreen is not null, "The first screen wasn't opened.");
        Screens.EnsureNoFailure(_firstScreen);
        Screens.EnsureNoFailure(_firstScreen!.Form);
        Screens.Ensure(_firstScreen.RawMaterials.Count > 0, "The first screen's list is empty.");
    }

    public async ValueTask DisposeAsync()
    {
        await _page.DisposeAsync();
        await _services.DisposeAsync();
    }
}
