using AwesomeAssertions;
using Serilog;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>
/// Settings' Printing tab: loads what's saved, saves what's picked and puts it in use for the printouts at once, and says
/// how much of the paper each page prints on.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class PrintingStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private (PrintingState State, PrintPage Page) CreateState()
    {
        var repository = new PrintSettingsRepository(CreateDbContextFactory());
        var page = new PrintPage(repository);
        return (new PrintingState(repository, page, new LoggerConfiguration().CreateLogger()), page);
    }

    [Test]
    public Task Load_shows_A4_portrait_and_normal_margins_before_anything_is_saved() => UiThread.RunAsync(async () =>
    {
        var (state, _) = CreateState();
        state.EditingPaperSize = PaperSize.Legal;

        await state.LoadCommand.ExecuteAsync();

        (state.EditingPaperSize, state.EditingOrientation, state.EditingMargins)
            .Should().Be((PaperSize.A4, PaperOrientation.Portrait, PageMargins.Normal));
        state.PrintedAreaText.Should().Be("Each page prints on 182 × 267 mm of the paper.");
        state.PaperSizes.Select(p => p.Value).Should().Equal(
            "A4 (210 × 297 mm)", "Letter, short (8.5 × 11 in)", "Long (8.5 × 13 in)", "Legal (8.5 × 14 in)");
    });

    [Test]
    public Task Save_keeps_the_choices_puts_them_in_use_and_Load_reads_them_back() => UiThread.RunAsync(async () =>
    {
        var (state, page) = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingPaperSize = PaperSize.LongBond;
        state.EditingOrientation = PaperOrientation.Landscape;
        state.EditingMargins = PageMargins.Narrow;

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().NotBeNull();
        page.Paper.Should().Be(new ApplicationCore.Rules.PageArea(330.2m, 215.9m), "the printouts use it without being read again");
        var (reloaded, _) = CreateState();
        await reloaded.LoadCommand.ExecuteAsync();
        (reloaded.EditingPaperSize, reloaded.EditingOrientation, reloaded.EditingMargins)
            .Should().Be((PaperSize.LongBond, PaperOrientation.Landscape, PageMargins.Narrow));
        reloaded.PrintedAreaText.Should().Be("Each page prints on 314.2 × 195.9 mm of the paper.");
    });

    [Test]
    public Task The_printed_area_follows_each_pick_before_it_is_saved() => UiThread.RunAsync(async () =>
    {
        var (state, page) = CreateState();
        await state.LoadCommand.ExecuteAsync();
        List<string?> changed = [];
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        state.EditingMargins = PageMargins.Wide;

        state.PrintedAreaText.Should().Be("Each page prints on 170 × 255 mm of the paper.");
        changed.Should().Contain(nameof(PrintingState.PrintedAreaText));
        page.PrintedArea.Should().Be(new ApplicationCore.Rules.PageArea(182m, 267m), "nothing's in use until it's saved");
    });
}
