namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The app's window, for the title bar the page draws in place of Windows' own (Layout.TitleBar): the window has no
/// caption bar of Windows', only its border, so the page's title bar holds the menu toggle and the heading on the left
/// and Find, Back, Forward, Minimize, Maximize and Close on the right. The MAUI app implements it on its window; what the
/// title bar asks of it is only ever asked on the window's thread.
/// </summary>
public interface IAppWindow
{
    /// <summary>Whether the window fills its monitor, so the title bar shows Restore in place of Maximize.</summary>
    bool IsMaximized { get; }

    /// <summary>Raised when the window is maximized or restored, on the window's thread.</summary>
    event Action? StateChanged;

    void Minimize();

    /// <summary>Maximizes the window, or restores it to its size from before when it's maximized.</summary>
    void ToggleMaximize();

    /// <summary>Closes the window as Windows' own Close button would, so a form with unsaved changes asks first.</summary>
    void Close();

    /// <summary>
    /// Where the title bar is, and what in it takes the mouse itself (its buttons), in the web view's device pixels from
    /// its top left corner. Windows takes the rest of the bar (<see cref="DeviceRect.Caption"/>) as the window's caption:
    /// dragging it moves the window (and snaps it to an edge), a double-click maximizes or restores it, and a right-click
    /// opens its system menu, all as on any window's title bar. The page sends it again whenever the bar or a button
    /// moves or changes size.
    /// </summary>
    void SetTitleBar(DeviceRect titleBar, IReadOnlyList<DeviceRect> buttons);
}

/// <summary>A rectangle in the web view, in device pixels (CSS pixels times the page's devicePixelRatio).</summary>
public readonly record struct DeviceRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// The parts of a title bar that are its caption: the bar less its buttons, as the gaps between them, each as tall as
    /// the bar. Given to Windows as the caption itself, rather than the whole bar with the buttons passed through, since
    /// MAUI clears the passed-through parts whenever its own title bar's row changes size.
    /// </summary>
    public static IReadOnlyList<DeviceRect> Caption(DeviceRect bar, IEnumerable<DeviceRect> buttons)
    {
        var gaps = new List<DeviceRect>();
        var from = bar.X;
        foreach (var button in buttons.Where(b => b.Width > 0).OrderBy(b => b.X))
        {
            var to = Math.Min(button.X, bar.X + bar.Width);
            if (to > from)
            {
                gaps.Add(new DeviceRect(from, bar.Y, to - from, bar.Height));
            }

            from = Math.Max(from, button.X + button.Width);
        }

        if (bar.X + bar.Width > from)
        {
            gaps.Add(new DeviceRect(from, bar.Y, bar.X + bar.Width - from, bar.Height));
        }

        return gaps;
    }
}
