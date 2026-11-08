using System.Globalization;
using AwesomeAssertions;
using Syncfusion.Blazor.DropDowns;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// TextField, NumberField and PickField: what's typed reaches the ViewModel as it's typed, and the server never writes
/// into a box that has the cursor, since a redraw for an earlier keystroke would drop the ones typed since.
/// </summary>
public class FieldTests
{
    public FieldTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A screen's form as its boxes see it: which record it's for, and the undo steps they open and close.</summary>
    private sealed class Form : IUndoRecorder
    {
        public int FormVersion { get; set; }

        public bool IsEditPending { get; set; }

        public int Ended { get; private set; }

        public void BeginUndoStep()
        {
        }

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
        form.IsEditPending.Should().BeTrue();

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

        field.WaitForAssertion(() => drawn.Should().Equal("abc", "5.00"));
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
        var box = field.FindComponent<SfComboBox<int?, PickField<string>.Choice>>();
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
}
