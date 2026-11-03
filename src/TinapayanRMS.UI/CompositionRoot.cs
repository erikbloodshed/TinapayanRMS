using System.Windows;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.ViewModels;
using TinapayanRMS.UI.Vim;
using TinapayanRMS.UI.Views;

namespace TinapayanRMS.UI;

/// <summary>
/// Builds the app's DI host. Split out from App.xaml.cs so tests can build and
/// resolve from it without starting the WPF application.
/// </summary>
public static class CompositionRoot
{
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        ReactiveUIBootstrapper.EnsureInitialized();

        return DatabaseHost.CreateBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IProductCloner, ProductCloner>();
                services.AddSingleton<ICostRecalculator, CostRecalculator>();
                // Backups:ToolsFolder, when set, is the folder holding pg_dump and pg_restore; otherwise they're looked for.
                var backupToolsFolder = context.Configuration["Backups:ToolsFolder"];
                services.AddSingleton<IDatabaseBackups>(provider => new DatabaseBackups(
                    provider.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>(), DatabaseBackups.DefaultFolder, backupToolsFolder));
                services.AddSingleton<SavedProductReader>();
                services.AddSingleton<MaterialCostShares>();
                services.AddSingleton<SharedRecipeImpact>();
                services.AddSingleton<ProductVersionRecorder>();
                services.AddSingleton<VersionedSaves>();
                services.AddSingleton<IViewStateStore>(new JsonViewStateStore(JsonViewStateStore.DefaultFilePath));

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

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<RawMaterialsViewModel>();
                services.AddSingleton<SubAssembliesViewModel>();
                services.AddSingleton<ProductsViewModel>();
                services.AddSingleton<ReportsViewModel>();
                services.AddSingleton<QuickFindViewModel>();
                services.AddSingleton<PriceSimulatorViewModel>();
                services.AddSingleton<PrintTabViewModel>();
                services.AddSingleton<UnitsOfMeasureViewModel>();
                services.AddSingleton<CategoriesViewModel>();
                services.AddSingleton<ProductCategoriesViewModel>();
                services.AddSingleton<SubAssemblyRolesViewModel>();
                services.AddSingleton<CostingDefaultsViewModel>();

                // Ticking the box turns the keys on in the running app as well as keeping the choice.
                services.AddSingleton(provider => new KeyboardSettingsViewModel(
                    provider.GetRequiredService<IViewStateStore>(), on => VimKeys.State.IsEnabled = on));

                // A restore replaces everything every screen holds, so the app starts again after one (App.RestartAfterExit).
                services.AddSingleton<AutomaticBackups>();
                services.AddSingleton(provider => new DatabaseBackupsViewModel(
                    provider.GetRequiredService<IDatabaseBackups>(),
                    message => ((App)Application.Current).RestartAfterExit(message),
                    provider.GetRequiredService<ILogger>(),
                    provider.GetRequiredService<AutomaticBackups>()));
            });
    }
}
