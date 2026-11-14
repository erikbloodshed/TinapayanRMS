using Microsoft.JSInterop;
using Syncfusion.Blazor.Grids;
using TinapayanRMS.ApplicationCore.Interfaces;
using GridAction = Syncfusion.Blazor.Grids.Action;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A table's sort, filters and columns kept as it was left, for the Windows user: the grid's own state as Syncfusion
/// writes it (GetPersistDataAsync), one line under the table's key in the view state, put back as the grid is created.
/// Reports' tables keep theirs this way.
/// </summary>
public static class GridLayouts
{
    /// <summary>Puts <paramref name="grid"/> back as it was left, when it was. A layout the grid can't take is left out.</summary>
    public static async Task RestoreAsync<TRow>(SfGrid<TRow>? grid, IViewStateStore viewState, string key)
    {
        if (grid is null || viewState.ReadList(key) is not [{ Length: > 0 } layout, ..])
        {
            return;
        }

        try
        {
            await grid.SetPersistDataAsync(layout);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // A layout from an older table: it shows as it starts instead.
        }
    }

    /// <summary>Keeps <paramref name="grid"/>'s layout once <paramref name="action"/> has changed it: a sort, a filter, the columns shown.</summary>
    public static async Task SaveAfterAsync<TRow>(SfGrid<TRow>? grid, IViewStateStore viewState, string key, GridAction action)
    {
        if (grid is null || action is not (GridAction.Sorting or GridAction.Filtering or GridAction.ClearFiltering or GridAction.ColumnState))
        {
            return;
        }

        viewState.WriteList(key, [await grid.GetPersistDataAsync()]);
    }
}
