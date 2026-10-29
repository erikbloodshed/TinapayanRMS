using System.Reactive.Linq;
using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Proves the ReactiveUI bootstrap end-to-end: a ReactiveObject property driving
/// a WhenAnyValue-derived property, resolved through DI like every future ViewModel.
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

    /// <summary>Back and Forward between the screens, which the header's buttons are enabled by. MainWindow records and follows it.</summary>
    public NavigationHistory Navigation { get; } = new();

    public MainWindowViewModel()
    {
        _heading = this.WhenAnyValue(x => x.Title)
            .Select(title => title.ToUpperInvariant())
            .ToProperty(this, x => x.Heading);
    }
}
