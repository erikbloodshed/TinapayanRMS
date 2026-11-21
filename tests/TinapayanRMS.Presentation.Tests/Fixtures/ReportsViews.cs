using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
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
    /// <param name="readsVersions">
    /// Whether Margin Erosion reads the product versions, as the app's does. Off, it has none to read, so a load leaves no
    /// read of them running for a test that doesn't wait for one (Settle) to outlast it, into the next test's database
    /// reset: only a test that checks Margin Erosion, and waits for it, turns it on.
    /// </param>
    public static ReportsState Create(IDbContextFactory<TinapayanRMSDbContext> factory, SavedProductReader savedProducts, bool readsVersions = false)
    {
        IReportQueries queries = new ReportQueries(
            new ProductRepository(factory),
            savedProducts,
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new IngredientBomLineRepository(factory),
            new ProductVersionRepository(factory));

        return new(
            readsVersions ? queries : new WithoutVersions(queries),
            new RawMaterialEditor(
                new RawMaterialRepository(factory),
                new RawMaterialPriceHistoryRepository(factory),
                new RawMaterialValidator(),
                new RawMaterialUnitValidator(),
                new RawMaterialBrandPriceValidator()),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>The visit's reads as they are, and no product versions to compare with.</summary>
    private sealed class WithoutVersions(IReportQueries queries) : IReportQueries
    {
        public Task<ReportsRead> ReadAsync(CancellationToken cancellationToken) => queries.ReadAsync(cancellationToken);

        public Task<IReadOnlyList<ProductVersion>> ReadVersionsAsOfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductVersion>>([]);
    }
}
