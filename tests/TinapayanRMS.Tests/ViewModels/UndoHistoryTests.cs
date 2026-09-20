using AwesomeAssertions;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// The undo and redo stacks on their own, with a string standing in for a screen's form: no database and no
/// ViewModel, so what's tested here is the stack's own contract rather than any screen's use of it.
/// </summary>
public class UndoHistoryTests
{
    public UndoHistoryTests() => ReactiveUIBootstrapper.EnsureInitialized();

    /// <summary>A form of one value, which the history captures and compares by its text.</summary>
    private sealed class Form
    {
        public string Value { get; set; } = string.Empty;

        public bool CanRecord { get; set; } = true;

        public UndoHistory<string> History { get; }

        public Form(int depth = 50) =>
            History = new UndoHistory<string>(() => Value, (a, b) => a == b, () => CanRecord, depth);

        /// <summary>One finished change, as a screen's Begin/End bracketing of a gesture would make it.</summary>
        public void Change(string to)
        {
            History.Begin();
            Value = to;
            History.End();
        }

        public bool Undo()
        {
            History.End();
            if (!History.TryUndo(out var restore))
            {
                return false;
            }

            Value = restore;
            return true;
        }

        public bool Redo()
        {
            History.End();
            if (!History.TryRedo(out var restore))
            {
                return false;
            }

            Value = restore;
            return true;
        }
    }

    [Fact]
    public void Nothing_to_undo_on_a_form_that_has_not_been_changed()
    {
        var form = new Form();

        form.History.CanUndo.Should().BeFalse();
        form.History.CanRedo.Should().BeFalse();
        form.Undo().Should().BeFalse();
    }

    [Fact]
    public void Undo_puts_back_the_value_before_the_last_change()
    {
        var form = new Form();
        form.Change("one");
        form.Change("two");

        form.Undo().Should().BeTrue();
        form.Value.Should().Be("one");
        form.Undo().Should().BeTrue();
        form.Value.Should().Be("");
        form.Undo().Should().BeFalse("there was nothing before that");
    }

    [Fact]
    public void Redo_returns_exactly_what_the_undo_stepped_off()
    {
        var form = new Form();
        form.Change("one");
        form.Change("two");
        form.Undo();

        form.Redo().Should().BeTrue();
        form.Value.Should().Be("two");
        form.Redo().Should().BeFalse();
    }

    [Fact]
    public void A_change_after_an_undo_leaves_nothing_to_redo()
    {
        var form = new Form();
        form.Change("one");
        form.Change("two");
        form.Undo();
        form.History.CanRedo.Should().BeTrue();

        form.Change("three");

        form.History.CanRedo.Should().BeFalse("a change made after stepping back is a new branch");
        form.Redo().Should().BeFalse();
    }

    [Fact]
    public void A_step_that_changes_nothing_is_not_recorded()
    {
        var form = new Form();
        form.Change("one");

        // Opened and closed with nothing typed, as a cell clicked into and left alone would be.
        form.History.Begin();
        form.History.End();

        // Retyped to the value it already had.
        form.Change("one");

        form.Undo().Should().BeTrue();
        form.Value.Should().Be("", "only the one real change was ever recorded");
        form.History.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Opening_a_step_settles_the_one_already_open()
    {
        var form = new Form();

        // Two gestures where only the first's close is seen, which is how an add row behaves: the grid
        // raises nothing when the add commits, so the next step opened has to settle it.
        form.History.Begin();
        form.Value = "one";
        form.History.Begin();
        form.Value = "two";
        form.History.End();

        form.Undo().Should().BeTrue();
        form.Value.Should().Be("one");
        form.Undo().Should().BeTrue();
        form.Value.Should().Be("");
    }

    [Fact]
    public void Nothing_is_recorded_while_the_screen_says_it_cannot()
    {
        var form = new Form { CanRecord = false };

        form.Change("one");

        form.History.CanUndo.Should().BeFalse("a load or a restore writing to the form isn't the user's change");
    }

    [Fact]
    public void The_oldest_step_goes_once_the_depth_is_passed()
    {
        var form = new Form(depth: 3);
        foreach (var value in new[] { "one", "two", "three", "four" })
        {
            form.Change(value);
        }

        // Four changes, three kept: stepping back three times reaches "one", not "".
        form.Undo();
        form.Undo();
        form.Undo();
        form.Value.Should().Be("one");
        form.History.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Clear_throws_both_stacks_away()
    {
        var form = new Form();
        form.Change("one");
        form.Change("two");
        form.Undo();

        form.History.Clear();

        form.History.CanUndo.Should().BeFalse();
        form.History.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void CanUndo_and_CanRedo_tell_the_buttons_when_they_change()
    {
        var form = new Form();
        List<string> raised = [];
        form.History.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        form.Change("one");
        raised.Should().Contain(nameof(UndoHistory<string>.CanUndo));

        raised.Clear();
        form.Undo();
        raised.Should().Contain(nameof(UndoHistory<string>.CanUndo)).And.Contain(nameof(UndoHistory<string>.CanRedo));
    }
}
