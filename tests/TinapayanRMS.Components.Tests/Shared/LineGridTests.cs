using System.Collections.ObjectModel;
using System.ComponentModel;
using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// LineGrid: the tables typed straight into. The add row is a draft that joins the lines the moment it's typed into,
/// and never otherwise.
/// </summary>
public class LineGridTests
{
    private sealed class Line : INotifyPropertyChanged
    {
        private string? _name;
        private decimal _cost;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        /// <summary>A figure the screen works out, which changes without anything being typed.</summary>
        public decimal Cost
        {
            get => _cost;
            set
            {
                _cost = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cost)));
            }
        }
    }

    private readonly ObservableCollection<Line> _lines = [new() { Name = "Flour" }, new() { Name = "Water" }];
    private readonly List<Line> _drafts = [];

    private Line NewDraft()
    {
        var draft = new Line();
        _drafts.Add(draft);
        return draft;
    }

    private IRenderedComponent<LineGrid<Line>> Render(BunitContext context, Action<ComponentParameterCollectionBuilder<LineGrid<Line>>>? more = null) =>
        context.Render<LineGrid<Line>>(parameters =>
        {
            parameters
                .Add(grid => grid.Items, _lines)
                .Add(grid => grid.NewItem, NewDraft)
                .Add(grid => grid.Header, "<th>Name</th>")
                .Add(grid => grid.Row, line => $"<td class=\"name\">{line.Name}</td>");
            more?.Invoke(parameters);
        });

    [Test]
    public async Task The_lines_are_followed_by_an_add_row()
    {
        await using var context = AppServices.NewContext();

        var grid = Render(context);

        var rows = grid.FindAll("tbody tr");
        rows.Should().HaveCount(3);
        rows.Select(row => row.ClassList.Contains("add-row")).Should().Equal(false, false, true);
        grid.FindAll("td.name").Select(cell => cell.TextContent).Should().Equal("Flour", "Water", "");
    }

    [Test]
    public async Task Typing_in_the_add_row_makes_it_a_line_and_a_new_add_row_takes_its_place()
    {
        await using var context = AppServices.NewContext();
        var grid = Render(context);

        await grid.InvokeAsync(() => _drafts[0].Name = "Yeast");

        grid.WaitForAssertion(() => _lines.Select(line => line.Name).Should().Equal("Flour", "Water", "Yeast"));
        _lines[2].Should().BeSameAs(_drafts[0]);
        _drafts.Should().HaveCount(2, "a fresh draft takes the add row");
        var rows = grid.FindAll("tbody tr");
        rows.Should().HaveCount(4);
        rows[^1].ClassList.Should().Contain("add-row");
    }

    [Test]
    public async Task An_add_row_left_alone_is_never_a_line()
    {
        await using var context = AppServices.NewContext();

        Render(context);

        _lines.Should().HaveCount(2);
        _drafts.Should().ContainSingle();
    }

    [Test]
    public async Task An_add_row_the_screen_changes_while_its_still_blank_stays_the_add_row()
    {
        await using var context = AppServices.NewContext();
        var grid = Render(context, parameters => parameters.Add(g => g.IsBlank, line => line.Name is null));

        // A composite's Products table prices its add row as costs arrive: nothing was typed, so it isn't a line.
        await grid.InvokeAsync(() => _drafts[0].Cost = 12.5m);

        _lines.Should().HaveCount(2);
        _drafts.Should().ContainSingle();

        await grid.InvokeAsync(() => _drafts[0].Name = "Yeast");

        grid.WaitForAssertion(() => _lines.Should().HaveCount(3));
    }

    [Test]
    public async Task A_read_only_table_has_no_add_row()
    {
        await using var context = AppServices.NewContext();

        var grid = Render(context, parameters => parameters.Add(g => g.ReadOnly, true));

        grid.FindAll("tbody tr").Should().HaveCount(2);
        grid.FindAll("tr.add-row").Should().BeEmpty();
    }

    [Test]
    public async Task Clicking_a_line_picks_it_but_the_add_row_is_never_the_line_picked()
    {
        await using var context = AppServices.NewContext();
        Line? picked = null;
        var grid = Render(context, parameters => parameters.Add(g => g.SelectedItemChanged, line => picked = line));

        grid.FindAll("tbody tr")[1].Click();
        picked.Should().BeSameAs(_lines[1]);

        picked = null;
        grid.Find("tr.add-row").Click();
        picked.Should().BeNull("Remove takes lines only");
    }

    [Test]
    public async Task An_empty_read_only_table_says_so()
    {
        await using var context = AppServices.NewContext();
        _lines.Clear();

        var grid = Render(context, parameters => parameters.Add(g => g.ReadOnly, true).Add(g => g.EmptyText, "No lines yet."));

        grid.Find("tr.empty-row").TextContent.Should().Be("No lines yet.");
    }

    [Test]
    public async Task A_table_left_is_let_go_though_its_lines_and_draft_live_on()
    {
        await using var context = AppServices.NewContext();

        var left = context.ShowAndLeave<LineGrid<Line>>(
            parameters => parameters
                .Add(grid => grid.Items, _lines)
                .Add(grid => grid.NewItem, NewDraft)
                .Add(grid => grid.Header, "<th>Name</th>")
                .Add(grid => grid.Row, line => $"<td class=\"name\">{line.Name}</td>"),
            whileShown: grid => grid.FindAll("tbody tr").Should().HaveCount(3));

        // What the screen works out goes on changing once the table has gone: its lines' costs, and its draft's.
        await context.Renderer.Dispatcher.InvokeAsync(() =>
        {
            foreach (var line in _lines.Concat(_drafts))
            {
                line.Cost = 1m;
            }
        });

        left.IsCollected().Should().BeTrue("the screen's lines and draft outlive every visit to it, and must hold on to none of the tables drawn from them");
        GC.KeepAlive(_drafts);
    }
}
