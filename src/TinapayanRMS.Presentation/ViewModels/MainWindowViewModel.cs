namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The window's title, which Windows shows on the taskbar and in Alt+Tab, and the title bar's heading: the title in
/// capitals.
/// </summary>
public sealed class MainWindowViewModel : ObservableState
{
    private string _title = "Tinapayan Festival Bakeshoppe — Recipe & Costing";

    public string Title
    {
        get => _title;
        set => RaiseAndSetIfChanged(ref _title, value);
    }

    public string Heading => Title.ToUpperInvariant();
}
