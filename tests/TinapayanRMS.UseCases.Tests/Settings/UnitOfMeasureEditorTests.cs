using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Settings;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Settings;

/// <summary>
/// Settings' Units of Measure tab, through what every list tab shares (ILookupListEditor): added with its id, written
/// over its stored self, deleted, and refused with why before anything is written.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class UnitOfMeasureEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task A_unit_added_comes_back_with_its_id_and_reads_back()
    {
        var editor = CreateEditor();
        var added = new UnitOfMeasure { Name = "kilogram", Abbreviation = "kg" };

        await editor.AddAsync(added, Cancellation);

        added.Id.Should().BePositive();
        (await editor.GetAllAsync(Cancellation)).Should().ContainSingle(u => u.Id == added.Id && u.Name == "kilogram" && u.Abbreviation == "kg");
    }

    [Test]
    public async Task An_update_writes_over_the_stored_unit_and_a_delete_removes_it()
    {
        var editor = CreateEditor();
        var added = new UnitOfMeasure { Name = "kilogram", Abbreviation = "kg" };
        await editor.AddAsync(added, Cancellation);

        await editor.UpdateAsync(new UnitOfMeasure { Id = added.Id, Name = "kilogram", Abbreviation = "kilo" }, Cancellation);
        (await editor.GetAllAsync(Cancellation)).Should().ContainSingle().Which.Abbreviation.Should().Be("kilo");

        await editor.DeleteAsync(added.Id, Cancellation);
        (await editor.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_unit_the_list_cannot_take_is_refused_before_anything_is_written()
    {
        var editor = CreateEditor();
        var nameless = new UnitOfMeasure { Name = "", Abbreviation = "kg" };

        editor.Invoking(e => e.EnsureValid(nameless)).Should().Throw<BusinessRuleException>();
        await editor.Awaiting(e => e.AddAsync(nameless, Cancellation)).Should().ThrowAsync<BusinessRuleException>();
        (await editor.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    private UnitOfMeasureEditor CreateEditor() => new(new UnitOfMeasureRepository(CreateDbContextFactory()), new UnitOfMeasureValidator());
}
