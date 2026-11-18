using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class SavedVersionRulesTests
{
    private static readonly DateTime Opened = new(2026, 11, 18, 8, 0, 0);

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
}
