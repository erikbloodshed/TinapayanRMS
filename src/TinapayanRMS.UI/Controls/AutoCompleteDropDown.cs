using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Puts <see cref="AutoCompleteDropDownRenderer"/> behind an SfDataGrid's GridMultiColumnDropDownList columns. Set
/// <c>controls:AutoCompleteDropDown.IsEnabled="True"</c> on a grid whose drop-down column is typed into; give the
/// column <c>AllowNullInput="True"</c> too, or clearing its text brings back the previous row's pick.
/// </summary>
public static class AutoCompleteDropDown
{
    private const string CellType = "MultiColumnDropDown";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoCompleteDropDown), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        grid.CellRenderers.Remove(CellType);
        grid.CellRenderers.Add(CellType, new AutoCompleteDropDownRenderer());
    }
}

/// <summary>
/// The multi-column drop-down cell renderer with three of its behaviours fixed for typing lines into a grid:
/// <list type="bullet">
/// <item>The one editor serves every row of the column and kept the text typed on the last row, so a new row opened
/// showing it. Each edit now starts from its own row's value.</item>
/// <item>Autocomplete ran 500 ms after a keystroke (<see cref="SfMultiColumnDropDownControl.FilterDelay"/>), so a
/// name typed and tabbed out of at once was never picked. It now runs as each key is typed.</item>
/// <item>The pop-up is placed on the monitor the cell is on (<see cref="ScreenAwareDropDownControl"/>).</item>
/// </list>
/// </summary>
public sealed class AutoCompleteDropDownRenderer : GridCellMultiColumnDropDownRenderer
{
    protected override SfMultiColumnDropDownControl OnCreateEditUIElement()
    {
        // What the base renderers do to the control they create, but with the screen-aware control.
        var editor = new ScreenAwareDropDownControl { FilterDelay = 0, BorderThickness = new Thickness(1) };
        Validation.SetErrorTemplate(editor, null);
        VisualContainer.SetWantsMouseInput(editor, true);
        return editor;
    }

    public override void OnInitializeEditElement(DataColumnBase dataColumn, SfMultiColumnDropDownControl uiElement, object dataContext)
    {
        base.OnInitializeEditElement(dataColumn, uiElement, dataContext);

        // A fresh editor has nothing left over; one already used shows this row's pick, or nothing.
        if (uiElement is ScreenAwareDropDownControl { HasEditor: true } && dataColumn.GridColumn is GridMultiColumnDropDownList column)
        {
            uiElement.Text = DisplayText(column, DataGrid.View?.GetPropertyAccessProvider().GetValue(dataContext, column.MappingName));
        }
    }

    private static string DisplayText(GridMultiColumnDropDownList column, object? value)
    {
        if (value is null || column.ItemsSource is not IEnumerable items)
        {
            return string.Empty;
        }

        var item = items.Cast<object>().FirstOrDefault(i => Equals(i.GetType().GetProperty(column.ValueMember)?.GetValue(i), value));
        return item?.GetType().GetProperty(column.DisplayMember)?.GetValue(item)?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// An SfMultiColumnDropDownControl whose pop-up opens below the cell when it fits on the cell's monitor, and above
/// it otherwise. The control's own placement measured the room below against the whole virtual desktop's height
/// (<see cref="SystemParameters.VirtualScreenHeight"/>), so on a shorter monitor than the tallest one it opened
/// below anyway, and Windows pushed the pop-up up over the cell being typed into.
/// </summary>
public sealed class ScreenAwareDropDownControl : SfMultiColumnDropDownControl
{
    /// <summary>Whether its template, with the text box its text is typed into, has been applied.</summary>
    internal bool HasEditor => GetTemplateChild("PART_TextBox") is TextBox;

    protected override void ProcessOnPopupPositionCalculation()
    {
        if (GetTemplateChild("PART_Popup") is not Popup popup || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            base.ProcessOnPopupPositionCalculation();
            return;
        }

        // Screen positions in device-independent units, as the base placement uses them.
        var fromDevice = target.TransformFromDevice;
        var cornerOnScreen = PointToScreen(new Point());
        var cell = new Rect(fromDevice.Transform(cornerOnScreen), new Size(ActualWidth, ActualHeight));
        var workAreaInPixels = MonitorWorkArea(cornerOnScreen);
        var workArea = new Rect(fromDevice.Transform(workAreaInPixels.TopLeft), fromDevice.Transform(workAreaInPixels.BottomRight));

        // The pop-up as drawn (its border, margin and resize grip) is taller than PopupHeight, the list's own height.
        var popupHeight = Math.Max(PopupHeight, (popup.Child as FrameworkElement)?.ActualHeight ?? 0);
        var roomBelow = workArea.Bottom - cell.Bottom;
        var roomAbove = cell.Top - workArea.Top;
        popup.VerticalOffset = roomBelow >= popupHeight || roomBelow >= roomAbove
            ? cell.Bottom - BorderThickness.Bottom
            : cell.Top - popupHeight;
        popup.HorizontalOffset = cell.Left + PopupWidth <= workArea.Right
            ? cell.Left
            : Math.Max(workArea.Left, cell.Right - PopupWidth);
    }

    /// <summary>The work area (the monitor without its taskbar), in pixels, of the monitor nearest a screen point.</summary>
    private static Rect MonitorWorkArea(Point screenPoint)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = (int)screenPoint.X, Y = (int)screenPoint.Y }, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
