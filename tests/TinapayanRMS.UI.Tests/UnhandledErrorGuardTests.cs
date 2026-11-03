using AwesomeAssertions;
using ReactiveUI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests;

/// <summary>
/// When the app carries on after an exception nothing else caught (App's handler), and when it's let close: the same
/// failure coming round again at once would otherwise show a message after a message.
/// </summary>
public class UnhandledErrorGuardTests
{
    private static readonly DateTime Start = new(2026, 10, 3, 9, 0, 0);

    [Fact]
    public void The_app_carries_on_after_a_few_failures()
    {
        var guard = new UnhandledErrorGuard();

        for (var i = 0; i < UnhandledErrorGuard.MaxFailures; i++)
        {
            guard.Admit(Start.AddSeconds(i)).Should().BeTrue();
        }
    }

    [Fact]
    public void One_failure_too_many_in_a_short_time_lets_the_app_close()
    {
        var guard = new UnhandledErrorGuard();
        for (var i = 0; i < UnhandledErrorGuard.MaxFailures; i++)
        {
            guard.Admit(Start.AddSeconds(i));
        }

        guard.Admit(Start.AddSeconds(UnhandledErrorGuard.MaxFailures)).Should().BeFalse("they're coming round faster than anyone could act on them");
    }

    [Fact]
    public void Failures_far_enough_apart_never_add_up()
    {
        var guard = new UnhandledErrorGuard();

        for (var i = 0; i < 20; i++)
        {
            guard.Admit(Start + (UnhandledErrorGuard.Window * i)).Should().BeTrue();
        }
    }

    [Fact]
    public void What_ReactiveUI_was_left_holding_is_shown_by_its_cause()
    {
        var cause = new TimeoutException();

        UnhandledErrorGuard.CauseOf(new UnhandledErrorException("Wrapped by ReactiveUI.", cause)).Should().BeSameAs(cause);
    }

    [Fact]
    public void Anything_else_is_shown_as_it_is()
    {
        var failure = new InvalidOperationException();

        UnhandledErrorGuard.CauseOf(failure).Should().BeSameAs(failure);
        UnhandledErrorGuard.CauseOf(new UnhandledErrorException("Nothing inside it.")).Should().BeOfType<UnhandledErrorException>();
    }

    [Fact]
    public void The_message_says_what_went_wrong_and_that_the_app_carries_on() =>
        UnhandledErrorGuard.CarryOnText(new TimeoutException()).Should().StartWith(Failure.TimedOutText).And.Contain("still running");
}
