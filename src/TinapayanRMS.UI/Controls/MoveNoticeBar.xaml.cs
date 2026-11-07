using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The notice of the last move in a main screen's list, with Undo, under its tree (ProductListCard, MaterialListCard,
/// SubAssemblyListCard). It puts itself away a few seconds after a notice shows, unless the mouse is over it.
/// </summary>
public sealed partial class MoveNoticeBar : UserControl
{
    /// <summary>How long a notice shows before hiding itself; checked again later while the mouse is over it.</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };

    private IListTreeScreen? _screen;

    public MoveNoticeBar()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (_screen is not null)
            {
                _screen.PropertyChanged -= ScreenChanged;
            }

            _screen = e.NewValue as IListTreeScreen;
            if (_screen is not null)
            {
                _screen.PropertyChanged += ScreenChanged;
            }

            StartClock();
        };

        _timer.Tick += (_, _) =>
        {
            // Kept while the mouse is over it, which may be on its way to Undo.
            if (!IsMouseOver && _screen is not null)
            {
                _timer.Stop();
                _screen.DismissMoveNoticeCommand.Execute().Subscribe();
            }
        };
    }

    private void ScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IListTreeScreen.MoveNotice))
        {
            StartClock();
        }
    }

    /// <summary>A new notice starts the clock again; none stops it.</summary>
    private void StartClock()
    {
        _timer.Stop();
        if (_screen?.MoveNotice is not null)
        {
            _timer.Start();
        }
    }
}
