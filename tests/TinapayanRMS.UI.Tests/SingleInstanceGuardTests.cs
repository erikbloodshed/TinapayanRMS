using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;

namespace TinapayanRMS.UI.Tests;

/// <summary>
/// One copy of the app at a time, on real named objects. Each copy here does everything on a thread of its own, as a copy
/// of the app does on its UI thread, because a mutex belongs to the thread that took it: on one thread a second claim
/// would simply be let in. A thread that ends holding the mutex is what Windows sees of a copy that crashed.
/// </summary>
public class SingleInstanceGuardTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Briefly = TimeSpan.FromMilliseconds(700);

    private readonly string _name = $"TinapayanRMS.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void The_first_copy_gets_the_app()
    {
        using var copy = new Copy(_name);

        copy.HasApp.Should().BeTrue();
    }

    [Fact]
    public void A_second_copy_doesnt_get_the_app()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);

        second.HasApp.Should().BeFalse();
    }

    [Fact]
    public void A_copy_that_closed_leaves_the_app_free()
    {
        var open = new Copy(_name);
        open.Dispose();

        using var next = new Copy(_name);

        next.HasApp.Should().BeTrue();
    }

    [Fact]
    public void A_copy_that_crashed_leaves_the_app_free()
    {
        var open = new Copy(_name);
        open.Crash();

        using var next = new Copy(_name);

        next.HasApp.Should().BeTrue();
    }

    [Fact]
    public void The_open_copy_comes_forward_when_asked()
    {
        using var open = new Copy(_name);
        var asked = 0;
        open.Guard.Listen(() =>
        {
            Interlocked.Increment(ref asked);
            open.Guard.CameForward();
        });
        using var second = new Copy(_name);

        var outcome = second.Ask(Plenty);

        outcome.Should().Be(SingleInstanceOutcome.CameForward);
        Volatile.Read(ref asked).Should().BeGreaterThan(0);
        second.HasApp.Should().BeFalse();
    }

    // As the app does, once its window shows: it wasn't listening yet when it was first asked.
    [Fact]
    public void A_copy_still_starting_is_waited_for()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);
        using var startsListening = new Timer(_ => open.Guard.Listen(open.Guard.CameForward), null, Briefly, Timeout.InfiniteTimeSpan);

        second.Ask(Plenty).Should().Be(SingleInstanceOutcome.CameForward);
    }

    // A frozen copy still hears: it's the pool thread that does. Only its UI thread can say it came forward.
    [Fact]
    public void A_copy_that_only_hears_the_request_hasnt_answered()
    {
        using var open = new Copy(_name);
        var asked = 0;
        open.Guard.Listen(() => Interlocked.Increment(ref asked));
        using var second = new Copy(_name);

        second.Ask(Briefly).Should().Be(SingleInstanceOutcome.NotAnswering);

        Volatile.Read(ref asked).Should().BeGreaterThan(0);
        second.HasApp.Should().BeFalse();
    }

    [Fact]
    public void A_copy_that_never_listens_is_reported_once_patience_runs_out()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);

        second.Ask(Briefly).Should().Be(SingleInstanceOutcome.NotAnswering);

        second.HasApp.Should().BeFalse();
    }

    [Fact]
    public void A_copy_that_closes_while_asked_leaves_the_app_to_the_one_asking()
    {
        var open = new Copy(_name);
        using var second = new Copy(_name);
        using var closes = new Timer(_ => open.Dispose(), null, Briefly, Timeout.InfiniteTimeSpan);

        second.Ask(Plenty).Should().Be(SingleInstanceOutcome.Claimed);

        second.HasApp.Should().BeTrue();
        using var third = new Copy(_name);
        third.HasApp.Should().BeFalse();
    }

    [Fact]
    public void A_copy_that_crashes_while_asked_leaves_the_app_to_the_one_asking()
    {
        var open = new Copy(_name);
        using var second = new Copy(_name);
        using var crashes = new Timer(_ => open.Crash(), null, Briefly, Timeout.InfiniteTimeSpan);

        second.Ask(Plenty).Should().Be(SingleInstanceOutcome.Claimed);

        second.HasApp.Should().BeTrue();
        using var third = new Copy(_name);
        third.HasApp.Should().BeFalse();
    }

    [Fact]
    public void A_copy_that_took_over_lets_go_when_it_closes()
    {
        var open = new Copy(_name);
        var second = new Copy(_name);
        using var closes = new Timer(_ => open.Dispose(), null, Briefly, Timeout.InfiniteTimeSpan);
        second.Ask(Plenty);

        second.Dispose();

        using var third = new Copy(_name);
        third.HasApp.Should().BeTrue();
    }

    // Each answer wakes only one of them, so each has to have asked for its own.
    [Fact]
    public async Task Copies_started_together_are_each_answered()
    {
        using var open = new Copy(_name);
        open.Guard.Listen(open.Guard.CameForward);
        var others = Enumerable.Range(0, 4).Select(_ => new Copy(_name)).ToList();

        var outcomes = await Task.WhenAll(others.Select(other => Task.Run(() => other.Ask(Plenty), TestContext.Current.CancellationToken)));
        others.ForEach(other => other.Dispose());

        outcomes.Should().AllBeEquivalentTo(SingleInstanceOutcome.CameForward);
    }

    // What a copy running as an administrator looks like from one that isn't: it's there, and Windows refuses to open it.
    [Fact]
    public void A_copy_out_of_reach_is_reported_rather_than_thrown()
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User!, MutexRights.FullControl, AccessControlType.Deny));
        using var open = MutexAcl.Create(false, $@"Local\{_name}", out var createdNew, security);
        createdNew.Should().BeTrue();
        using var second = new Copy(_name);

        second.HasApp.Should().BeFalse();
        second.Ask(Plenty).Should().Be(SingleInstanceOutcome.OutOfReach);
    }

    [Fact]
    public void Closing_is_safe_whether_or_not_it_ever_had_the_app()
    {
        using var open = new Copy(_name);
        var second = new Copy(_name);
        var neverClaimed = new SingleInstanceGuard(_name);

        second.Dispose();
        var closing = () =>
        {
            neverClaimed.Dispose();
            neverClaimed.Dispose();
        };

        closing.Should().NotThrow();
    }

    [Theory]
    [InlineData(SingleInstanceOutcome.NotAnswering, SingleInstanceGuard.NotAnsweringText)]
    [InlineData(SingleInstanceOutcome.OutOfReach, SingleInstanceGuard.OutOfReachText)]
    [InlineData(SingleInstanceOutcome.CameForward, null)]
    [InlineData(SingleInstanceOutcome.Claimed, null)]
    public void Only_a_copy_that_couldnt_be_brought_forward_is_mentioned(SingleInstanceOutcome outcome, string? text) =>
        SingleInstanceGuard.TextOf(outcome).Should().Be(text);

    [Theory]
    [InlineData(@"C:\Dev\TinapayanRMS.UI.exe", @"C:\Published\TinapayanRMS.UI.exe", @"C:\Published\TinapayanRMS.UI.exe")]
    [InlineData(@"C:\Dev\TinapayanRMS.UI.exe", @"c:\dev\TINAPAYANRMS.UI.EXE", null)]
    [InlineData(@"C:\Dev\TinapayanRMS.UI.exe", null, null)]
    public void A_copy_is_elsewhere_only_when_its_path_is_known_to_be_another(string mine, string? theirs, string? elsewhere) =>
        SingleInstanceGuard.ElsewhereThan(mine, [theirs]).Should().Be(elsewhere);

    [Fact]
    public void Being_elsewhere_says_where() =>
        SingleInstanceGuard.ElsewhereText(@"C:\Published\TinapayanRMS.UI.exe").Should().Contain(@"C:\Published\TinapayanRMS.UI.exe");

    /// <summary>A copy of the app: a guard that claims when it opens, doing everything on one thread, as a UI thread does.</summary>
    private sealed class Copy : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new();
        private readonly Thread _thread;

        public Copy(string name)
        {
            Guard = new SingleInstanceGuard(name);
            _thread = new Thread(() =>
            {
                foreach (var work in _work.GetConsumingEnumerable())
                {
                    work();
                }
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            HasApp = OnItsThread(Guard.TryClaim);
        }

        public SingleInstanceGuard Guard { get; }

        public bool HasApp { get; private set; }

        public SingleInstanceOutcome Ask(TimeSpan patience)
        {
            var outcome = OnItsThread(() => Guard.AskOpenCopyToComeForward(patience));
            HasApp |= outcome == SingleInstanceOutcome.Claimed;
            return outcome;
        }

        /// <summary>Ends its thread without letting go.</summary>
        public void Crash() => End();

        public void Dispose()
        {
            if (_work.IsAddingCompleted)
            {
                return;
            }

            OnItsThread(() =>
            {
                Guard.Dispose();
                return true;
            });
            End();
        }

        private void End()
        {
            _work.CompleteAdding();
            _thread.Join();
        }

        /// <summary>Runs on the copy's thread and waits for it, with whatever it threw brought back to the test.</summary>
        private T OnItsThread<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>();
            _work.Add(() =>
            {
                try
                {
                    result.SetResult(work());
                }
                catch (Exception ex)
                {
                    result.SetException(ex);
                }
            });
            return result.Task.GetAwaiter().GetResult();
        }
    }
}
