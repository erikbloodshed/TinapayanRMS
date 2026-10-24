using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridAutoRowHeight on a table like the Mixing Sheet: a line whose wrapped column runs to a second line is tall enough
/// to show all of it, a short line stays one line tall, and a line replaced in place is measured again.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridAutoRowHeightTests
{
    public sealed record Mix(string Name, string For);

    private const string LongFor = "Hopia Ube x 5 250 pcs, Hopia Monggo x 5 100 pcs, Hopia Baboy x 5 50 pcs, Hopia Hapon x 5 20 pcs";

    [Fact]
    public void A_line_whose_text_wraps_is_tall_enough_for_all_of_it_and_a_short_one_stays_one_line()
    {
        OffscreenGrid.Run(MixesGrid, grid =>
        {
            var heights = grid.GetVisualContainer().RowHeights;
            var shortRow = grid.ResolveToRowIndex(0);
            var longRow = grid.ResolveToRowIndex(1);

            heights[shortRow].Should().Be(grid.RowHeight);
            heights[longRow].Should().BeGreaterThan(grid.RowHeight);
            heights[longRow].Should().BeGreaterThanOrEqualTo(Math.Floor(DrawnTextHeight(grid, LongFor)), "no line of the text is cut off");
        });
    }

    [Fact]
    public void A_line_replaced_in_place_is_measured_again()
    {
        OffscreenGrid.Run(MixesGrid, grid =>
        {
            var mixes = (ObservableCollection<Mix>)grid.ItemsSource;
            var longRow = grid.ResolveToRowIndex(1);

            mixes[1] = mixes[1] with { For = "Hopia Ube x 5 250 pcs" };
            OffscreenGrid.Pump();

            grid.GetVisualContainer().RowHeights[longRow].Should().Be(grid.RowHeight);
        });
    }

    public sealed record Ingredient(string Name, decimal Grams);

    public sealed record MixWithIngredients(string Name, string For, IReadOnlyList<Ingredient> Ingredients);

    [Fact]
    public void In_a_grid_whose_rows_open_to_details_a_wrapping_line_is_still_tall_enough_and_its_details_keep_theirs()
    {
        OffscreenGrid.Run(MixesWithDetailsGrid, grid =>
        {
            var heights = grid.GetVisualContainer().RowHeights;
            var shortRow = grid.ResolveToRowIndex(0);
            var longRow = grid.ResolveToRowIndex(1);

            heights[shortRow].Should().Be(grid.RowHeight);
            heights[longRow].Should().BeGreaterThan(grid.RowHeight, "the grid never asks a line's height once it has details, so it's given directly");
            var tall = heights[longRow];

            // Opened, the first mix's details take their own height; the lines keep theirs.
            grid.ExpandDetailsViewAt(0);
            OffscreenGrid.Pump();
            heights[shortRow + 1].Should().BeGreaterThan(grid.RowHeight, "the details row is the nested grid's height");
            heights[shortRow].Should().Be(grid.RowHeight);
            heights[grid.ResolveToRowIndex(1)].Should().Be(tall);

            // Replaced in place with a short For, the line is measured again.
            var mixes = (ObservableCollection<MixWithIngredients>)grid.ItemsSource;
            mixes[1] = mixes[1] with { For = "Hopia Ube x 5 250 pcs" };
            OffscreenGrid.Pump();
            heights[grid.ResolveToRowIndex(1)].Should().Be(grid.RowHeight);
        });
    }

    /// <summary>The Mixing Sheet's table with its rows opening to their ingredients.</summary>
    private static SfDataGrid MixesWithDetailsGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<MixWithIngredients>
            {
                new("Pandesal Dough", "Pandesal 1,150 pcs", [new Ingredient("Bread Flour", 5000m), new Ingredient("Sugar", 600m)]),
                new("Hopia Dough", LongFor, [new Ingredient("All-Purpose Flour", 2000m)]),
            },
            AutoGenerateColumns = false,
            AutoGenerateRelations = false,
            AllowEditing = false,
            ColumnSizer = GridLengthUnitType.Star,
            Width = 560,
            Height = 500,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(MixWithIngredients.Name), Width = 160 });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(MixWithIngredients.For), TextWrapping = TextWrapping.Wrap });
        var details = new SfDataGrid { AutoGenerateColumns = false };
        details.Columns.Add(new GridTextColumn { MappingName = nameof(Ingredient.Name) });
        details.Columns.Add(new GridNumericColumn { MappingName = nameof(Ingredient.Grams) });
        grid.DetailsViewDefinition.Add(new GridViewDefinition { RelationalColumn = nameof(MixWithIngredients.Ingredients), DataGrid = details });
        GridAutoRowHeight.SetIsEnabled(grid, true);
        return grid;
    }

    /// <summary>What the cell showing <paramref name="text"/> needs: its text's height, plus the cell's padding and border.</summary>
    private static double DrawnTextHeight(SfDataGrid grid, string text)
    {
        var cell = Descendants(grid).OfType<GridCell>().First(c => Descendants(c).OfType<TextBlock>().Any(t => t.Text == text));
        var block = Descendants(cell).OfType<TextBlock>().First(t => t.Text == text);
        var measured = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = block.FontFamily,
            FontSize = block.FontSize,
            FontWeight = block.FontWeight,
        };
        measured.Measure(new Size(block.ActualWidth, double.PositiveInfinity));
        return measured.DesiredSize.Height + block.Margin.Top + block.Margin.Bottom
            + cell.Padding.Top + cell.Padding.Bottom + cell.BorderThickness.Top + cell.BorderThickness.Bottom;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Two mixes, the second for so many products its For text wraps, in a grid as narrow as the Mixing Sheet's For column.</summary>
    private static SfDataGrid MixesGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Mix> { new("Pandesal Dough", "Pandesal 1,150 pcs"), new("Hopia Dough", LongFor) },
            AutoGenerateColumns = false,
            AllowEditing = false,
            ColumnSizer = GridLengthUnitType.Star,
            Width = 560,
            Height = 300,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Mix.Name), Width = 160 });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Mix.For), TextWrapping = TextWrapping.Wrap });
        GridAutoRowHeight.SetIsEnabled(grid, true);
        return grid;
    }
}
