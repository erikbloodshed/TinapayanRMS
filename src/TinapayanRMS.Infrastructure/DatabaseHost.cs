using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure;

/// <summary>
/// A host with the app's database and every repository registered, from the appsettings.json beside it: what the app
/// (CompositionRoot) and its two servers, the Recipe API and the MCP server, all start from, so they reach the same database the
/// same way. It's here, not in the UI project, because the servers can't reference that.
/// </summary>
public static class DatabaseHost
{
    public static IHostBuilder CreateBuilder() =>
        new HostBuilder()
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
                // which also makes concurrent calls from different screens safe. The factory is also the
                // unit of work: inside one, every repository's context shares its one transaction.
                services.AddSingleton(new SharedTransactionDbContextFactory(connectionString));
                services.AddSingleton<IDbContextFactory<TinapayanRMSDbContext>>(provider => provider.GetRequiredService<SharedTransactionDbContextFactory>());
                services.AddSingleton<IUnitOfWork>(provider => provider.GetRequiredService<SharedTransactionDbContextFactory>());

                services.AddSingleton<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
                services.AddSingleton<IRawMaterialCategoryRepository, RawMaterialCategoryRepository>();
                services.AddSingleton<IProductCategoryRepository, ProductCategoryRepository>();
                services.AddSingleton<IRawMaterialRepository, RawMaterialRepository>();
                services.AddSingleton<IRawMaterialUnitRepository, RawMaterialUnitRepository>();
                services.AddSingleton<IRawMaterialPriceHistoryRepository, RawMaterialPriceHistoryRepository>();
                services.AddSingleton<IRawMaterialBrandPriceRepository, RawMaterialBrandPriceRepository>();
                services.AddSingleton<IIngredientBomLineRepository, IngredientBomLineRepository>();
                services.AddSingleton<ISubAssemblyRoleRepository, SubAssemblyRoleRepository>();
                services.AddSingleton<ISubAssemblyRepository, SubAssemblyRepository>();
                services.AddSingleton<ISubAssemblyLineRepository, SubAssemblyLineRepository>();
                services.AddSingleton<IProductRepository, ProductRepository>();
                services.AddSingleton<IProductSubAssemblyUsageRepository, ProductSubAssemblyUsageRepository>();
                services.AddSingleton<IPackagingLineRepository, PackagingLineRepository>();
                services.AddSingleton<ICostingParametersRepository, CostingParametersRepository>();
                services.AddSingleton<ICostingDefaultsRepository, CostingDefaultsRepository>();
                services.AddSingleton<IPackagingLinkRepository, PackagingLinkRepository>();
                services.AddSingleton<IProductVersionRepository, ProductVersionRepository>();
                services.AddSingleton<IPriceScenarioRepository, PriceScenarioRepository>();
            });
}
