using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>
/// What Settings' list tabs share (LookupListState), through Units of Measure: a pick loads the form, Save adds a new
/// item or changes the picked one and then clears the form, a load keeps the pick and the typing, and Delete asks first.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class LookupListStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private UnitsOfMeasureState CreateState(bool confirm = true, List<string>? asked = null)
    {
        var state = new UnitsOfMeasureState(
            new UnitOfMeasureRepository(CreateDbContextFactory()), new UnitOfMeasureValidator(), new LoggerConfiguration().CreateLogger());
        state.ConfirmDelete.RegisterHandler(context =>
        {
            asked?.Add(context.Input);
            context.SetOutput(confirm);
        });
        return state;
    }

    private static async Task AddAsync(UnitsOfMeasureState state, string name, string abbreviation)
    {
        await state.NewCommand.ExecuteAsync();
        state.EditingName = name;
        state.EditingAbbreviation = abbreviation;
        await state.SaveCommand.ExecuteAsync();
    }

    [Test]
    public Task Save_adds_a_new_one_then_clears_the_form_for_the_next() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.FormTitle.Should().Be("New Unit");

        await AddAsync(state, "kilogram", "kg");
        await AddAsync(state, "gram", "g");

        state.ErrorMessage.Should().BeNull();
        state.Items.Select(u => (u.Name, u.Abbreviation)).Should().Equal(("gram", "g"), ("kilogram", "kg"));
        state.SelectedItem.Should().BeNull();
        state.EditingName.Should().BeEmpty();
        state.EditingAbbreviation.Should().BeEmpty();
        state.FormTitle.Should().Be("New Unit");
    });

    [Test]
    public Task A_pick_loads_the_form_and_Save_changes_that_one() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await AddAsync(state, "kilogram", "kg");

        state.SelectedItem = state.Items.Single();
        state.FormTitle.Should().Be("Edit Unit");
        state.EditingName.Should().Be("kilogram");
        state.EditingAbbreviation.Should().Be("kg");

        state.EditingAbbreviation = "kilo";
        await state.SaveCommand.ExecuteAsync();

        state.Items.Should().ContainSingle().Which.Abbreviation.Should().Be("kilo");
        state.SelectedItem.Should().BeNull();
    });

    [Test]
    public Task A_load_keeps_the_pick_and_what_was_typed() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await AddAsync(state, "kilogram", "kg");
        state.SelectedItem = state.Items.Single();
        state.EditingName = "kilo";

        await state.LoadCommand.ExecuteAsync();

        state.SelectedItem.Should().BeSameAs(state.Items.Single());
        state.EditingName.Should().Be("kilo");
    });

    [Test]
    public Task A_form_the_validator_refuses_saves_nothing_and_says_why() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().NotBeNullOrEmpty();
        state.Items.Should().BeEmpty();
    });

    [Test]
    public Task Delete_asks_first_and_does_nothing_when_declined() => UiThread.RunAsync(async () =>
    {
        List<string> asked = [];
        var state = CreateState(confirm: false, asked);
        await state.LoadCommand.ExecuteAsync();
        await AddAsync(state, "kilogram", "kg");
        state.DeleteCommand.CanExecute().Should().BeFalse("nothing's picked");
        state.SelectedItem = state.Items.Single();

        await state.DeleteCommand.ExecuteAsync();

        asked.Should().Equal("Delete 'kilogram'? This can't be undone.");
        state.Items.Should().ContainSingle();
    });

    [Test]
    public Task Delete_confirmed_removes_it_and_clears_the_form() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await AddAsync(state, "kilogram", "kg");
        state.SelectedItem = state.Items.Single();

        await state.DeleteCommand.ExecuteAsync();

        state.Items.Should().BeEmpty();
        state.SelectedItem.Should().BeNull();
        state.EditingName.Should().BeEmpty();
    });
}
