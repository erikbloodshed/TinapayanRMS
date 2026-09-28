using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>Settings' Costing Defaults tab: loads what's saved, saves what's typed, and refuses a negative percentage.</summary>
[Collection(DatabaseCollection.Name)]
public class CostingDefaultsViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private CostingDefaultsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        return new CostingDefaultsViewModel(
            new CostingDefaultsRepository(CreateDbContextFactory()), new CostingParametersValidator(), new LoggerConfiguration().CreateLogger());
    }

    [Fact]
    public async Task Save_keeps_the_percentages_and_Load_reads_them_back()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.EditingLaborPercent = 25m;
        viewModel.EditingOverheadPercent = 10m;
        viewModel.EditingAdministrativePercent = 5m;
        viewModel.EditingMarginPercent = 40m;
        viewModel.EditingVatPercent = 12m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().NotBeNull();
        var reloaded = CreateViewModel();
        await reloaded.LoadCommand.Execute();
        (reloaded.EditingLaborPercent, reloaded.EditingOverheadPercent, reloaded.EditingAdministrativePercent, reloaded.EditingMarginPercent, reloaded.EditingVatPercent)
            .Should().Be((25m, 10m, 5m, 40m, 12m));
    }

    [Fact]
    public async Task Save_refuses_a_negative_percentage_and_saves_nothing()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.EditingVatPercent = -12m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("VAT percent can't be negative.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).VatPercent.Should().Be(0m);
    }
}
