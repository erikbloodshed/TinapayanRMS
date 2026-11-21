using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Settings;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Settings;

/// <summary>How every printout is laid out: the app's own until saved, and a save gives back what's kept, names tidied.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class PrintSettingsEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Before_anything_is_saved_the_settings_are_the_apps_own()
    {
        var settings = await CreateEditor().GetAsync(Cancellation);

        (settings.PaperSize, settings.Orientation, settings.Margins).Should().Be((PaperSize.A4, PaperOrientation.Portrait, PageMargins.Normal));
    }

    [Test]
    public async Task A_save_gives_back_the_settings_as_kept_names_tidied()
    {
        var editor = CreateEditor();

        var kept = await editor.SaveAsync(
            new PrintSettings { PaperSize = PaperSize.LongBond, ShopName = "  Tinapayan  ", FooterNote = "   " }, Cancellation);

        (kept.PaperSize, kept.ShopName, kept.FooterNote).Should().Be((PaperSize.LongBond, "Tinapayan", (string?)null));
        (await editor.GetAsync(Cancellation)).ShopName.Should().Be("Tinapayan");
    }

    private PrintSettingsEditor CreateEditor() => new(new PrintSettingsRepository(CreateDbContextFactory()));
}
