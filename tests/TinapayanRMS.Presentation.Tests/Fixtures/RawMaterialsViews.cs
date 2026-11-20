using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.UseCases.RawMaterials;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>A Raw Materials view over the real use cases and repositories, on a test's database, as the app builds it.</summary>
internal static class RawMaterialsViews
{
    /// <param name="costShares">What the Dependents tiles' shares are worked out by; none, and they aren't.</param>
    public static RawMaterialsState Create(
        IDbContextFactory<TinapayanRMSDbContext> factory,
        IViewStateStore? viewStateStore = null,
        MaterialCostShares? costShares = null) =>
        new(
            new RawMaterialQueries(
                new RawMaterialRepository(factory),
                new RawMaterialPriceHistoryRepository(factory),
                new RawMaterialBrandPriceRepository(factory),
                new IngredientBomLineRepository(factory),
                new SubAssemblyRepository(factory),
                new SubAssemblyLineRepository(factory),
                new RawMaterialCategoryRepository(factory),
                new UnitOfMeasureRepository(factory),
                new CostingDefaultsRepository(factory),
                costShares),
            new RawMaterialEditor(
                new RawMaterialRepository(factory),
                new RawMaterialPriceHistoryRepository(factory),
                new RawMaterialValidator(),
                new RawMaterialUnitValidator(),
                new RawMaterialBrandPriceValidator()),
            new RawMaterialCategoryEditor(new RawMaterialCategoryRepository(factory), new RawMaterialCategoryValidator()),
            viewStateStore ?? new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
}
