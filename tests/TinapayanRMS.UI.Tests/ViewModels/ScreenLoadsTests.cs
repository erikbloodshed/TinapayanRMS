using System.Reactive.Subjects;
using AwesomeAssertions;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The loads a screen's tab runs as it's shown (ScreenLoads, which MainWindow runs them through): one at a time per
/// screen, and one asked for while another runs follows it, since the running one may have read before another screen
/// changed what it shows.
/// </summary>
public class ScreenLoadsTests
{
    private readonly List<Subject<RxVoid>> _started = [];

    private IObservable<RxVoid> Load()
    {
        var load = new Subject<RxVoid>();
        _started.Add(load);
        return load;
    }

    [Fact]
    public void A_load_asked_for_while_the_screens_last_runs_follows_it_once()
    {
        var loads = new ScreenLoads<string>(_ => true);

        loads.Load("Products", Load);
        loads.Load("Products", Load);
        loads.Load("Products", Load);
        _started.Should().HaveCount(1, "never two of one screen's side by side");

        _started[0].OnCompleted();
        _started.Should().HaveCount(2, "one more, for what may have changed since the first began");

        _started[1].OnCompleted();
        _started.Should().HaveCount(2);
    }

    [Fact]
    public void One_asked_for_by_a_screen_left_meanwhile_waits_for_its_next_showing()
    {
        var showing = true;
        var loads = new ScreenLoads<string>(_ => showing);
        loads.Load("Products", Load);
        loads.Load("Products", Load);

        showing = false;
        _started[0].OnCompleted();
        _started.Should().HaveCount(1, "a screen that isn't showing loads when it's shown");

        showing = true;
        loads.Load("Products", Load);
        _started.Should().HaveCount(2);
    }

    [Fact]
    public void A_load_that_fails_lets_the_next_showing_load()
    {
        var loads = new ScreenLoads<string>(_ => true);
        loads.Load("Products", Load);

        _started[0].OnError(new TimeoutException());
        loads.Load("Products", Load);

        _started.Should().HaveCount(2);
    }

    [Fact]
    public void Different_screens_load_side_by_side()
    {
        var loads = new ScreenLoads<string>(_ => true);

        loads.Load("Products", Load);
        loads.Load("Reports", Load);

        _started.Should().HaveCount(2);
    }
}
