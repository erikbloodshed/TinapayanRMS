using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Tests;

/// <summary>
/// The app's host, as App builds it, but with the view state kept in memory: the real one is the developer's own
/// %LocalAppData%\TinapayanRMS\view-state.json, which a screen disposed here would otherwise write to.
/// </summary>
public class CompositionRootTests
{
    [Test]
    public void CreateHostBuilder_resolves_a_DbContext_from_configuration()
    {
        using var host = HostWith(new InMemoryViewStateStore());

        var dbContextFactory = host.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>();
        using var dbContext = dbContextFactory.CreateDbContext();

        dbContext.Database.GetConnectionString().Should().Contain("Database=tinapayanrms");
        dbContext.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Test]
    public void CreateHostBuilder_keeps_the_view_state_in_a_file()
    {
        // Resolving the store alone reads and writes nothing.
        using var host = CompositionRoot.CreateHostBuilder([]).Build();

        host.Services.GetRequiredService<IViewStateStore>().Should().BeOfType<JsonViewStateStore>();
    }

    [Test]
    public void CreateHostBuilder_resolves_the_main_window_view_model()
    {
        using var host = HostWith(new InMemoryViewStateStore());

        var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();

        // An independent literal, not just internal consistency with Title: this would
        // still pass if Heading derived correctly from a Title that was itself wrong.
        viewModel.Title.Should().Be("Tinapayan Festival Bakeshoppe — Recipe & Costing");
        viewModel.Heading.Should().Be(viewModel.Title.ToUpperInvariant());
    }

    [Test]
    public void CreateHostBuilder_resolves_every_screens_view_model_and_one_never_shown_writes_no_view_state()
    {
        // As the developer's own file can hold them: open folders kept before the lists kept their closed ones.
        var store = new InMemoryViewStateStore(new Dictionary<string, IReadOnlyList<string>>
        {
            [ProductsViewModel.OpenFoldersKey] = ["Production", "Production/5", "ResearchAndDevelopment"],
            [RawMaterialsViewModel.OpenFoldersKey] = ["Ingredient", "Ingredient/57"],
            [SubAssembliesViewModel.ClosedFoldersKey] = ["2", "3"],
        });

        using (var host = HostWith(store))
        {
            // Building each one checks that everything its constructor asks for is registered.
            host.Services.GetRequiredService<RawMaterialsViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<SubAssembliesViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<ProductsViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<ReportsViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<PriceSimulatorViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<UnitsOfMeasureViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<CategoriesViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<ProductCategoriesViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<SubAssemblyRolesViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<DatabaseBackupsViewModel>().Should().NotBeNull();
        }

        store.WriteCount.Should().Be(0, "a screen made and disposed without being shown has opened or closed nothing");
    }

    /// <summary>The app's host, with <paramref name="viewState"/> in place of the developer's own view-state.json.</summary>
    private static IHost HostWith(IViewStateStore viewState) =>
        CompositionRoot.CreateHostBuilder([])
            .ConfigureServices(services => services.AddSingleton(viewState))
            .Build();
}
