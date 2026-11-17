using System.Collections.ObjectModel;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Shared;

/// <summary>A figure worked out when read, only when what it reads changed, and a screen told when it may have.</summary>
public class DerivedTests
{
    private int _computed;
    private int _told;
    private readonly Line _input = new() { Grams = 1m };
    private readonly Derived<decimal> _figure;

    public DerivedTests() => _figure = new Derived<decimal>(() => { _computed++; return _input.Grams * 10m; }, () => _told++);

    [Test]
    public void It_is_worked_out_when_first_read_and_not_again_until_something_changes()
    {
        _computed.Should().Be(0, "nothing has read it yet");

        (_figure.Value, _figure.Value, _figure.Value).Should().Be((10m, 10m, 10m));

        _computed.Should().Be(1, "a screen drawn again reads it for nothing");
    }

    [Test]
    public void Marked_stale_it_is_worked_out_again_once_from_what_it_reads_then()
    {
        _ = _figure.Value;

        _input.Grams = 2m;
        _figure.Invalidate();
        _input.Grams = 3m;
        _figure.Invalidate();

        (_figure.Value, _figure.Value).Should().Be((30m, 30m));
        _computed.Should().Be(2, "thirty writes in an undo work it out once, from the whole form");
    }

    [Test]
    public void The_screen_is_told_once_a_figure_it_read_goes_stale_not_on_every_change()
    {
        _figure.Invalidate();
        _told.Should().Be(0, "nothing showed it, so there's nothing to draw again");

        _ = _figure.Value;
        _figure.Invalidate();
        _figure.Invalidate();
        _figure.Invalidate();

        _told.Should().Be(1, "a pass through every row asks for one redraw");
    }

    [Test]
    public void Any_change_to_a_table_marks_it_stale_a_row_added_taken_out_or_changed()
    {
        ObservableCollection<Line> lines = [new() { Grams = 100m }];
        var total = new Derived<decimal>(() => lines.Sum(l => l.Grams), () => { });
        using var watch = RowChanges.OnAnyChange(lines, total.Invalidate);
        total.Value.Should().Be(100m);

        lines[0].Grams = 150m;
        total.Value.Should().Be(150m, "a row's grams typed");

        lines.Add(new Line { Grams = 50m });
        total.Value.Should().Be(200m, "a row added");

        var added = lines[1];
        lines.RemoveAt(0);
        total.Value.Should().Be(50m, "a row taken out");

        added.Grams = 70m;
        total.Value.Should().Be(70m, "a row added since is watched too");
    }

    private sealed class Line : ObservableRow
    {
        public decimal Grams
        {
            get;
            set => RaiseAndSetIfChanged(ref field, value);
        }
    }
}
