using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>A Products view over the real use cases and repositories, on a test's database, as the app builds it.</summary>
internal static class ProductsViews
{
    public static ProductsState Create(
        IDbContextFactory<TinapayanRMSDbContext> factory,
        IUnitOfWork unitOfWork,
        IViewStateStore? viewStateStore = null) =>
        new(
            new ProductQueries(
                new ProductRepository(factory),
                new ProductCategoryRepository(factory),
                new ProductFolderRepository(factory),
                new ProductVersionRepository(factory),
                new RawMaterialRepository(factory),
                new RawMaterialCategoryRepository(factory),
                new SubAssemblyRepository(factory),
                new SubAssemblyLineRepository(factory),
                new SubAssemblyRoleRepository(factory),
                new CostingDefaultsRepository(factory),
                new PackagingLinkRepository(factory),
                new PackagingLineRepository(factory),
                VersionRecorders.CreateReader(factory, unitOfWork)),
            new ProductEditor(
                new ProductRepository(factory),
                new ProductVersionRepository(factory),
                new ProductCloner(factory),
                VersionRecorders.CreateSaves(factory, unitOfWork),
                new ProductValidator(),
                new CostingParametersValidator()),
            new ProductListEditor(new ProductRepository(factory), new ProductFolderRepository(factory)),
            new ProductCategoryEditor(new ProductCategoryRepository(factory), new ProductCategoryValidator()),
            viewStateStore ?? new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
}
