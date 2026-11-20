using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.RawMaterials;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.RawMaterials;

/// <summary>What the Raw Materials screen does to a category: added with its kind, renamed keeping it, refused with why.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class RawMaterialCategoryEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task A_category_added_comes_back_with_its_id_and_kind()
    {
        var added = await CreateEditor().AddAsync("In-House", RawMaterialCategoryType.Made, Cancellation);

        added.Id.Should().BePositive();
        (await Repository().GetAllAsync(Cancellation)).Should().ContainSingle(c => c.Id == added.Id && c.Name == "In-House" && c.Type == RawMaterialCategoryType.Made);
    }

    [Test]
    public async Task A_name_the_categories_cannot_take_is_refused_before_anything_is_written()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid("", RawMaterialCategoryType.Ingredient)).Should().Throw<BusinessRuleException>().WithMessage("Name is required.");
        await editor.Awaiting(e => e.AddAsync("", RawMaterialCategoryType.Ingredient, Cancellation)).Should().ThrowAsync<BusinessRuleException>();
        (await Repository().GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_rename_keeps_the_categorys_kind()
    {
        var editor = CreateEditor();
        var added = await editor.AddAsync("Boxes", RawMaterialCategoryType.Packaging, Cancellation);

        await editor.RenameAsync(added, "Cake Boxes", Cancellation);

        (await Repository().GetAllAsync(Cancellation)).Single(c => c.Id == added.Id)
            .Should().Match<Domain.Entities.RawMaterialCategory>(c => c.Name == "Cake Boxes" && c.Type == RawMaterialCategoryType.Packaging);
    }

    private RawMaterialCategoryRepository Repository() => new(CreateDbContextFactory());

    private RawMaterialCategoryEditor CreateEditor() => new(Repository(), new RawMaterialCategoryValidator());
}
