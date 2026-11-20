using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.RawMaterials;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.RawMaterials;

/// <summary>
/// The Raw Materials editor and queries on the real repositories: what the editor refuses, what a Save writes and reads
/// back, a rename refused over a newer save, and the catalog's read. The screen's own tests (Presentation.Tests'
/// RawMaterials) drive them through the form.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class RawMaterialEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public void A_material_needs_a_name_and_a_category()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid(Purchased("", categoryId: 1))).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid(Purchased("Sugar", categoryId: 0))).Should().Throw<BusinessRuleException>().WithMessage("Category is required.");
        editor.Invoking(e => e.EnsureValid(Purchased("Sugar", categoryId: 1))).Should().NotThrow();
    }

    [Test]
    public void A_unit_and_a_brand_price_are_checked_as_the_form_checks_each_row()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid(new RawMaterialUnit { UnitOfMeasureId = 0, BaseUnitsPerUnit = 1000m }))
            .Should().Throw<BusinessRuleException>().WithMessage("Unit is required.");
        editor.Invoking(e => e.EnsureValid(new RawMaterialUnit { UnitOfMeasureId = 1, BaseUnitsPerUnit = 0m })).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid(new RawMaterialBrandPrice { Brand = new string('x', 300), Price = 1m })).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid(new RawMaterialBrandPrice { Brand = "Sugarland", Price = 45m })).Should().NotThrow();
    }

    [Test]
    public async Task A_save_is_refused_as_the_check_refuses_whoever_calls_it()
    {
        await CreateEditor().Awaiting(e => e.SaveAsync(new RawMaterialSaveRequest(Purchased("Sugar", categoryId: 0), [], null, null), Cancellation))
            .Should().ThrowAsync<BusinessRuleException>().WithMessage("Category is required.");
    }

    [Test]
    public async Task A_purchased_material_is_saved_with_its_brand_prices_and_reads_back_with_them()
    {
        var categoryId = await AddCategoryAsync();

        var id = await CreateEditor().SaveAsync(
            new RawMaterialSaveRequest(Purchased("Sugar", categoryId), [], [("Sugarland", null, 0.09m)], null), Cancellation);

        var queries = CreateQueries();
        var saved = (await queries.ReadAsync(id, Cancellation))!;
        saved.Name.Should().Be("Sugar");
        var detail = await queries.ReadDetailAsync(saved, Cancellation);
        detail.BrandPrices.Select(p => (p.Brand, p.Price)).Should().Equal(("Sugarland", 0.09m));
        detail.BomLines.Should().BeEmpty();
    }

    [Test]
    public async Task A_rename_is_refused_over_a_save_made_since_it_was_opened()
    {
        var categoryId = await AddCategoryAsync();
        var editor = CreateEditor();
        var queries = CreateQueries();
        var id = await editor.SaveAsync(new RawMaterialSaveRequest(Purchased("Sugar", categoryId), [], [(null, null, 0.09m)], null), Cancellation);
        var openedAt = (await queries.ReadAsync(id, Cancellation))!.UpdatedAt!.Value;

        // Another PC saves it after the list read it.
        await editor.SaveAsync(new RawMaterialSaveRequest(Purchased("Sugar", categoryId, id, openedAt), [], [(null, null, 0.10m)], null), Cancellation);

        await editor.Awaiting(e => e.RenameAsync(id, "Brown Sugar", [new SavedVersionCheck(id, openedAt)], Cancellation))
            .Should().ThrowAsync<SavedSinceException>();
        (await queries.ReadAsync(id, Cancellation))!.Name.Should().Be("Sugar");
    }

    [Test]
    public async Task The_catalog_reads_each_materials_dependents_once_however_many_tabs_show_it()
    {
        var categoryId = await AddCategoryAsync();
        var id = await CreateEditor().SaveAsync(new RawMaterialSaveRequest(Purchased("Sugar", categoryId), [], [(null, null, 0.09m)], null), Cancellation);

        var catalog = await CreateQueries().ReadCatalogAsync([id, id], Cancellation);

        catalog.RawMaterials.Should().ContainSingle(m => m.Id == id);
        catalog.Dependents.Keys.Should().Equal(id);
        catalog.Dependents[id].Should().BeEmpty("no product uses it yet");
    }

    private static RawMaterial Purchased(string name, int categoryId, int id = 0, DateTime? savedVersion = null) => new()
    {
        Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Purchased,
        UpdatedAt = savedVersion,
    };

    private async Task<int> AddCategoryAsync() =>
        (await new RawMaterialCategoryEditor(new RawMaterialCategoryRepository(CreateDbContextFactory()), new RawMaterialCategoryValidator())
            .AddAsync("Dry goods", RawMaterialCategoryType.Ingredient, Cancellation)).Id;

    private RawMaterialEditor CreateEditor()
    {
        var factory = CreateDbContextFactory();
        return new RawMaterialEditor(
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialBrandPriceValidator());
    }

    private RawMaterialQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new RawMaterialQueries(
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new IngredientBomLineRepository(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new UnitOfMeasureRepository(factory),
            new CostingDefaultsRepository(factory));
    }
}
