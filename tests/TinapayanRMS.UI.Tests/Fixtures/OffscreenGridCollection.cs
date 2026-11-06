using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// Every test that builds a real SfDataGrid or SfTreeGrid: the grid behaviour tests in an off-screen window
/// (<see cref="OffscreenGrid"/>) and the themed ones (<see cref="StaThread.RunThemed"/>). They run one at a time:
/// <list type="bullet">
/// <item>As a collection each, their windows would be up at once, on separate STA threads, and a different
/// current-cell or focus test would fail on almost every full run while each passed alone.</item>
/// <item>Syncfusion keeps whether the current cell has been validated in one static flag for the whole process
/// (ValidationHelper.IsCurrentCellValidated). A themed grid test running beside an edit could set it, so the edit's
/// commit skipped the validation that writes an Explicit binding's value, and a pick never reached its row
/// (GridAddNewRowTabTests, about one full run in three). The app has one UI thread and one edit at a time.</item>
/// </list>
/// Such a class carries <c>[NotInParallel(OffscreenGridCollection.Name)]</c>.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Named for the collection it names.")]
public static class OffscreenGridCollection
{
    public const string Name = "Offscreen grid";
}
