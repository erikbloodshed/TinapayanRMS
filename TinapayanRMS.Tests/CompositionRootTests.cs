using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.UI;

namespace TinapayanRMS.Tests;

public class CompositionRootTests
{
    [Fact]
    public void CreateHostBuilder_resolves_a_DbContext_from_configuration()
    {
        using IHost host = CompositionRoot.CreateHostBuilder([]).Build();

        var dbContextFactory = host.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>();
        using var dbContext = dbContextFactory.CreateDbContext();

        dbContext.Database.GetConnectionString().Should().Contain("Initial Catalog=TinapayanRMS");
    }

    [Fact]
    public void CreateHostBuilder_resolves_the_main_window_view_model()
    {
        using IHost host = CompositionRoot.CreateHostBuilder([]).Build();

        var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();

        // An independent literal, not just internal consistency with Title: this would
        // still pass if Heading derived correctly from a Title that was itself wrong.
        viewModel.Title.Should().Be("Tinapayan Festival Bakeshoppe — Recipe & Costing");
        viewModel.Heading.Should().Be(viewModel.Title.ToUpperInvariant());
    }
}
