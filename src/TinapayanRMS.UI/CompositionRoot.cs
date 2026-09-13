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
using TinapayanRMS.UI.ViewModels;
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

        return new HostBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");

                // A factory, not AddDbContext: ViewModels are singletons that outlive any one
                // screen, so there's no natural request scope to hand a single DbContext to.
                // Each repository call creates and disposes its own short-lived context instead,
                // which also makes concurrent calls from different screens safe.
                services.AddDbContextFactory<TinapayanRMSDbContext>(options => options.UseSqlServer(connectionString));

                services.AddSingleton<SubAssemblyCostPropagator>();
                services.AddSingleton<IngredientCostPropagator>();
                services.AddSingleton<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
                services.AddSingleton<IIngredientCategoryRepository, IngredientCategoryRepository>();
                services.AddSingleton<IPackagingCategoryRepository, PackagingCategoryRepository>();
                services.AddSingleton<IIngredientRepository, IngredientRepository>();
                services.AddSingleton<IIngredientUnitRepository, IngredientUnitRepository>();
                services.AddSingleton<IPackagingMaterialRepository, PackagingMaterialRepository>();
                services.AddSingleton<IPackagingMaterialUnitRepository, PackagingMaterialUnitRepository>();
                services.AddSingleton<IIngredientPriceHistoryRepository, IngredientPriceHistoryRepository>();
                services.AddSingleton<IPackagingPriceHistoryRepository, PackagingPriceHistoryRepository>();
                services.AddSingleton<IIngredientBomLineRepository, IngredientBomLineRepository>();
                services.AddSingleton<ISubAssemblyRoleRepository, SubAssemblyRoleRepository>();
                services.AddSingleton<ISubAssemblyRepository, SubAssemblyRepository>();
                services.AddSingleton<ISubAssemblyLineRepository, SubAssemblyLineRepository>();
                services.AddSingleton<IProductRepository, ProductRepository>();
                services.AddSingleton<IProductSubAssemblyUsageRepository, ProductSubAssemblyUsageRepository>();
                services.AddSingleton<IPackagingLineRepository, PackagingLineRepository>();
                services.AddSingleton<ICostingParametersRepository, CostingParametersRepository>();
                services.AddSingleton<IProductTabLinkRepository, ProductTabLinkRepository>();
                services.AddSingleton<IProductCloner, ProductCloner>();

                services.AddSingleton(Log.Logger);
                services.AddSingleton<IValidator<UnitOfMeasure>, UnitOfMeasureValidator>();
                services.AddSingleton<IValidator<IngredientCategory>, IngredientCategoryValidator>();
                services.AddSingleton<IValidator<PackagingCategory>, PackagingCategoryValidator>();
                services.AddSingleton<IValidator<Ingredient>, IngredientValidator>();
                services.AddSingleton<IValidator<IngredientUnit>, IngredientUnitValidator>();
                services.AddSingleton<IValidator<PackagingMaterial>, PackagingMaterialValidator>();
                services.AddSingleton<IValidator<PackagingMaterialUnit>, PackagingMaterialUnitValidator>();
                services.AddSingleton<IValidator<SubAssemblyRole>, SubAssemblyRoleValidator>();
                services.AddSingleton<IValidator<SubAssembly>, SubAssemblyValidator>();
                services.AddSingleton<IValidator<Product>, ProductValidator>();
                services.AddSingleton<IValidator<CostingParameters>, CostingParametersValidator>();
                services.AddSingleton<IValidator<IngredientPriceHistory>, IngredientPriceHistoryValidator>();
                services.AddSingleton<IValidator<PackagingPriceHistory>, PackagingPriceHistoryValidator>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IngredientsViewModel>();
                services.AddSingleton<PackagingMaterialsViewModel>();
                services.AddSingleton<SubAssembliesViewModel>();
                services.AddSingleton<ProductsViewModel>();
                services.AddSingleton<ReportsViewModel>();
                services.AddSingleton<UnitsOfMeasureViewModel>();
            });
    }
}
