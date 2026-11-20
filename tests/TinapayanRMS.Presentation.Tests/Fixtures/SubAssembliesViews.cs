using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>A Sub-Assemblies view over the real use cases and repositories, on a test's database, as the app builds it.</summary>
internal static class SubAssembliesViews
{
    /// <param name="recipeImpact">What a Save of a recipe products link is checked with; none, and nothing is asked about.</param>
    public static SubAssembliesState Create(
        IDbContextFactory<TinapayanRMSDbContext> factory,
        IUnitOfWork unitOfWork,
        IViewStateStore? viewStateStore = null,
        SharedRecipeImpact? recipeImpact = null) =>
        new(
            new SubAssemblyQueries(
                new SubAssemblyRepository(factory),
                new SubAssemblyLineRepository(factory),
                new SubAssemblyRoleRepository(factory),
                new RawMaterialRepository(factory),
                new RawMaterialCategoryRepository(factory)),
            new SubAssemblyEditor(new SubAssemblyRepository(factory), new SubAssemblyValidator(), VersionRecorders.CreateSaves(factory, unitOfWork), recipeImpact),
            new SubAssemblyRoleEditor(new SubAssemblyRoleRepository(factory), new SubAssemblyRoleValidator()),
            viewStateStore ?? new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
}
