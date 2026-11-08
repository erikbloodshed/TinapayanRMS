using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// A ProductVersionRecorder over real repositories, and the VersionedSaves over one, for a ViewModel under test, as
/// CompositionRoot builds them.
/// </summary>
public static class VersionRecorders
{
    public static ProductVersionRecorder Create(IDbContextFactory<TinapayanRMSDbContext> factory)
    {
        var productRepository = new ProductRepository(factory);
        var unitOfWork = factory as IUnitOfWork
            ?? throw new ArgumentException("The app's factory is its unit of work too (SharedTransactionDbContextFactory).", nameof(factory));
        return new ProductVersionRecorder(Reader(factory, productRepository, unitOfWork), productRepository, new ProductVersionRepository(factory));
    }

    public static VersionedSaves CreateSaves(IDbContextFactory<TinapayanRMSDbContext> factory, IUnitOfWork unitOfWork)
    {
        var productRepository = new ProductRepository(factory);
        var versionRepository = new ProductVersionRepository(factory);
        var reader = Reader(factory, productRepository, unitOfWork);
        return new VersionedSaves(
            unitOfWork,
            productRepository,
            versionRepository,
            new CostingParametersRepository(factory),
            reader,
            new ProductVersionRecorder(reader, productRepository, versionRepository));
    }

    private static SavedProductReader Reader(IDbContextFactory<TinapayanRMSDbContext> factory, ProductRepository productRepository, IUnitOfWork unitOfWork) => new(
        new PackagingLinkRepository(factory),
        new ProductSubAssemblyUsageRepository(factory),
        new PackagingLineRepository(factory),
        new CostingParametersRepository(factory),
        new SubAssemblyLineRepository(factory),
        productRepository,
        new CostingDefaultsRepository(factory),
        unitOfWork);
}
