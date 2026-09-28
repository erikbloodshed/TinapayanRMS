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
/// GridFillerRows in a grouped, name-sorted, live-data-shaped list — how the Raw Materials list (MaterialListCard.xaml)
/// was set up before its grouping was removed (2026-09-17). Kept as a regression test for any grid set up this way,
/// since GridFillerRows is still used elsewhere. Filler rows are frozen footer rows, so ones that no longer fit sit
/// over the list's rows, and a grouped grid's row generation breaks under them: an ArgumentOutOfRangeException from
/// GroupDisplayElements.GetItemAt during layout closed the app (2026-09-16). Fuzzing the list reproduced it only
/// after filler rows had been left overflowing, and never with them kept to what fits.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridFillerRowsGroupedTests
{
    [Fact]
    public void Expanding_every_group_at_once_takes_away_the_filler_rows_that_no_longer_fit()
    {
        OffscreenGrid.Run(MaterialList, grid =>
        {
            // Collapsed, the list is a header and a caption. CollapseAllGroup raises no GroupCollapsed either, so it's the
            // next refresh of a row that fills the rest with filler rows.
            CollapseAndRefresh(grid);
            grid.UnBoundRows.Should().NotBeEmpty();

            // ExpandAllGroup raises no GroupExpanded and changes no records, so nothing but layout says the rows grew.
            grid.ExpandAllGroup();
            OffscreenGrid.Pump();

            FillerRowsThatFit(grid).Should().BeGreaterThanOrEqualTo(grid.UnBoundRows.Count);
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
        return GridFillerRows.FillerRowCount(rows.RenderSize, rows.TotalExtent - (grid.UnBoundRows.Count * grid.RowHeight), grid.RowHeight);
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
