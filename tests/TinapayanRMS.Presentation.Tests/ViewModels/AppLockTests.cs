using AwesomeAssertions;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// Whether the window is locked: it starts locked, unlocks with the password, locks again by Lock or after the auto-lock's
/// idle minutes, and makes a try wait after five wrong passwords in a row.
/// </summary>
public class AppLockTests
{
    private static readonly DateTime Start = new(2026, 11, 15, 8, 0, 0);

    [Test]
    public void It_starts_locked_with_no_sign_in_yet()
    {
        var appLock = new AppLock(new TestClock(Start));

        appLock.IsLocked.Should().BeTrue();
        appLock.HasSignedIn.Should().BeFalse();
        appLock.WaitLeft.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void Unlock_and_Lock_say_so_once_each()
    {
        var appLock = new AppLock(new TestClock(Start));
        var changes = 0;
        appLock.Changed += () => changes++;

        appLock.Unlock();
        appLock.Unlock();
        appLock.IsLocked.Should().BeFalse();
        appLock.HasSignedIn.Should().BeTrue();
        changes.Should().Be(1);

        appLock.Lock();
        appLock.Lock();
        appLock.IsLocked.Should().BeTrue();
        appLock.HasSignedIn.Should().BeTrue("the screens stay drawn under the panel once signed in");
        changes.Should().Be(2);
    }

    [Test]
    public void It_locks_itself_once_idle_for_the_auto_locks_minutes_and_not_before()
    {
        var clock = new TestClock(Start);
        var appLock = new AppLock(clock);
        appLock.SetAutoLockMinutes(5);
        appLock.Unlock();

        clock.Advance(TimeSpan.FromMinutes(4));
        appLock.CheckIdle().Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        appLock.CheckIdle().Should().BeTrue();
        appLock.IsLocked.Should().BeTrue();
        appLock.CheckIdle().Should().BeFalse("it's locked already");
    }

    [Test]
    public void A_key_or_click_starts_the_idle_time_again()
    {
        var clock = new TestClock(Start);
        var appLock = new AppLock(clock);
        appLock.SetAutoLockMinutes(5);
        appLock.Unlock();

        clock.Advance(TimeSpan.FromMinutes(4));
        appLock.NoteActivity();
        clock.Advance(TimeSpan.FromMinutes(4));

        appLock.CheckIdle().Should().BeFalse();
        appLock.IsLocked.Should().BeFalse();
    }

    [Test]
    public void An_auto_lock_of_0_never_locks()
    {
        var clock = new TestClock(Start);
        var appLock = new AppLock(clock);
        appLock.SetAutoLockMinutes(0);
        appLock.Unlock();

        clock.Advance(TimeSpan.FromDays(1));

        appLock.CheckIdle().Should().BeFalse();
        appLock.IsLocked.Should().BeFalse();
    }

    [Test]
    public void Unlocking_starts_the_idle_time_from_then()
    {
        var clock = new TestClock(Start);
        var appLock = new AppLock(clock);
        appLock.SetAutoLockMinutes(5);

        // Locked at the sign-in panel for an hour, then signed in.
        clock.Advance(TimeSpan.FromHours(1));
        appLock.Unlock();

        appLock.CheckIdle().Should().BeFalse();
    }

    [Test]
    public void The_fifth_wrong_password_in_a_row_makes_the_next_try_wait_half_a_minute()
    {
        var clock = new TestClock(Start);
        var appLock = new AppLock(clock);

        for (var i = 0; i < AppLock.WrongTriesBeforeWait - 1; i++)
        {
            appLock.NoteWrongTry();
        }

        appLock.WaitLeft.Should().Be(TimeSpan.Zero);

        appLock.NoteWrongTry();
        appLock.WaitLeft.Should().Be(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(20));
        appLock.WaitLeft.Should().Be(TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(10));
        appLock.WaitLeft.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void Signing_in_forgets_the_wrong_tries_before_it()
    {
        var appLock = new AppLock(new TestClock(Start));
        for (var i = 0; i < AppLock.WrongTriesBeforeWait - 1; i++)
        {
            appLock.NoteWrongTry();
        }

        appLock.Unlock();
        appLock.Lock();
        appLock.NoteWrongTry();

        appLock.WaitLeft.Should().Be(TimeSpan.Zero, "the count started again with the sign-in");
    }
}
