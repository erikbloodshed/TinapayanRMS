using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Settings → Printing: no row reads as A4, portrait and normal margins; a save is read back, and saved again over the one
/// row; a choice the lists don't offer is refused by the rules, and by the table.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class PrintSettingsPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private PrintSettingsRepository CreateRepository() => new(CreateDbContextFactory());

    [Test]
    public async Task Before_anything_is_saved_printouts_are_A4_portrait_with_normal_margins()
    {
        var settings = await CreateRepository().GetAsync(Cancellation);

        (settings.PaperSize, settings.Orientation, settings.Margins)
            .Should().Be((PaperSize.A4, PaperOrientation.Portrait, PageMargins.Normal));
    }

    [Test]
    public async Task A_save_is_read_back_and_a_second_replaces_it_in_the_one_row()
    {
        await CreateRepository().SetAsync(
            new PrintSettings { PaperSize = PaperSize.LongBond, Orientation = PaperOrientation.Landscape, Margins = PageMargins.Narrow }, Cancellation);
        var first = await CreateRepository().GetAsync(Cancellation);

        await CreateRepository().SetAsync(new PrintSettings { PaperSize = PaperSize.Letter, Margins = PageMargins.Wide }, Cancellation);
        var second = await CreateRepository().GetAsync(Cancellation);

        (first.PaperSize, first.Orientation, first.Margins).Should().Be((PaperSize.LongBond, PaperOrientation.Landscape, PageMargins.Narrow));
        (second.PaperSize, second.Orientation, second.Margins).Should().Be((PaperSize.Letter, PaperOrientation.Portrait, PageMargins.Wide));
        await using var dbContext = CreateDbContext();
        (await dbContext.PrintSettings.CountAsync(Cancellation)).Should().Be(1);
    }

    [Test]
    public async Task A_paper_the_list_does_not_offer_is_refused_and_nothing_is_saved()
    {
        var act = () => CreateRepository().SetAsync(new PrintSettings { PaperSize = (PaperSize)99 }, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Choose a paper size from the list.");
        await using var dbContext = CreateDbContext();
        (await dbContext.PrintSettings.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Test]
    public async Task The_table_refuses_a_paper_written_around_the_rules()
    {
        await CreateRepository().SetAsync(new PrintSettings(), Cancellation);
        await using var dbContext = CreateDbContext();

        var act = () => dbContext.Database.ExecuteSqlRawAsync("UPDATE print_settings SET paper_size = 'A3'", Cancellation);

        await act.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "CK_PrintSettings_PaperSize");
    }
}
