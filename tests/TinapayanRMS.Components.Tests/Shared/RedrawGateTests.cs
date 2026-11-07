using AwesomeAssertions;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// RedrawGate: however many redraws a component is asked for in one turn of its dispatcher, it's drawn once, after the
/// work that asked. The tests stand in for the dispatcher with a queue they run by hand.
/// </summary>
public sealed class RedrawGateTests
{
    /// <summary>A dispatcher's queue: what's posted waits until the test runs it, as it would behind the work in hand.</summary>
    private sealed class Turns : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out var posted))
            {
                posted.Callback(posted.State);
            }
        }
    }

    private readonly Turns _turns = new();
    private readonly RedrawGate _gate;
    private int _redraws;

    public RedrawGateTests() => _gate = new RedrawGate(work => work(), () => _redraws++);

    /// <summary>
    /// Runs <paramref name="test"/> with the queue as its thread's dispatcher, and puts the thread's own back after it. Set
    /// in the test itself, not in the constructor: TUnit makes the class on one thread and may run the test on another.
    /// </summary>
    private void OnTurns(Action test)
    {
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_turns);
        try
        {
            test();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    [Test]
    public void Many_requests_in_one_turn_are_one_redraw_after_it() => OnTurns(() =>
    {
        for (var i = 0; i < 300; i++)
        {
            _gate.Request();
        }

        _redraws.Should().Be(0, "the work that asked is still changing things");
        _gate.IsPending.Should().BeTrue();

        _turns.RunAll();

        _redraws.Should().Be(1);
        _gate.IsPending.Should().BeFalse();
    });

    [Test]
    public void A_request_after_the_redraw_is_drawn_too() => OnTurns(() =>
    {
        _gate.Request();
        _turns.RunAll();

        _gate.Request();
        _gate.Request();
        _turns.RunAll();

        _redraws.Should().Be(2);
    });

    [Test]
    public void A_component_drawn_some_other_way_meanwhile_isnt_drawn_again() => OnTurns(() =>
    {
        _gate.Request();
        _gate.Drawn();
        _turns.RunAll();

        _redraws.Should().Be(0);
        _gate.IsPending.Should().BeFalse();
    });

    [Test]
    public void A_request_made_after_that_drawing_still_is() => OnTurns(() =>
    {
        _gate.Request();
        _gate.Drawn();
        _gate.Request();
        _turns.RunAll();

        _redraws.Should().Be(1);
    });

    [Test]
    public void With_nothing_asked_for_nothing_is_drawn() => OnTurns(() =>
    {
        _turns.RunAll();

        _redraws.Should().Be(0);
        _gate.IsPending.Should().BeFalse();
    });
}
