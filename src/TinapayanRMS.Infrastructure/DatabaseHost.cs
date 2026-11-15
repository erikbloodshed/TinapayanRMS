using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure;

/// <summary>
/// The app's database and every repository, registered the one way all their hosts register them: the app
/// (TinapayanRMS.Components' ServiceRegistration) and the two servers, the Recipe API and
/// the MCP server, so they reach the same database the same way. It's here, not in either app's project, because the
/// servers can't reference those.
/// </summary>
public static class DatabaseHost
{
    /// <summary>A host with <see cref="AddTinapayanRMSDatabase"/> on the connection string in the appsettings.json beside it.</summary>
    public static IHostBuilder CreateBuilder() =>
        new HostBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .ConfigureServices((context, services) => services.AddTinapayanRMSDatabase(context.Configuration));

    /// <summary>The database and the repositories, on <paramref name="configuration"/>'s ConnectionStrings:Default.</summary>
    public static IServiceCollection AddTinapayanRMSDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");

        // A factory, not AddDbContext: ViewModels outlive any one operation (one set for each
        // page the app's web view loads), so there's no natural request scope to hand a single DbContext to.
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
        services.AddSingleton<IProductFolderRepository, ProductFolderRepository>();
        services.AddSingleton<IProductSubAssemblyUsageRepository, ProductSubAssemblyUsageRepository>();
        services.AddSingleton<IPackagingLineRepository, PackagingLineRepository>();
        services.AddSingleton<ICostingParametersRepository, CostingParametersRepository>();
        services.AddSingleton<ICostingDefaultsRepository, CostingDefaultsRepository>();
        services.AddSingleton<IAccessSettingsRepository, AccessSettingsRepository>();
        services.AddSingleton<IPackagingLinkRepository, PackagingLinkRepository>();
        services.AddSingleton<IProductVersionRepository, ProductVersionRepository>();
        services.AddSingleton<IPriceScenarioRepository, PriceScenarioRepository>();

        return services;
    }
}
