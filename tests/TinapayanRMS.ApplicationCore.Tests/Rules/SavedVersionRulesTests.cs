using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class SavedVersionRulesTests
{
    private static readonly DateTime Opened = new(2026, 11, 18, 8, 0, 0);

    [Test]
    public void A_linked_ones_lines_stored_as_they_were_taken_save()
    {
        IReadOnlyList<RecipeLine> taken = [new(1, 1000m, IsFormulaBase: true), new(2, 650m)];

        var act = () => SavedVersionRules.EnsureLinesNotSavedSince("House Dough", storedLines: [new(1, 1000m, IsFormulaBase: true), new(2, 650m)], taken);

        act.Should().NotThrow();
    }

    [Test]
    public void A_linked_ones_lines_saved_since_they_were_taken_are_refused_by_name()
    {
        IReadOnlyList<RecipeLine> taken = [new(1, 1000m, IsFormulaBase: true), new(2, 650m)];

        var regrammed = () => SavedVersionRules.EnsureLinesNotSavedSince("House Dough", storedLines: [new(1, 1000m, IsFormulaBase: true), new(2, 600m)], taken);
        var reticked = () => SavedVersionRules.EnsureLinesNotSavedSince("House Dough", storedLines: [new(1, 1000m), new(2, 650m)], taken);

        regrammed.Should().Throw<SavedSinceException>().WithMessage(
            "House Dough's lines were saved after you opened this product, so your changes weren't saved. They're still on the form: "
            + "note them, press Revert to see what was saved, then make yours again.");
        reticked.Should().Throw<SavedSinceException>();
    }

    [Test]
    public void A_sub_assembly_whose_owner_changed_since_refuses_by_name_and_says_which_it_is()
    {
        var adopted = SavedVersionRules.OwnershipChanged(12, "Loaf Topping", isOwnNow: true);
        var released = SavedVersionRules.OwnershipChanged(12, "Loaf Topping", isOwnNow: false);

        adopted.Ownership.Should().Be(new OwnershipChange(12, "Loaf Topping", IsOwnNow: true));
        adopted.Message.Should().Be(
            "Loaf Topping became this product's own after you opened this product, so your changes weren't saved. They're still on "
            + "the form: note them, press Revert to see what was saved, then make yours again.");
        released.Message.Should().StartWith("Loaf Topping moved to the Sub-Assemblies screen after you opened this product");
    }

    [Test]
    public void A_sub_assembly_whose_owner_changed_shown_as_it_is_now_needs_only_another_Save()
    {
        var text = SavedVersionRules.OwnershipShownText(new OwnershipChange(12, "Loaf Topping", IsOwnNow: false));

        text.Should().Be(
            "Loaf Topping moved to the Sub-Assemblies screen after you opened this product, so your changes weren't saved. They're "
            + "still on the form, which shows Loaf Topping as it is now: press Save again.");
    }

    [Test]
    public void Lines_that_dont_say_what_they_were_taken_from_arent_checked()
    {
        var act = () => SavedVersionRules.EnsureLinesNotSavedSince("House Dough", storedLines: [new(1, 1000m)], linesTaken: null);

        act.Should().NotThrow();
    }

    [Test]
    public void A_record_saved_as_the_form_opened_it_saves()
    {
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: Opened, openedSavedAt: Opened);

        act.Should().NotThrow();
    }

    [Test]
    public void A_record_saved_since_the_form_opened_it_is_refused_by_name()
    {
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: Opened.AddMinutes(1), openedSavedAt: Opened);

        act.Should().Throw<SavedSinceException>().WithMessage(
            "Someone else saved Pandesal after you opened it, so your changes weren't saved. They're still on the form: note them, "
            + "open Pandesal again without saving to see what was saved, then make yours again.");
    }

    [Test]
    public void Any_other_saved_time_is_refused_an_earlier_one_too()
    {
        // A backup restored since puts an earlier time back: the form still doesn't show what's saved.
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: Opened.AddDays(-1), openedSavedAt: Opened);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void A_save_that_doesnt_say_what_it_opened_isnt_checked()
    {
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: Opened, openedSavedAt: null);

        act.Should().NotThrow("a new record, or a caller that isn't a form, has opened nothing");
    }

    [Test]
    public void A_record_opened_with_no_saved_time_saves_while_it_still_has_none()
    {
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: null, SavedVersionRules.OpenedAs(null));

        act.Should().NotThrow();
    }

    [Test]
    public void A_record_opened_with_no_saved_time_is_refused_once_a_save_has_stamped_one()
    {
        var act = () => SavedVersionRules.EnsureNotSavedSince("Pandesal", storedSavedAt: Opened, SavedVersionRules.OpenedAs(null));

        act.Should().Throw<SavedSinceException>();
    }

    // Each form holding a record with unsaved changes sends its check (a record view has a tab for each): a list's write
    // is checked on every record a form holds, and no other.
    [Test]
    public void A_list_write_is_checked_on_each_record_a_form_holds_with_changes_and_no_other()
    {
        SavedVersionCheck[] held = [new(7, Opened), new(9, Opened)];
        var savedSince = Opened.AddMinutes(1);

        var onOne = () => SavedVersionRules.EnsureNotSavedSince(held, 7, "Pandesal", savedSince);
        var onTheOther = () => SavedVersionRules.EnsureNotSavedSince(held, 9, "Monay", savedSince);
        var onAnother = () => SavedVersionRules.EnsureNotSavedSince(held, 8, "Ensaymada", savedSince);
        var withNothingTyped = () => SavedVersionRules.EnsureNotSavedSince([], 7, "Pandesal", savedSince);

        onOne.Should().Throw<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Pandesal"));
        onTheOther.Should().Throw<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Monay"));
        onAnother.Should().NotThrow();
        withNothingTyped.Should().NotThrow();
    }
}
