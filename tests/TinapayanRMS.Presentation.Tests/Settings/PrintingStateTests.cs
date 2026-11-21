using AwesomeAssertions;
using Serilog;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Settings;

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
        var editor = new PrintSettingsEditor(new PrintSettingsRepository(CreateDbContextFactory()));
        var page = new PrintPage(editor);
        return (new PrintingState(editor, page, new LoggerConfiguration().CreateLogger()), page);
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
    public Task The_heading_note_type_size_and_what_prints_are_saved_and_put_in_use() => UiThread.RunAsync(async () =>
    {
        var (state, page) = CreateState();
        await state.LoadCommand.ExecuteAsync();
        (state.PrintsUnitCosts, state.PrintsPercentages, state.PrintsFigures, state.PrintsPriceComparison)
            .Should().Be((true, true, true, true), "everything prints until something's left out");
        state.EditingShopName = " Tinapayan Festival Bakeshoppe ";
        state.EditingShopAddress = "Poblacion, Tagum City";
        state.EditingFooterNote = "For internal use only";
        state.EditingTypeSize = PrintTypeSize.Compact;
        state.PrintsUnitCosts = false;
        state.PrintsPriceComparison = false;

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.EditingShopName.Should().Be("Tinapayan Festival Bakeshoppe", "the field shows the name as it's kept");
        page.Settings.ShopName.Should().Be("Tinapayan Festival Bakeshoppe");
        page.HasHeading.Should().BeTrue();
        page.TypeScale.Should().Be(0.9m);
        var (reloaded, _) = CreateState();
        await reloaded.LoadCommand.ExecuteAsync();
        (reloaded.EditingShopAddress, reloaded.EditingFooterNote, reloaded.EditingTypeSize)
            .Should().Be(("Poblacion, Tagum City", "For internal use only", PrintTypeSize.Compact));
        (reloaded.PrintsUnitCosts, reloaded.PrintsPercentages, reloaded.PrintsFigures, reloaded.PrintsPriceComparison)
            .Should().Be((false, true, true, false));
    });

    [Test]
    public Task Choose_reads_a_picture_into_the_logo_and_Save_keeps_it_until_Remove() => UiThread.RunAsync(async () =>
    {
        var (state, page) = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var file = Path.Combine(Path.GetTempPath(), $"tinapayanrms-logo-{Guid.NewGuid():N}.png");
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7];
        await File.WriteAllBytesAsync(file, picture, Cancellation);
        try
        {
            using var _ = state.ChooseLogoFile.RegisterHandler(interaction => interaction.SetOutput(file));
            state.RemoveLogoCommand.CanExecute().Should().BeFalse("there's no logo to remove");

            await state.ChooseLogoCommand.ExecuteAsync();
            await state.SaveCommand.ExecuteAsync();

            state.LogoSource.Should().Be("data:image/png;base64," + Convert.ToBase64String(picture));
            page.LogoSource.Should().Be(state.LogoSource);
            state.RemoveLogoCommand.CanExecute().Should().BeTrue();

            await state.RemoveLogoCommand.ExecuteAsync();
            await state.SaveCommand.ExecuteAsync();

            page.Settings.Logo.Should().BeNull();
            page.LogoSource.Should().BeNull();
        }
        finally
        {
            File.Delete(file);
        }
    });

    [Test]
    public Task A_file_that_is_not_a_PNG_or_JPEG_is_refused_and_the_logo_stays() => UiThread.RunAsync(async () =>
    {
        var (state, _) = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var file = Path.Combine(Path.GetTempPath(), $"tinapayanrms-logo-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(file, "not a picture", Cancellation);
        try
        {
            using var _ = state.ChooseLogoFile.RegisterHandler(interaction => interaction.SetOutput(file));

            await state.ChooseLogoCommand.ExecuteAsync();

            state.ErrorMessage.Should().Be("A logo must be a PNG or JPEG picture.");
            state.LogoSource.Should().BeNull();
        }
        finally
        {
            File.Delete(file);
        }
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
