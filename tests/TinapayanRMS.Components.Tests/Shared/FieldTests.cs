using System.Globalization;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Syncfusion.Blazor.DropDowns;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// TextField, NumberField and PickField: what's typed reaches the screen state as it's typed, and the server never writes
/// into a box that has the cursor, since a redraw for an earlier keystroke would drop the ones typed since.
/// </summary>
public class FieldTests
{
    [Test]
    public async Task A_change_with_nothing_typed_leaves_the_number_its_rounded_figure_stands_for()
    {
        // As the Vim keys' Esc raises one in a Baker's % cell showing 13.3 for 13.3333…%.
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters
            .Add(f => f.Value, 13.333333m)
            .Add(f => f.Format, "N1")
            .Add(f => f.ValueChanged, typed.Add));

        field.Find("input").Focus();
        field.Find("input").Change("13.3");
        field.Find("input").Blur();

        typed.Should().BeEmpty("nothing was typed");
    }

    [Test]
    public async Task The_figure_shown_typed_into_a_box_that_lands_on_leave_is_taken()
    {
        // Only the change passes it on here, so it's the change that's tested: typed, even the figure it showed, it's taken.
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters
            .Add(f => f.Value, 13.333333m)
            .Add(f => f.Format, "N1")
            .Add(f => f.LandsOnLeave, true)
            .Add(f => f.ValueChanged, typed.Add));

        field.Find("input").Focus();
        field.Find("input").Input("13.3");
        typed.Should().BeEmpty("it lands only once it's left");
        field.Find("input").Change("13.3");

        typed.Should().Equal([13.3m]);
    }

    public FieldTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A screen's form as its boxes see it: which record it's for, and the undo steps they open and close.</summary>
    private sealed class Form : IUndoRecorder
    {
        public int FormVersion { get; set; }

        public bool IsEditPending { get; set; }

        public int Begun { get; private set; }

        public int Ended { get; private set; }

        public void BeginUndoStep() => Begun++;

        public void EndUndoStep() => Ended++;
    }

    [Test]
    public async Task A_text_box_shows_its_value_and_passes_on_each_keystroke()
    {
        await using var context = AppServices.NewContext();
        List<string> typed = [];
        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Value, "Pandesal")
            .Add(f => f.ValueChanged, typed.Add));

        field.Find("input").GetAttribute("value").Should().Be("Pandesal");

        field.Find("input").Input("Pandesal x");
        field.Find("input").Input("Pandesal x 8");

        typed.Should().Equal("Pandesal x", "Pandesal x 8");
    }

    [Test]
    public async Task A_password_box_hides_its_characters_and_a_text_box_doesnt()
    {
        await using var context = AppServices.NewContext();

        var password = context.Render<TextField>(parameters => parameters.Add(f => f.Password, true));
        var text = context.Render<TextField>();

        password.Find("input").GetAttribute("type").Should().Be("password");
        text.Find("input").GetAttribute("type").Should().Be("text");
    }

    [Test]
    public async Task A_text_box_with_the_cursor_isnt_written_to_until_its_left()
    {
        await using var context = AppServices.NewContext();
        var field = context.Render<TextField>(parameters => parameters.Add(f => f.Value, "Pandesal"));

        field.Find("input").Focus();
        field.Render(parameters => parameters.Add(f => f.Value, "Pandesal x 8"));

        field.Find("input").GetAttribute("value").Should().Be("Pandesal", "the box already shows what's being typed");

        field.Find("input").Blur();

        field.Find("input").GetAttribute("value").Should().Be("Pandesal x 8");
    }

    [Test]
    public async Task Leaving_a_text_box_raises_OnLeave()
    {
        await using var context = AppServices.NewContext();
        var left = 0;
        var field = context.Render<TextField>(parameters => parameters.Add(f => f.OnLeave, () => left++));

        field.Find("input").Focus();
        field.Find("input").Blur();

        left.Should().Be(1);
    }

    [Test]
    public async Task A_text_box_whose_form_becomes_another_records_under_the_cursor_takes_nothing_more_until_the_cursor_returns()
    {
        await using var context = AppServices.NewContext();
        var form = new Form();
        List<string> typed = [];
        var left = 0;
        var field = context.Render<TextField>(parameters => parameters
            .AddCascadingValue<IUndoRecorder>(form)
            .Add(f => f.Value, "")
            .Add(f => f.ValueChanged, typed.Add)
            .Add(f => f.OnLeave, () => left++));
        field.Find("input").Focus();
        field.Find("input").Input("Mus");

        // New, then the browser's Back: the material from before loads while the Name box has the cursor. A key typed
        // meanwhile is on its way, and it was saved as the material's name.
        form.FormVersion++;
        field.Find("input").Input("Musc");
        field.Render(parameters => parameters.Add(f => f.Value, "First Class Flour"));

        typed.Should().Equal("Mus");
        field.Find("input").GetAttribute("value").Should().Be("First Class Flour", "a fresh box is drawn with the record's own text");

        // The box it replaced is left on its way out, which is no visit to this record's box.
        field.Find("input").Blur();
        left.Should().Be(0);
        form.Ended.Should().Be(0);

        field.Find("input").Focus();
        field.Find("input").Input("First Class Flour, sifted");
        field.Find("input").Blur();

        typed.Should().Equal("Mus", "First Class Flour, sifted");
        left.Should().Be(1);
    }

    [Test]
    public async Task A_number_box_whose_form_becomes_another_records_under_the_cursor_takes_nothing_more_until_the_cursor_returns()
    {
        await using var context = AppServices.NewContext();
        var form = new Form();
        List<decimal> typed = [];
        var field = context.Render<NumberField<decimal>>(parameters => parameters
            .AddCascadingValue<IUndoRecorder>(form)
            .Add(f => f.Value, 0m)
            .Add(f => f.LandsOnLeave, true)
            .Add(f => f.ValueChanged, typed.Add));
        field.Find("input").Focus();
        form.IsEditPending.Should().BeFalse("nothing is typed yet");
        field.Find("input").Input("4");
        form.IsEditPending.Should().BeTrue("what's typed lands only once the box is left");

        form.FormVersion++;
        field.Find("input").Input("45");
        field.Find("input").Change("45");
        field.Render(parameters => parameters.Add(f => f.Value, 37m));

        typed.Should().BeEmpty();
        form.IsEditPending.Should().BeFalse("nothing is waiting to land in this record's box");
        field.Find("input").GetAttribute("value").Should().Be("37.00");

        field.Find("input").Focus();
        field.Find("input").Change("45");

        typed.Should().Equal(45m);
    }

    [Test]
    public async Task A_box_keeps_the_cursor_while_its_form_stays_the_same_records()
    {
        await using var context = AppServices.NewContext();
        var form = new Form();
        List<string> typed = [];
        var field = context.Render<TextField>(parameters => parameters
            .AddCascadingValue<IUndoRecorder>(form)
            .Add(f => f.Value, "Flour")
            .Add(f => f.ValueChanged, typed.Add));
        field.Find("input").Focus();

        // A redraw for something else, as each keystroke brings: the box is the same record's, and isn't written to.
        field.Find("input").Input("Flour, s");
        field.Render(parameters => parameters.Add(f => f.Value, "Flour, s"));
        field.Find("input").Input("Flour, sifted");

        typed.Should().Equal("Flour, s", "Flour, sifted");
        field.Find("input").GetAttribute("value").Should().Be("Flour");
    }

    [Test]
    [Arguments("N2", "1,234.50")]
    [Arguments("N0", "1,235")]
    [Arguments("#,0.0###", "1,234.5")]
    public async Task A_number_box_shows_its_number_in_its_format(string format, string shown)
    {
        await using var context = AppServices.NewContext();

        var field = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Value, 1234.5m).Add(f => f.Format, format));

        field.Find("input").GetAttribute("value").Should().Be(shown);
    }

    [Test]
    public async Task A_number_box_tells_the_page_whether_a_point_and_a_minus_may_be_typed()
    {
        // The page's script (app.js) refuses any other key before it shows: a point in a count's box, a minus where the
        // number can't go below zero.
        await using var context = AppServices.NewContext();

        var amount = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.Min, 0m)).Find("input");
        var count = context.Render<NumberField<int>>(parameters => parameters.Add(f => f.Min, 1m).Add(f => f.Format, "N0")).Find("input");
        var change = context.Render<NumberField<decimal?>>().Find("input");
        var below = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Min, -100m)).Find("input");

        (amount.HasAttribute("data-whole"), amount.HasAttribute("data-signed")).Should().Be((false, false));
        (count.HasAttribute("data-whole"), count.HasAttribute("data-signed")).Should().Be((true, false));
        (change.HasAttribute("data-whole"), change.HasAttribute("data-signed")).Should().Be((false, true), "with no Min it may go below zero");
        below.HasAttribute("data-signed").Should().BeTrue();
    }

    [Test]
    public async Task A_number_box_bound_to_a_double_or_a_float_is_refused()
    {
        await using var context = AppServices.NewContext();

        var toDouble = () => context.Render<NumberField<double>>();
        var toFloat = () => context.Render<NumberField<float?>>();

        toDouble.Should().Throw<InvalidOperationException>().WithMessage("*not a Double*");
        toFloat.Should().Throw<InvalidOperationException>().WithMessage("*not a Nullable*");
    }

    [Test]
    public async Task A_number_passes_on_as_its_typed_once_it_reads_as_a_number()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.ValueChanged, typed.Add));

        // "2." and "2.50" would read back differently once tidied, so they wait for the box to be left.
        foreach (var text in new[] { "2", "2.", "2.5", "2.50", "1,250" })
        {
            field.Find("input").Input(text);
        }

        typed.Should().Equal(2m, 2.5m, 1250m);
    }

    [Test]
    public async Task What_isnt_a_number_is_never_passed_on()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.Value, 5m).Add(f => f.ValueChanged, typed.Add));

        field.Find("input").Input("abc");
        field.Find("input").Change("abc");

        typed.Should().BeEmpty();
    }

    [Test]
    public async Task An_emptied_box_is_null_where_the_number_can_be_and_zero_where_it_cant()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> optional = [];
        List<decimal> required = [];
        var optionalField = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.Value, 5m).Add(f => f.ValueChanged, optional.Add));
        var requiredField = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Value, 5m).Add(f => f.ValueChanged, required.Add));

        optionalField.Find("input").Change("");
        requiredField.Find("input").Change("");

        optional.Should().Equal([null]);
        required.Should().Equal(0m);
    }

    [Test]
    public async Task A_number_is_kept_between_its_Min_and_Max()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters
            .Add(f => f.Min, 0m)
            .Add(f => f.Max, 99.99m)
            .Add(f => f.ValueChanged, typed.Add));

        // Past the limit, nothing passes on while typing; leaving the box lands the nearest it allows.
        field.Find("input").Input("150");
        typed.Should().BeEmpty();

        field.Find("input").Change("150");
        typed.Should().Equal(99.99m);
    }

    [Test]
    public async Task A_number_beyond_the_largest_a_box_takes_is_never_passed_on()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        List<int> whole = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.Value, 5m).Add(f => f.ValueChanged, typed.Add));
        var wholeField = context.Render<NumberField<int>>(parameters => parameters.Add(f => f.Value, 5).Add(f => f.Format, "N0").Add(f => f.ValueChanged, whole.Add));

        // A held key. Its first nine digits are a number; the tenth on isn't one the costs can be worked out from, and
        // 21 of them took a recipe's cost per piece past what a decimal holds, which broke the page.
        foreach (var text in new[] { "888888888", "8888888888", "888888888888888888888", "-888888888888888888888" })
        {
            field.Find("input").Input(text);
            wholeField.Find("input").Input(text);
        }

        field.Find("input").Change("888888888888888888888");
        wholeField.Find("input").Change("888888888888888888888");

        typed.Should().Equal(888888888m);
        whole.Should().Equal(888888888);
        NumberField<int>.Largest.Should().BeLessThan(int.MaxValue, "a whole number box converts what it takes to an int");
    }

    [Test]
    public async Task A_box_left_holding_what_it_didnt_take_is_drawn_as_typed_and_then_as_its_number()
    {
        await using var context = AppServices.NewContext();
        var field = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Value, 5m));
        List<string?> drawn = [];
        field.OnAfterRender += (_, _) => drawn.Add(field.Find("input").GetAttribute("value"));

        // The browser's box holds "abc" and the screen's copy of it "5.00", the very text to show again. Drawn once,
        // nothing would have changed, nothing would be sent, and the box would go on showing "abc".
        field.Find("input").Focus();
        field.Find("input").Input("abc");
        field.Find("input").Change("abc");
        drawn.Clear();
        field.Find("input").Blur();

        await field.WaitForAssertionAsync(() => drawn.Should().Equal("abc", "5.00"));
    }

    [Test]
    public async Task A_whole_number_box_passes_on_whole_numbers()
    {
        await using var context = AppServices.NewContext();
        List<int> typed = [];
        var field = context.Render<NumberField<int>>(parameters => parameters.Add(f => f.Value, 1).Add(f => f.Format, "N0").Add(f => f.ValueChanged, typed.Add));

        field.Find("input").Input("8");

        typed.Should().Equal(8);
    }

    [Test]
    public async Task A_box_that_lands_on_leave_passes_its_number_only_once_left()
    {
        await using var context = AppServices.NewContext();
        List<decimal?> typed = [];
        var field = context.Render<NumberField<decimal?>>(parameters => parameters.Add(f => f.LandsOnLeave, true).Add(f => f.ValueChanged, typed.Add));

        field.Find("input").Input("12");
        typed.Should().BeEmpty();

        field.Find("input").Change("12");
        typed.Should().Equal(12m);
    }

    [Test]
    public async Task A_number_box_with_the_cursor_isnt_written_to_until_its_left()
    {
        await using var context = AppServices.NewContext();
        var field = context.Render<NumberField<decimal>>(parameters => parameters.Add(f => f.Value, 450m).Add(f => f.Format, "N1"));

        field.Find("input").Focus();
        field.Render(parameters => parameters.Add(f => f.Value, 460m));

        field.Find("input").GetAttribute("value").Should().Be("450.0");

        field.Find("input").Blur();

        field.Find("input").GetAttribute("value").Should().Be("460.0");
    }

    [Test]
    public async Task A_picker_with_the_cursor_isnt_given_back_a_pick_it_refused_until_its_left()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Flour", "Salt", "Water"])
            .Add(f => f.Value, "Salt")
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();
        box.Instance.Value.Should().Be(1);

        // The text rubbed out: the combo box has no pick, and a line's ingredient can't be left without one.
        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));

        picked.Should().BeEmpty("the pick stays as it was");
        box.Instance.Value.Should().BeNull("the old pick written back into the box under the cursor would undo the rubbing out");

        // Something picked as it's typed still passes on at once.
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(2));
        picked.Should().Equal("Water");

        // Refused again, then left: the box shows the pick again.
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));
        field.Render(parameters => parameters.Add(f => f.Value, "Water"));
        box.Instance.Value.Should().BeNull();
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        box.Instance.Value.Should().Be(2);
    }

    [Test]
    public async Task A_picker_lists_the_best_match_first_and_leaving_it_picks_that_without_the_rest_written_into_the_box()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Unsalted Butter", "Flour", "Sea Salt"])
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();
        box.Instance.Autofill.Should().BeFalse("the rest of a name written into the box under the cursor was in the way");

        // A new line: nothing picked yet, so the combo box, which takes only an exact name as it's left, says nothing.
        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("salt")));

        // The combo box lists what holds the text, sorted by its rank.
        box.Instance.DataSource.Where(c => c.Text.Contains("salt", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Rank)
            .Select(c => c.Text).Should().Equal("Sea Salt", "Unsalted Butter");
        picked.Should().BeEmpty("nothing changes as letters are typed");

        // Left with the text typed: the combo box says so (CustomValueAsync), then that it was left.
        box.Instance.LeftWithTypedText = true;
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        picked.Should().Equal("Sea Salt");
    }

    [Test]
    public async Task A_picker_that_can_be_left_empty_keeps_the_highlighted_item_the_combo_box_then_answers_no_pick_to()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Margarine", "Refrigerated Margarine", "Water"])
            .Add(f => f.Value, "Water")
            .Add(f => f.AllowEmpty, true)
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();

        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("marg")));

        // Tab: no item is named "marg", so the highlighted one is taken; the combo box, still showing "marg", answers no
        // pick again before it says it was left.
        box.Instance.LeftWithTypedText = true;
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        picked.Should().Equal(["Margarine"], "a Category was emptied by the second answer");
    }

    [Test]
    public async Task Escape_then_leaving_keeps_the_pick()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["All Purpose Cream", "All Purpose Flour"])
            .Add(f => f.Value, "All Purpose Flour")
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();

        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("purpose")));
        box.Instance.BestMatch!.Text.Should().Be("All Purpose Cream");

        // Escape: the combo box answers with no pick of its own accord, then, left, settles no typed text.
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        picked.Should().BeEmpty();
    }

    [Test]
    public async Task Enter_or_tab_on_text_naming_no_item_exactly_picks_the_item_the_arrow_keys_moved_to()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Bread Flour", "Cake Flour", "Salt"])
            .Add(f => f.Value, "Cake Flour")
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();

        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("flour")));
        var bread = box.Instance.DataSource.First(c => c.Text == "Bread Flour");
        await field.InvokeAsync(() => events.Instance.OnValueSelect.InvokeAsync(Selected(bread)));

        // Tab: the combo box settles the typed text (CustomValueAsync), finds no item named "flour", and answers with no
        // pick.
        box.Instance.LeftWithTypedText = true;
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(null));

        picked.Should().Equal("Bread Flour");

        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));
        picked.Should().Equal(["Bread Flour"], "it's picked once");
    }

    [Test]
    public async Task A_picker_rubbed_out_and_left_picks_nothing()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Flour", "Salt"])
            .Add(f => f.Value, "Salt")
            .Add(f => f.ValueChanged, picked.Add));
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();

        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("fl")));
        await field.InvokeAsync(() => events.Instance.Filtering.InvokeAsync(Typed("")));
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        picked.Should().BeEmpty("the line keeps its pick");
    }

    [Test]
    public async Task A_value_the_combo_box_hands_back_without_the_cursor_is_no_pick()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["gram", "bag"])
            .Add(f => f.Value, "bag")
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();

        // Another record picked: its list comes with the base unit showing, then its own unit, as a Raw Materials pick's
        // Buffered Cost "Per" does as the details arrive.
        field.Render(parameters => parameters.Add(f => f.Items, ["gram", "tray"]).Add(f => f.Value, "gram"));
        field.Render(parameters => parameters.Add(f => f.Value, "tray"));

        // The combo box, settling on the list it was given, hands back the place it was given with it: nobody picked.
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(0));

        picked.Should().BeEmpty("the box didn't have the cursor");
        box.Instance.Value.Should().Be(1, "the box shows the screen state's pick");
    }

    [Test]
    public async Task A_picker_whose_list_is_rebuilt_under_the_cursor_keeps_showing_the_item_it_picked()
    {
        await using var context = AppServices.NewContext();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Bread", "Pastry"])
            .Add(f => f.Value, "Pastry")
            .Add(f => f.ValueChanged, picked.Add));
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();

        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));

        // A category added in its sorted place while the box has the cursor moves Pastry from 1 to 2.
        field.Render(parameters => parameters.Add(f => f.Items, ["Bread", "Cake", "Pastry"]));

        box.Instance.Value.Should().Be(2, "the box still shows Pastry, not Cake, which took its old place");
        picked.Should().BeEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_picker_takes_the_cursor_and_opens_its_list_as_it_shows_only_when_it_opens_on_show(bool openOnShow)
    {
        await using var context = AppServices.NewContext();

        // The Packaging tab's Link and Copy picker shows in answer to the button: once the combo box is made, its script
        // set up, it's given the cursor and its list. Any other picker waits to be clicked.
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Gift Box Set", "Cream Loaf"])
            .Add(f => f.OpenOnShow, openOnShow));

        context.JSInterop.Invocations.Any(i => i.Identifier == "sfBlazor.DropDownList.focusIn").Should().Be(openOnShow);
        field.FindAll("li.e-list-item").Select(li => li.TextContent.Trim()).Should().Equal(openOnShow ? ["Gift Box Set", "Cream Loaf"] : []);
    }

    [Test]
    public async Task A_deferred_picker_is_its_look_alike_until_it_has_the_cursor_and_the_combo_box_only_while_it_has()
    {
        await using var context = AppServices.NewContext();
        context.JSInterop.Setup<bool>("tinapayan.handOverPick", _ => true).SetResult(true);
        var form = new Form();
        List<string?> picked = [];
        var field = context.Render<PickField<string>>(parameters => parameters
            .AddCascadingValue<IUndoRecorder>(form)
            .Add(f => f.Items, ["Flour", "Salt", "Water"])
            .Add(f => f.Value, "Salt")
            .Add(f => f.Deferred, true)
            .Add(f => f.ValueChanged, picked.Add));

        // A recipe line as it's drawn: the item's name in the look-alike, and no combo box setting up its list.
        field.FindComponents<PickComboBox<string>>().Should().BeEmpty();
        field.Find(".deferred-pick input").GetAttribute("value").Should().Be("Salt");

        // The look-alike takes the cursor: the combo box is made, and once it's set up, the cursor is handed to it.
        field.Find(".deferred-pick input").Focus();
        await field.WaitForAssertionAsync(() => field.FindAll(".deferred-pick").Should().BeEmpty());
        var handOver = context.JSInterop.Invocations.Single(i => i.Identifier == "tinapayan.handOverPick");
        handOver.Arguments[1].Should().Be(false, "the arrow wasn't pressed");
        form.Begun.Should().Be(0, "the look-alike's cursor is no visit to the picker yet");

        // The combo box, given the cursor, is the picker as ever: a pick passes on.
        var box = field.FindComponent<PickComboBox<string>>();
        var events = field.FindComponent<ComboBoxEvents<int?, PickField<string>.Choice>>();
        box.Instance.Value.Should().Be(1);
        await field.InvokeAsync(() => events.Instance.Focus.InvokeAsync(new object()));
        await field.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(2));
        picked.Should().Equal("Water");
        form.Begun.Should().Be(1);

        // Left: the look-alike takes its place again, with the line's pick.
        field.Render(parameters => parameters.Add(f => f.Value, "Water"));
        await field.InvokeAsync(() => events.Instance.Blur.InvokeAsync(new object()));

        field.FindComponents<PickComboBox<string>>().Should().BeEmpty();
        field.Find(".deferred-pick input").GetAttribute("value").Should().Be("Water");
        form.Ended.Should().Be(1);
    }

    [Test]
    public async Task A_deferred_pickers_arrow_opens_its_list_once_the_combo_box_has_the_cursor()
    {
        await using var context = AppServices.NewContext();
        context.JSInterop.Setup<bool>("tinapayan.handOverPick", _ => true).SetResult(true);
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Flour", "Salt", "Water"])
            .Add(f => f.Value, "Salt")
            .Add(f => f.Deferred, true));

        field.Find(".deferred-pick .e-ddl-icon").MouseDown();

        await field.WaitForAssertionAsync(() => field.FindAll("li.e-list-item").Select(li => li.TextContent.Trim()).Should().Equal("Flour", "Salt", "Water"));
        context.JSInterop.Invocations.Single(i => i.Identifier == "tinapayan.handOverPick").Arguments[1].Should().Be(true);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_list_a_picker_opens_itself_closes_on_escape(bool deferred)
    {
        await using var context = AppServices.NewContext();
        context.JSInterop.Setup<bool>("tinapayan.handOverPick", _ => true).SetResult(true);

        // A deferred picker's arrow, or a picker that opens on show (the Packaging tab's Link and Copy): the combo box
        // opened by the picker, not by a press on its own arrow.
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Flour", "Salt", "Water"])
            .Add(f => f.Deferred, deferred)
            .Add(f => f.OpenOnShow, !deferred));
        if (deferred)
        {
            field.Find(".deferred-pick .e-ddl-icon").MouseDown();
        }

        await field.WaitForAssertionAsync(() => field.FindAll("li.e-list-item").Should().HaveCount(3));

        // Escape, as the combo box's script hands it on, closes it, as it does a list opened by a press on the combo box's
        // own arrow: Syncfusion closes only a list it has marked open.
        var box = field.FindComponent<PickComboBox<string>>();
        await field.InvokeAsync(() => box.Instance.KeyActionHandler(new KeyActions { Action = "escape", Key = "Escape", Type = "keydown", Events = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" } }));

        await field.WaitForAssertionAsync(() => field.FindAll(".e-popup-open li.e-list-item").Should().BeEmpty());
    }

    [Test]
    public async Task A_deferred_picker_whose_cursor_went_on_before_its_combo_box_was_set_up_stays_its_look_alike()
    {
        await using var context = AppServices.NewContext();

        // Tab, Tab: the look-alike had the cursor and lost it before the combo box was ready to take it.
        context.JSInterop.Setup<bool>("tinapayan.handOverPick", _ => true).SetResult(false);
        var field = context.Render<PickField<string>>(parameters => parameters
            .Add(f => f.Items, ["Flour", "Salt", "Water"])
            .Add(f => f.Value, "Salt")
            .Add(f => f.Deferred, true));

        field.Find(".deferred-pick input").Focus();

        await field.WaitForAssertionAsync(() => context.JSInterop.Invocations.Any(i => i.Identifier == "tinapayan.handOverPick").Should().BeTrue());
        await field.WaitForAssertionAsync(() => field.FindComponents<PickComboBox<string>>().Should().BeEmpty());
        field.Find(".deferred-pick input").GetAttribute("value").Should().Be("Salt");
    }

    // The combo box's event arguments, as it makes them: their setters are Syncfusion's own.
    private static FilteringEventArgs Typed(string text)
    {
        var args = new FilteringEventArgs();
        SetText(args, text);
        return args;
    }

    private static SelectEventArgs<PickField<string>.Choice> Selected(PickField<string>.Choice choice)
    {
        var args = new SelectEventArgs<PickField<string>.Choice>();
        SelectArgs<PickField<string>.Choice>.SetItemData(args, choice);
        return args;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Text")]
    private static extern void SetText(FilteringEventArgs args, string text);

    // An accessor on a generic type is reached through a generic class of its own.
    private static class SelectArgs<T>
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ItemData")]
        public static extern void SetItemData(SelectEventArgs<T> args, T item);
    }
}
