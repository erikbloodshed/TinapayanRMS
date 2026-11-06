using AwesomeAssertions;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>A screen's derived updates: at once, or once at the end of a batch of writes such as an undo's restore.</summary>
public class DerivedUpdatesTests
{
    private readonly List<string> _ran = [];
    private readonly DerivedUpdates _updates = new();

    private void Costs() => _ran.Add("costs");

    private void Batch() => _ran.Add("batch");

    [Test]
    public void An_update_outside_a_batch_runs_at_once()
    {
        _updates.Run(Costs);
        _updates.Run(Costs);

        _ran.Should().Equal("costs", "costs");
    }

    [Test]
    public void Updates_asked_for_in_a_batch_run_once_each_when_it_ends_in_the_order_first_asked()
    {
        _updates.Batch(() =>
        {
            _updates.Run(Batch);
            _updates.Run(Costs);
            _updates.Run(Batch);
            _updates.IsBatching.Should().BeTrue();
            _ran.Should().BeEmpty("nothing runs from a half-written form");
        });

        _ran.Should().Equal("batch", "costs");
        _updates.IsBatching.Should().BeFalse();
    }

    [Test]
    public void Nested_batches_run_their_updates_as_the_outermost_ends()
    {
        _updates.Batch(() =>
        {
            _updates.Batch(() => _updates.Run(Costs));
            _ran.Should().BeEmpty();
        });

        _ran.Should().Equal("costs");
    }

    [Test]
    public void Writes_that_throw_drop_the_updates_they_held_and_leave_no_batch_open()
    {
        var act = () => _updates.Batch(() =>
        {
            _updates.Run(Costs);
            throw new InvalidOperationException("refused");
        });

        act.Should().Throw<InvalidOperationException>();
        _ran.Should().BeEmpty();
        _updates.Run(Costs);
        _ran.Should().Equal("costs");
    }
}
