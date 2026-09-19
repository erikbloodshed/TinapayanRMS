using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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
/// The multi-column drop-down cell renderer, set up for typing lines into a grid:
/// <list type="bullet">
/// <item>The one editor serves every row of the column and kept the text typed on the last row, so a new row opened
/// showing it. Each edit now starts from its own row's value.</item>
/// <item>Autocomplete ran 500 ms after a keystroke (<see cref="SfMultiColumnDropDownControl.FilterDelay"/>), so a
/// name typed and tabbed out of at once was never picked. It now runs as each key is typed.</item>
/// <item>The list opened only with F4 or the arrow. It now opens as the first key is typed
/// (<see cref="SfMultiColumnDropDownControl.AllowImmediatePopup"/>, which the column has no property for).</item>
/// <item>A column with <c>SearchCondition="Contains"</c> picks a name that contains the typed text, not only one that
/// starts with it (<see cref="ScreenAwareDropDownControl"/>).</item>
/// <item>The pop-up is placed on the monitor the cell is on (<see cref="ScreenAwareDropDownControl"/>).</item>
/// <item>Tab keeps the row picked in the open list, as Enter does; the grid moved on without it.</item>
/// <item>The key that starts the edit is typed into the box; the base dropped it, so the first letter had to be typed twice.</item>
/// </list>
/// </summary>
public sealed class AutoCompleteDropDownRenderer : GridCellMultiColumnDropDownRenderer
{
    protected override SfMultiColumnDropDownControl OnCreateEditUIElement()
    {
        // What the base renderers do to the control they create, but with the screen-aware control.
        var editor = new ScreenAwareDropDownControl { FilterDelay = 0, AllowImmediatePopup = true, BorderThickness = new Thickness(1) };
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

    protected override void OnEditElementLoaded(object sender, RoutedEventArgs e)
    {
        // A key typed on a cell not yet being edited starts the edit, and the grid keeps its character in
        // PreviewInputText, since the editor doesn't exist yet to take it. The text box renderer writes it into the box
        // once the editor loads; this one's base only focused the box and dropped it, so the first letter typed never
        // showed and had to be typed again. It's typed into the box here, over the row's name as a text cell's
        // character replaces its text, as typed text, once the control has finished loading: typed during Loaded, it
        // showed but neither filtered nor picked. The list is opened first, as the control's own key-down opens it
        // (AllowImmediatePopup) before the text arrives; the text then filters and picks as any key's does.
        var typed = (string?)PreviewInputText;
        base.OnEditElementLoaded(sender, e);

        if (string.IsNullOrEmpty(typed) || sender is not ScreenAwareDropDownControl editor)
        {
            return;
        }

        editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (editor.TypingBox is not { IsKeyboardFocused: true } box)
            {
                return;
            }

            box.SelectAll();
            editor.IsDropDownOpen = editor.AllowImmediatePopup;
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, typed));
        });
    }

    protected override bool ShouldGridTryToHandleKeyDown(KeyEventArgs e)
    {
        // The grid takes Tab in its PreviewKeyDown, before the control's own key handling ever sees it, and moves on to
        // the next cell reading the value picked before the list was opened. The row picked in the list (with the arrow
        // keys) was only committed by Enter. Tab now commits it too, as the control's own Tab would, then moves on.
        if (e.Key == Key.Tab && HasCurrentCellState && IsInEditing && CurrentCellRendererElement is ScreenAwareDropDownControl editor)
        {
            editor.CommitListPick();
        }

        return base.ShouldGridTryToHandleKeyDown(e);
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
/// An SfMultiColumnDropDownControl for a grid cell that's typed into:
/// <list type="bullet">
/// <item>Its pop-up opens below the cell when it fits on the cell's monitor, and above it otherwise. The control's own
/// placement measured the room below against the whole virtual desktop's height
/// (<see cref="SystemParameters.VirtualScreenHeight"/>), so on a shorter monitor than the tallest one it opened
/// below anyway, and Windows pushed the pop-up up over the cell being typed into.</item>
/// <item>With <see cref="SfMultiColumnDropDownControl.SearchCondition"/> set to Contains, typing picks a name that
/// contains the text. The condition only ever filtered the list: the pick (autocomplete) took the first name starting
/// with the text, whatever the condition, and wrote that name into the box. A name starting with the text still wins
/// ("fir" picks First Class Flour); when none does, the first that contains it ("flour" picks All Purpose Flour). The
/// text stays as typed.</item>
/// </list>
/// </summary>
public sealed class ScreenAwareDropDownControl : SfMultiColumnDropDownControl
{
    /// <summary>Whether a name in the list starts with the text being typed, once worked out for that text.</summary>
    private bool? _anyNameStartsWithText;

    /// <summary>The text box its text is typed into, once its template has been applied.</summary>
    internal TextBox? TypingBox => GetTemplateChild("PART_TextBox") as TextBox;

    /// <summary>Whether its template, with the text box its text is typed into, has been applied.</summary>
    internal bool HasEditor => TypingBox is not null;

    protected override void ProcessOnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        // The pick for new text asks again whether a name starts with it.
        _anyNameStartsWithText = null;
        base.ProcessOnEditorTextChanged(sender, e);
    }

    protected override bool ProcessAppendText(object item, string exactValue, string filterText)
    {
        if (SearchCondition != SearchCondition.Contains)
        {
            return base.ProcessAppendText(item, exactValue, filterText);
        }

        // Asked of each name in list order until one is picked, both texts lower-cased as the list's filter compares
        // them. The first name starting with the text is picked, or, when none does, the first containing it.
        return !string.IsNullOrEmpty(filterText)
            && (exactValue.StartsWith(filterText, StringComparison.CurrentCulture) || (exactValue.Contains(filterText) && !AnyNameStartsWith(filterText)));
    }

    protected override List<string> ProcessAppendStringList(object item, string _value)
    {
        // The base writes the picked name into the box and selects the part not typed yet, which only fits a name the
        // text starts. With Contains the box keeps what was typed: SearchText, taken just before the pick.
        return SearchCondition == SearchCondition.Contains ? [SearchText] : base.ProcessAppendStringList(item, _value);
    }

    protected override void CommitValue(bool autoCommit = false)
    {
        // Leaving the cell keeps the pick only when the box holds its whole name, which a contained text doesn't.
        // With Contains, the pick typing made is kept, as Tab keeps it.
        base.CommitValue(autoCommit || (SearchCondition == SearchCondition.Contains && SelectedItem is not null));
    }

    /// <summary>
    /// Makes the row picked in the open list the control's pick and closes the list: what the control does itself on
    /// Enter. Nothing while the list is closed or has no row picked, which is how text matching no name leaves it
    /// (the columns' AllowNullInput clears the list's pick), so Tab never commits a name the text doesn't match.
    /// </summary>
    internal void CommitListPick()
    {
        if (!IsDropDownOpen || InternalGrid?.SelectedItem is not { } picked)
        {
            return;
        }

        SelectedItem = picked;
        CommitValue(autoCommit: true);
        IsDropDownOpen = false;
    }

    /// <summary>Whether any name in the list starts with the lower-cased typed text.</summary>
    private bool AnyNameStartsWith(string filterText)
    {
        _anyNameStartsWithText ??= ItemsSource is IEnumerable items
            && items.Cast<object?>().Any(item => ComparedName(item)?.StartsWith(filterText, StringComparison.CurrentCulture) == true);
        return _anyNameStartsWithText.Value;
    }

    /// <summary>An item's name as the base compares it: its display member, lower-cased unless filtering is case-sensitive.</summary>
    private string? ComparedName(object? item)
    {
        var name = item?.GetType().GetProperty(DisplayMember)?.GetValue(item)?.ToString();
        return AllowCaseSensitiveFiltering ? name : name?.ToLower(CultureInfo.CurrentCulture);
    }

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
