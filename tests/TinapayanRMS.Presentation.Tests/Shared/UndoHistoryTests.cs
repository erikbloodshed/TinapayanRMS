using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Shared;

/// <summary>
/// The undo and redo stacks on their own, with a string standing in for a screen's form: no database and no
/// ViewModel, so what's tested here is the stack's own contract rather than any screen's use of it.
/// </summary>
public class UndoHistoryTests
{
    /// <summary>A form of one value, which the history captures and compares by its text.</summary>
    private sealed class Form
    {
        private string _value = string.Empty;

        /// <summary>
        /// Setting it refreshes the dirty flag, which is what a screen does: it watches its own property changes
        /// rather than waiting for the step around the gesture to settle, so the Save button follows what's typed.
        /// </summary>
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                History.RefreshDirty();
            }
        }

        public bool CanRecord { get; set; } = true;

        public UndoHistory<string> History { get; }

        public Form(int depth = 50) =>
            History = new UndoHistory<string>(() => Value, (a, b) => a == b, () => CanRecord, depth: depth);

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

    [Test]
    public void Each_step_hands_back_the_settled_form_as_it_stood_when_the_step_was_taken()
    {
        // As Products rebases its settled form when another section's form opens: the step moves it, and the undo
        // has to be able to put it back.
        var form = new Form();
        form.History.MarkClean();
        form.History.Begin();
        form.Value = "copy";
        form.History.Rebase((_, now) => now);
        form.History.End();

        form.History.TryUndo(out var restore, out var settledThen).Should().BeTrue();
        restore.Should().Be("");
        settledThen.Should().Be("", "the settled form before the step moved it");

        form.Value = restore;
        form.History.Rebase((_, _) => settledThen!);
        form.History.IsDirty.Should().BeFalse();

        form.History.TryRedo(out var redone, out var settledAtUndo).Should().BeTrue();
        redone.Should().Be("copy");
        settledAtUndo.Should().Be("copy", "the settled form as it stood when the undo stepped off it");
    }

    [Test]
    public void Nothing_to_undo_on_a_form_that_has_not_been_changed()
    {
        var form = new Form();

        form.History.CanUndo.Should().BeFalse();
        form.History.CanRedo.Should().BeFalse();
        form.Undo().Should().BeFalse();
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public void A_step_ended_with_a_rebase_is_measured_from_it_but_puts_back_where_it_began()
    {
        // The form's text stands for "the user's part:the part that isn't". Only a change to the
        // first part is a step, and undoing it puts back both, as they were when the step began.
        static string Rebase(string before, string now) => before.Split(':')[0] + ":" + now.Split(':')[1];

        var form = new Form();
        form.Value = "a:x";

        form.History.Begin();
        form.Value = "a:y";
        form.History.End(Rebase);
        form.History.CanUndo.Should().BeFalse("only the part that isn't the user's moved");

        form.History.Begin();
        form.Value = "b:z";
        form.History.End(Rebase);

        form.Undo().Should().BeTrue();
        form.Value.Should().Be("a:y");
    }

    [Test]
    public void RewriteAll_carries_a_change_saved_from_outside_the_form_into_every_step()
    {
        // As Products does for a product dragged into another category: the form's text stands for "name:category", and
        // the category was saved from outside the form. No undo or redo may bring the old one back; they move the name only.
        static string InCakes(string snapshot) => snapshot.Split(':')[0] + ":cakes";

        var form = new Form { Value = "a:breads" };
        form.History.MarkClean();
        form.Change("b:breads");
        form.Change("c:breads");
        form.Undo();

        form.Value = InCakes(form.Value);
        form.History.RewriteAll(InCakes);

        form.History.IsDirty.Should().BeTrue("the name still isn't what was saved");
        form.Undo().Should().BeTrue();
        form.Value.Should().Be("a:cakes");
        form.History.IsDirty.Should().BeFalse("the settled form took the new category too");

        form.Redo().Should().BeTrue();
        form.Value.Should().Be("b:cakes");
        form.Redo().Should().BeTrue();
        form.Value.Should().Be("c:cakes");
    }

    [Test]
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

    [Test]
    public void Nothing_is_recorded_while_the_screen_says_it_cannot()
    {
        var form = new Form { CanRecord = false };

        form.Change("one");

        form.History.CanUndo.Should().BeFalse("a load or a restore writing to the form isn't the user's change");
    }

    [Test]
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

    [Test]
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

    [Test]
    public void Nothing_to_save_until_the_form_has_been_settled_and_then_changed()
    {
        var form = new Form();
        form.History.IsDirty.Should().BeFalse("a form still being loaded holds nothing of the user's");

        form.History.MarkClean();
        form.History.IsDirty.Should().BeFalse();

        form.Change("one");
        form.History.IsDirty.Should().BeTrue();
    }

    [Test]
    public void A_form_typed_back_to_what_it_was_settled_with_has_nothing_to_save()
    {
        var form = new Form();
        form.History.MarkClean();
        form.Change("one");

        form.Change("");

        form.History.IsDirty.Should().BeFalse("it shows what it was settled with, however it got there");
        form.History.CanUndo.Should().BeTrue("both changes are still steps to take back");
    }

    [Test]
    public void Stepping_back_to_the_settled_form_leaves_nothing_to_save()
    {
        var form = new Form();
        form.History.MarkClean();
        form.Change("one");
        form.Change("two");

        form.Undo();
        form.History.IsDirty.Should().BeTrue("it's back at 'one', which was never saved");

        form.Undo();
        form.History.IsDirty.Should().BeFalse();
    }

    [Test]
    public void What_a_save_wrote_becomes_the_form_there_is_nothing_to_save_against()
    {
        var form = new Form();
        form.History.MarkClean();
        form.Change("one");

        // A Save throws the history away, then settles the form on what it wrote.
        form.History.Clear();
        form.History.MarkClean();

        form.History.IsDirty.Should().BeFalse();
        form.Change("two");
        form.History.IsDirty.Should().BeTrue();
        form.Change("one");
        form.History.IsDirty.Should().BeFalse("'one' is what was saved");
    }

    [Test]
    public void A_form_being_loaded_has_nothing_to_save_however_far_it_has_got()
    {
        var form = new Form();
        form.History.MarkClean();
        form.Change("one");

        // Another record picked: the history goes, and the form is written to before it settles.
        form.History.Clear();
        form.Value = "half-loaded";

        form.History.IsDirty.Should().BeFalse("nothing is the user's until the screen says the form has landed");
    }

    [Test]
    public void An_edit_the_form_has_not_seen_yet_keeps_Save_offered()
    {
        var form = new Form();
        form.History.MarkClean();

        // A cell opened for editing: whatever is typed there won't reach the form until the edit ends.
        form.History.IsEditPending = true;
        form.History.IsDirty.Should().BeTrue("the click that saves is the click that ends the edit");

        // It ended with nothing typed.
        form.History.IsEditPending = false;
        form.History.IsDirty.Should().BeFalse();
    }

    [Test]
    public void IsDirty_tells_the_Save_button_when_it_changes()
    {
        var form = new Form();
        form.History.MarkClean();
        List<string> raised = [];
        form.History.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        form.Change("one");
        raised.Should().Contain(nameof(UndoHistory<string>.IsDirty));

        raised.Clear();
        form.Change("");
        raised.Should().Contain(nameof(UndoHistory<string>.IsDirty));
    }

    [Test]
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
