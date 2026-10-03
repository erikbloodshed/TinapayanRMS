using AwesomeAssertions;

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
}
