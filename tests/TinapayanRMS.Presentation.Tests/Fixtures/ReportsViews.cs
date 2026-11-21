using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Reports;
using TinapayanRMS.UseCases.RawMaterials;
using TinapayanRMS.UseCases.Reports;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>The Reports screen over the real use cases and repositories, on a test's database, as the app builds it.</summary>
internal static class ReportsViews
{
    /// <param name="savedProducts">What prices the products, over the same database.</param>
    public static ReportsState Create(IDbContextFactory<TinapayanRMSDbContext> factory, SavedProductReader savedProducts) =>
        new(
            new ReportQueries(
                new ProductRepository(factory),
                savedProducts,
                new RawMaterialRepository(factory),
                new RawMaterialPriceHistoryRepository(factory),
                new IngredientBomLineRepository(factory),
                new ProductVersionRepository(factory)),
            new RawMaterialEditor(
                new RawMaterialRepository(factory),
                new RawMaterialPriceHistoryRepository(factory),
                new RawMaterialValidator(),
                new RawMaterialUnitValidator(),
                new RawMaterialBrandPriceValidator()),
            new LoggerConfiguration().CreateLogger());
}
