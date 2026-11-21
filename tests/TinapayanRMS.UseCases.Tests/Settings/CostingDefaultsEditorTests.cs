using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Settings;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Settings;

/// <summary>
/// Settings' Costing Defaults tab: saved and read back, a percentage a product's costing would refuse refused with the same
/// words before anything is written, and the stored costs worked out again.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class CostingDefaultsEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task What_is_saved_reads_back()
    {
        var editor = CreateEditor();

        var defaults = Defaults(labor: 25m);
        defaults.NewProductStage = ProductStage.Innovation;
        defaults.VatPercent = 12m;

        await editor.SaveAsync(defaults, Cancellation);

        var saved = await editor.GetAsync(Cancellation);
        (saved.LaborPercent, saved.NewProductStage, saved.VatPercent).Should().Be((25m, ProductStage.Innovation, 12m));
    }

    [Test]
    public async Task A_percentage_a_products_costing_refuses_is_refused_with_its_words_and_nothing_is_written()
    {
        var editor = CreateEditor();
        await editor.SaveAsync(Defaults(labor: 25m), Cancellation);
        var expected = new CostingParametersValidator()
            .Validate(new CostingParameters { LaborPercent = -1m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 40m })
            .ToString(" ");

        await editor.Awaiting(e => e.SaveAsync(Defaults(labor: -1m), Cancellation))
            .Should().ThrowAsync<BusinessRuleException>().WithMessage(expected);

        (await editor.GetAsync(Cancellation)).LaborPercent.Should().Be(25m);
    }

    [Test]
    public async Task Recalculating_with_every_cost_up_to_date_corrects_none()
    {
        (await CreateEditor().RecalculateCostsAsync(Cancellation)).IngredientsCorrected.Should().Be(0);
    }

    private static CostingDefaults Defaults(decimal labor) => new()
    {
        LaborPercent = labor, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 40m,
    };

    private CostingDefaultsEditor CreateEditor() => new(
        new CostingDefaultsRepository(CreateDbContextFactory()), new CostingParametersValidator(), new CostRecalculator(CreateDbContextFactory()));
}
