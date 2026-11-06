using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
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

    [Test]
    public void The_first_copy_gets_the_app()
    {
        using var copy = new Copy(_name);

        copy.HasApp.Should().BeTrue();
    }

    [Test]
    public void A_second_copy_doesnt_get_the_app()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);

        second.HasApp.Should().BeFalse();
    }

    [Test]
    public void A_copy_that_closed_leaves_the_app_free()
    {
        var open = new Copy(_name);
        open.Dispose();

        using var next = new Copy(_name);

        next.HasApp.Should().BeTrue();
    }

    [Test]
    public void A_copy_that_crashed_leaves_the_app_free()
    {
        var open = new Copy(_name);
        open.Crash();

        using var next = new Copy(_name);

        next.HasApp.Should().BeTrue();
    }

    [Test]
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
    [Test]
    public void A_copy_still_starting_is_waited_for()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);
        using var startsListening = new Timer(_ => open.Guard.Listen(open.Guard.CameForward), null, Briefly, Timeout.InfiniteTimeSpan);

        second.Ask(Plenty).Should().Be(SingleInstanceOutcome.CameForward);
    }

    // A frozen copy still hears: it's the pool thread that does. Only its UI thread can say it came forward.
    [Test]
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

    [Test]
    public void A_copy_that_never_listens_is_reported_once_patience_runs_out()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name);

        second.Ask(Briefly).Should().Be(SingleInstanceOutcome.NotAnswering);

        second.HasApp.Should().BeFalse();
    }

    [Test]
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

    [Test]
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

    [Test]
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
    [Test]
    public async Task Copies_started_together_are_each_answered()
    {
        using var open = new Copy(_name);
        open.Guard.Listen(open.Guard.CameForward);
        var others = Enumerable.Range(0, 4).Select(_ => new Copy(_name)).ToList();

        var outcomes = await Task.WhenAll(others.Select(other => Task.Run(() => other.Ask(Plenty), TestContext.Current!.Execution.CancellationToken)));
        others.ForEach(other => other.Dispose());

        outcomes.Should().AllBeEquivalentTo(SingleInstanceOutcome.CameForward);
    }

    // What a copy running as an administrator looks like from one that isn't: it's there, and Windows refuses to open it.
    [Test]
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

    // What a copy does as it starts, all the way through (App.OnStartup)

    [Test]
    public void A_copy_starting_with_the_app_free_says_nothing()
    {
        using var copy = new Copy(_name, claim: false);
        List<string> said = [];

        copy.Claim(Plenty, () => null, (text, _) => said.Add(text)).Should().BeTrue();

        said.Should().BeEmpty();
    }

    [Test]
    public void A_copy_starting_with_the_app_open_closes_without_a_word_once_that_one_comes_forward()
    {
        using var open = new Copy(_name);
        open.Guard.Listen(open.Guard.CameForward);
        using var second = new Copy(_name, claim: false);
        List<string> said = [];

        second.Claim(Plenty, () => null, (text, _) => said.Add(text)).Should().BeFalse();

        said.Should().BeEmpty();
    }

    // The message waits on an answer, and the copy only comes forward after: in front, it would cover the message.
    [Test]
    public void A_copy_starting_with_another_build_open_says_where_that_runs_from_before_it_comes_forward()
    {
        const string Elsewhere = @"C:\Published\TinapayanRMS.UI.exe";
        using var open = new Copy(_name);
        var asked = 0;
        open.Guard.Listen(() =>
        {
            Interlocked.Increment(ref asked);
            open.Guard.CameForward();
        });
        using var second = new Copy(_name, claim: false);
        List<(string Text, MessageBoxImage Image)> said = [];
        var askedWhenSaid = -1;

        var claimed = second.Claim(Plenty, () => Elsewhere, (text, image) =>
        {
            said.Add((text, image));
            askedWhenSaid = Volatile.Read(ref asked);
        });

        claimed.Should().BeFalse();
        said.Should().Equal((SingleInstanceGuard.ElsewhereText(Elsewhere), MessageBoxImage.Information));
        askedWhenSaid.Should().Be(0);
        Volatile.Read(ref asked).Should().BeGreaterThan(0);
    }

    [Test]
    public void A_copy_starting_with_a_frozen_one_open_says_so_rather_than_vanish()
    {
        using var open = new Copy(_name);
        using var second = new Copy(_name, claim: false);
        List<(string Text, MessageBoxImage Image)> said = [];

        second.Claim(Briefly, () => null, (text, image) => said.Add((text, image))).Should().BeFalse();

        said.Should().Equal((SingleInstanceGuard.NotAnsweringText, MessageBoxImage.Warning));
    }

    [Test]
    public void A_copy_starting_as_the_open_one_closes_carries_on_without_a_word()
    {
        var open = new Copy(_name);
        using var second = new Copy(_name, claim: false);
        using var closes = new Timer(_ => open.Dispose(), null, Briefly, Timeout.InfiniteTimeSpan);
        List<string> said = [];

        second.Claim(Plenty, () => null, (text, _) => said.Add(text)).Should().BeTrue();

        said.Should().BeEmpty();
    }

    [Test]
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

    [Test]
    [Arguments(SingleInstanceOutcome.NotAnswering, SingleInstanceGuard.NotAnsweringText)]
    [Arguments(SingleInstanceOutcome.OutOfReach, SingleInstanceGuard.OutOfReachText)]
    [Arguments(SingleInstanceOutcome.CameForward, null)]
    [Arguments(SingleInstanceOutcome.Claimed, null)]
    public void Only_a_copy_that_couldnt_be_brought_forward_is_mentioned(SingleInstanceOutcome outcome, string? text) =>
        SingleInstanceGuard.TextOf(outcome).Should().Be(text);

    [Test]
    [Arguments(@"C:\Dev\TinapayanRMS.UI.exe", @"C:\Published\TinapayanRMS.UI.exe", @"C:\Published\TinapayanRMS.UI.exe")]
    [Arguments(@"C:\Dev\TinapayanRMS.UI.exe", @"c:\dev\TINAPAYANRMS.UI.EXE", null)]
    [Arguments(@"C:\Dev\TinapayanRMS.UI.exe", null, null)]
    public void A_copy_is_elsewhere_only_when_its_path_is_known_to_be_another(string mine, string? theirs, string? elsewhere) =>
        SingleInstanceGuard.ElsewhereThan(mine, [theirs]).Should().Be(elsewhere);

    [Test]
    public void Being_elsewhere_says_where() =>
        SingleInstanceGuard.ElsewhereText(@"C:\Published\TinapayanRMS.UI.exe").Should().Contain(@"C:\Published\TinapayanRMS.UI.exe");

    /// <summary>A copy of the app: a guard that claims when it opens, doing everything on one thread, as a UI thread does.</summary>
    private sealed class Copy : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new();
        private readonly Thread _thread;

        public Copy(string name, bool claim = true)
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
            HasApp = claim && OnItsThread(Guard.TryClaim);
        }

        public SingleInstanceGuard Guard { get; }

        public bool HasApp { get; private set; }

        public SingleInstanceOutcome Ask(TimeSpan patience)
        {
            var outcome = OnItsThread(() => Guard.AskOpenCopyToComeForward(patience));
            HasApp |= outcome == SingleInstanceOutcome.Claimed;
            return outcome;
        }

        public bool Claim(TimeSpan patience, Func<string?> openCopyElsewhere, Action<string, MessageBoxImage> say)
        {
            var claimed = OnItsThread(() => Guard.Claim(patience, openCopyElsewhere, say));
            HasApp |= claimed;
            return claimed;
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
