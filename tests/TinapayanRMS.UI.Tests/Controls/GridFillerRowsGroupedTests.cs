using System.Collections.ObjectModel;
using System.ComponentModel;
using AwesomeAssertions;
using Syncfusion.Data;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Converters;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridFillerRows in a grouped, name-sorted, live-data-shaped list, kept as a regression
/// test for any grid set up this way. As the grid's own frozen footer rows, padding that no longer fits would sit over
/// the list's rows, and a grouped grid's row generation breaks under them: an ArgumentOutOfRangeException from
/// GroupDisplayElements.GetItemAt during layout closes the app. Drawn,
/// the padding is never in the grid's rows, and it still has to keep to the space the rows leave.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridFillerRowsGroupedTests
{
    [Fact]
    public void Expanding_every_group_at_once_takes_away_the_padding_that_no_longer_fits()
    {
        OffscreenGrid.Run(MaterialList, grid =>
        {
            // Collapsed, the list is a header and a caption, and the rest is padded.
            CollapseAndRefresh(grid);
            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().BePositive();

            // ExpandAllGroup raises no GroupExpanded and changes no records, so nothing but layout says the rows grew.
            grid.ExpandAllGroup();
            OffscreenGrid.Pump();

            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(FillerRowsThatFit(grid));
            grid.UnBoundRows.Should().BeEmpty();
        });
    }

    private static void CollapseAndRefresh(SfDataGrid grid)
    {
        var materials = (ObservableCollection<RawMaterial>)grid.ItemsSource;
        grid.CollapseAllGroup();
        OffscreenGrid.Pump();
        materials[^1] = Material(materials[^1].Id, materials[^1].Name, cost: 0.9m);
        OffscreenGrid.Pump();
    }

    private static int FillerRowsThatFit(SfDataGrid grid)
    {
        var rows = (PixelScrollAxis)grid.GetVisualContainer().ScrollRows;
        return GridFillerRows.FillerRowCount(rows.RenderSize, rows.TotalExtent, grid.RowHeight);
    }

    private static readonly string[] MaterialNames = ["Butter", "Egg", "Egg White", "Egg Yolk", "Flour", "Salt", "Sugar"];

    /// <summary>A 200 px list of seven ingredients, grouped, sorted and shaped as the Raw Materials list is, with filler rows.</summary>
    private static SfDataGrid MaterialList()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<RawMaterial>(
                MaterialNames.Select((name, i) => Material(i + 1, name, cost: 0.01m * (i + 1)))),
            AutoGenerateColumns = false,
            AllowEditing = false,
            SelectionMode = GridSelectionMode.Single,
            NavigationMode = NavigationMode.Row,
            LiveDataUpdateMode = LiveDataUpdateMode.AllowDataShaping,
            AutoExpandGroups = true,
            ShowColumnWhenGrouped = false,
            Height = 200,
        };
        grid.GroupColumnDescriptions.Add(new GroupColumnDescription { ColumnName = "Type", Converter = new RawMaterialGroupConverter() });
        grid.SortColumnDescriptions.Add(new SortColumnDescription { ColumnName = "Name", SortDirection = ListSortDirection.Ascending });
        grid.Columns.Add(new GridTextColumn { MappingName = "Name" });
        grid.Columns.Add(new GridTextColumn { MappingName = "Type" });
        grid.Columns.Add(new NumericColumn { MappingName = "CostPerBaseUnit", NumberDecimalDigits = 4 });
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }

    private static RawMaterial Material(int id, string name, decimal cost) => new()
    {
        Id = id,
        Name = name,
        Type = RawMaterialType.Ingredient,
        Source = RawMaterialSource.Purchased,
        CostPerBaseUnit = cost,
        BufferedCostPerBaseUnit = cost,
    };
}
