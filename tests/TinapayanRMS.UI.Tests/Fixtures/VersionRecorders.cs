using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>A ProductVersionRecorder over real repositories, for a ViewModel under test, as CompositionRoot builds it.</summary>
public static class VersionRecorders
{
    public static ProductVersionRecorder Create(IDbContextFactory<TinapayanRMSDbContext> factory)
    {
        var productRepository = new ProductRepository(factory);
        return new ProductVersionRecorder(
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                productRepository),
            productRepository,
            new ProductVersionRepository(factory));
    }
}
