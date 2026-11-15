using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components;

/// <summary>
/// The app's services. The ViewModels are scoped: in Blazor Hybrid a scope is a page the window's web view loads, so a
/// reload (after a restore) builds every screen afresh. The repositories and services hold nothing but the context
/// factory, so they're singletons. What only the window can do (its file dialogs, its title bar's buttons) the MAUI app
/// adds: IFileDialogs and IAppWindow.
/// </summary>
public static class ServiceRegistration
{
    /// <param name="viewStateFile">
    /// The file the screens' conveniences are kept in for the Windows user (the lists' open folders, the Vim keys, the
    /// reports' columns, the backups' second folder).
    /// </param>
    public static IServiceCollection AddTinapayanRMS(this IServiceCollection services, IConfiguration configuration, string viewStateFile)
    {
        services.AddTinapayanRMSDatabase(configuration);
        services.AddSingleton<IProductCloner, ProductCloner>();
        services.AddSingleton<ICostRecalculator, CostRecalculator>();
        // Backups:ToolsFolder, when set, is the folder holding pg_dump and pg_restore; otherwise they're looked for. The
        // backups' own folder is one every Windows user on the PC shares (DatabaseBackups.DefaultFolder).
        services.AddSingleton<IDatabaseBackups>(provider => new DatabaseBackups(
            provider.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>(), DatabaseBackups.DefaultFolder, configuration["Backups:ToolsFolder"]));
        services.AddSingleton<SavedProductReader>();
        services.AddSingleton<MaterialCostShares>();
        services.AddSingleton<SharedRecipeImpact>();
        services.AddSingleton<ProductVersionRecorder>();
        services.AddSingleton<VersionedSaves>();

        // One store for the process, so its lock covers every page the web view loads.
        services.AddSingleton<IViewStateStore>(new JsonViewStateStore(viewStateFile));

        services.AddSingleton(Log.Logger);
        services.AddSingleton<IValidator<UnitOfMeasure>, UnitOfMeasureValidator>();
        services.AddSingleton<IValidator<RawMaterialCategory>, RawMaterialCategoryValidator>();
        services.AddSingleton<IValidator<ProductCategory>, ProductCategoryValidator>();
        services.AddSingleton<IValidator<RawMaterial>, RawMaterialValidator>();
        services.AddSingleton<IValidator<RawMaterialUnit>, RawMaterialUnitValidator>();
        services.AddSingleton<IValidator<SubAssemblyRole>, SubAssemblyRoleValidator>();
        services.AddSingleton<IValidator<SubAssembly>, SubAssemblyValidator>();
        services.AddSingleton<IValidator<Product>, ProductValidator>();
        services.AddSingleton<IValidator<CostingParameters>, CostingParametersValidator>();
        services.AddSingleton<IValidator<RawMaterialBrandPrice>, RawMaterialBrandPriceValidator>();

        services.AddScoped<MainWindowViewModel>();
        services.AddScoped<RawMaterialsViewModel>();
        services.AddScoped<SubAssembliesViewModel>();
        services.AddScoped<ProductsViewModel>();
        services.AddScoped<ReportsViewModel>();
        services.AddScoped<QuickFindViewModel>();
        services.AddScoped<PriceSimulatorViewModel>();
        services.AddScoped<PrintTabViewModel>();
        services.AddScoped<UnitsOfMeasureViewModel>();
        services.AddScoped<CategoriesViewModel>();
        services.AddScoped<ProductCategoriesViewModel>();
        services.AddScoped<SubAssemblyRolesViewModel>();
        services.AddScoped<CostingDefaultsViewModel>();
        services.AddScoped<SecuritySettingsViewModel>();

        // Whether the window is locked, one for the process, so the page loading again after a restore keeps it; the
        // sign-in panel's ViewModel is the page's.
        services.AddSingleton(new AppLock(TimeProvider.System));
        services.AddScoped<SignInViewModel>();

        // The Vim keys, for this page: ticking the box in Settings → Keyboard turns them on or off at once.
        services.AddScoped<VimKeysState>();
        services.AddScoped(provider => new KeyboardSettingsViewModel(
            provider.GetRequiredService<IViewStateStore>(), provider.GetRequiredService<VimKeysState>().Set));

        // A restore replaces everything every screen holds, so the window starts again (AppStatus.Restored): a forced load
        // of its page opens a new scope, which builds every ViewModel afresh.
        services.AddSingleton<AppStatus>();

        // The day's automatic backup and its copy to a second folder, checked every hour (StartupTasks). The second folder
        // and the last problems are kept in the view state.
        services.AddSingleton<AutomaticBackups>();
        services.AddScoped(provider => new DatabaseBackupsViewModel(
            provider.GetRequiredService<IDatabaseBackups>(),
            message => provider.GetRequiredService<AppStatus>().AnnounceRestore(message),
            provider.GetRequiredService<Serilog.ILogger>(),
            provider.GetRequiredService<AutomaticBackups>()));
        // A backup or restore running there keeps the window open (UnsavedChangesGuard): closing ends the process.
        services.AddScoped<IBusyWork>(provider => provider.GetRequiredService<DatabaseBackupsViewModel>());
        // Started by the MAUI app once it's built, as a web host starts its hosted services.
        services.AddHostedService<StartupTasks>();

        services.AddScoped<Notices>();
        services.AddScoped<Confirmations>();
        services.AddScoped<Prompts>();
        services.AddScoped<ScreenLinks>();
        services.AddScoped<ScreenTabs>();
        services.AddScoped<PrintPreviews>();
        services.AddScoped<UnsavedChangesGuard>();

        // The web view's own print dialog.
        services.AddScoped<IPagePrinter, WebViewPagePrinter>();

        return services;
    }
}
