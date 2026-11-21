using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Products;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Products;

/// <summary>
/// A product category: added from the Products forms' picker by name, and kept by Settings' Product Categories tab with
/// its own Wastage %, refused with why.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class ProductCategoryEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task A_category_added_by_name_comes_back_with_its_id()
    {
        var editor = CreateEditor();

        var added = await editor.AddAsync("Breads", Cancellation);

        added.Id.Should().BePositive();
        (await editor.GetAllAsync(Cancellation)).Should().ContainSingle(c => c.Id == added.Id && c.Name == "Breads");
    }

    [Test]
    public async Task Settings_update_writes_the_name_and_wastage_over_the_stored_category()
    {
        var editor = CreateEditor();
        var added = await editor.AddAsync("Breads", Cancellation);

        await editor.UpdateAsync(new ProductCategory { Id = added.Id, Name = "Loaves", WastagePercent = 7.5m }, Cancellation);

        (await editor.GetAllAsync(Cancellation)).Should().ContainSingle()
            .Which.Should().Match<ProductCategory>(c => c.Name == "Loaves" && c.WastagePercent == 7.5m);
    }

    [Test]
    public async Task A_name_the_categories_cannot_take_is_refused_before_anything_is_written()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid(new ProductCategory { Name = "" })).Should().Throw<BusinessRuleException>();
        await editor.Awaiting(e => e.AddAsync(new ProductCategory { Name = "" }, Cancellation)).Should().ThrowAsync<BusinessRuleException>();
        (await editor.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    private ProductCategoryEditor CreateEditor() => new(new ProductCategoryRepository(CreateDbContextFactory()), new ProductCategoryValidator());
}
