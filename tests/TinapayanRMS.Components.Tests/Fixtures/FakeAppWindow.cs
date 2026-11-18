using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>The window for a test: it remembers what the title bar asked of it, and is maximized or restored when told to.</summary>
public sealed class FakeAppWindow : IAppWindow
{
    public bool IsMaximized { get; private set; }

    public event Action? StateChanged;

    public int Minimized { get; private set; }

    public int Closed { get; private set; }

    public int PrintDialogsClosed { get; private set; }

    public (DeviceRect TitleBar, IReadOnlyList<DeviceRect> Buttons)? TitleBar { get; private set; }

    public void Minimize() => Minimized++;

    public void ToggleMaximize()
    {
        IsMaximized = !IsMaximized;
        StateChanged?.Invoke();
    }

    public void Close() => Closed++;

    public void ClosePrintDialog() => PrintDialogsClosed++;

    public void SetTitleBar(DeviceRect titleBar, IReadOnlyList<DeviceRect> buttons) => TitleBar = (titleBar, buttons);
}
