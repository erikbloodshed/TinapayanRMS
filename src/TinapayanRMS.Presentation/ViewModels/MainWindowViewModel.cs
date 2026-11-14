using System.Reactive.Linq;
using ReactiveUI;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The window's title, which Windows shows on the taskbar and in Alt+Tab, and the title bar's heading: the title in
/// capitals.
/// </summary>
public sealed class MainWindowViewModel : ReactiveObject
{
    private string _title = "Tinapayan Festival Bakeshoppe — Recipe & Costing";

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    private readonly ObservableAsPropertyHelper<string> _heading;
    public string Heading => _heading.Value;

    public MainWindowViewModel()
    {
        _heading = this.WhenAnyValue(x => x.Title)
            .Select(title => title.ToUpperInvariant())
            .ToProperty(this, x => x.Heading);
    }
}
