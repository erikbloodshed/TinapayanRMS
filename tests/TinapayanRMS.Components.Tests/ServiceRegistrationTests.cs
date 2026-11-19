using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Presentation.PriceSimulator;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Reports;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Tests;

/// <summary>ServiceRegistration.AddTinapayanRMS: everything the window's pages ask for is registered, and scoped where it must be.</summary>
public class ServiceRegistrationTests
{
    [Test]
    public async Task The_context_factory_uses_the_configured_connection_string()
    {
        await using var context = AppServices.NewContext();

        var factory = context.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(TestContext.Current!.Execution.CancellationToken);

        dbContext.Database.GetConnectionString().Should().StartWith(AppServices.ConnectionString, "EF Core only adds its application name after it");
    }

    [Test]
    public async Task The_shell_view_model_has_the_shops_heading()
    {
        await using var context = AppServices.NewContext();

        var state = context.Services.GetRequiredService<MainWindowState>();

        // An independent literal, not just internal consistency with Title: this would still pass if Heading derived
        // correctly from a Title that was itself wrong.
        state.Title.Should().Be("Tinapayan Festival Bakeshoppe — Recipe & Costing");
        state.Heading.Should().Be(state.Title.ToUpperInvariant());
    }

    [Test]
    [Arguments(typeof(RawMaterialsState))]
    [Arguments(typeof(SubAssembliesState))]
    [Arguments(typeof(ProductsState))]
    [Arguments(typeof(ReportsState))]
    [Arguments(typeof(PriceSimulatorState))]
    [Arguments(typeof(QuickFindState))]
    [Arguments(typeof(PrintDialogState))]
    [Arguments(typeof(UnitsOfMeasureState))]
    [Arguments(typeof(RawMaterialCategoriesState))]
    [Arguments(typeof(ProductCategoriesState))]
    [Arguments(typeof(SubAssemblyRolesState))]
    [Arguments(typeof(CostingDefaultsState))]
    [Arguments(typeof(KeyboardState))]
    [Arguments(typeof(DatabaseBackupsState))]
    [Arguments(typeof(ScreenLinks))]
    [Arguments(typeof(Notices))]
    [Arguments(typeof(Confirmations))]
    [Arguments(typeof(Prompts))]
    [Arguments(typeof(UnsavedChangesGuard))]
    [Arguments(typeof(IPagePrinter))]
    [Arguments(typeof(PrintPreviews))]
    [Arguments(typeof(VimKeysState))]
    public async Task Every_screens_services_resolve(Type service)
    {
        await using var context = AppServices.NewContext();

        // Building each one checks that everything its constructor asks for is registered.
        context.Services.GetRequiredService(service).Should().NotBeNull();
    }

    [Test]
    public async Task Each_page_the_window_loads_gets_its_own_screens()
    {
        await using var context = AppServices.NewContext();

        // A scope is a page the web view loads: a reload (after a restore) must build every screen afresh.
        using var firstPage = context.Services.CreateScope();
        using var reloaded = context.Services.CreateScope();

        var first = firstPage.ServiceProvider.GetRequiredService<ProductsState>();
        first.Should().BeSameAs(firstPage.ServiceProvider.GetRequiredService<ProductsState>());
        first.Should().NotBeSameAs(reloaded.ServiceProvider.GetRequiredService<ProductsState>());
    }

    [Test]
    public async Task Ticking_the_keyboard_box_turns_the_vim_keys_on_for_the_page()
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();
        var changes = 0;
        keys.Changed += () => changes++;
        keys.IsOn.Should().BeFalse("they're off until the Windows user turns them on");

        context.Services.GetRequiredService<KeyboardState>().IsVimKeysOn = true;

        keys.IsOn.Should().BeTrue();
        changes.Should().Be(1);
    }
}
