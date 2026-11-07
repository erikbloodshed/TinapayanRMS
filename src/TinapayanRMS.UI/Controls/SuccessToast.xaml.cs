using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The hosting screen's SuccessMessage as a toast in its page's bottom-right corner, where ErrorToast
/// shows errors. Like ErrorToast it never sets its own DataContext: it binds to the SuccessMessage every
/// ViewModelBase has. Unlike an error, which stays until it's dealt with, it fades out a few seconds
/// after it opens, unless the mouse is over it; its close button hides it at once. RunSafelyAsync
/// clears the message before every action, so a second save shows it again, and an error replaces it.
/// </summary>
public sealed partial class SuccessToast : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(SuccessToast), new PropertyMetadata(null, OnMessageChanged));

    /// <summary>How long it shows before fading out; checked again later while the mouse is over it.</summary>
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public SuccessToast()
    {
        InitializeComponent();
        _hideTimer.Tick += OnHideTimerTick;
        SetBinding(MessageProperty, new Binding(nameof(ViewModelBase.SuccessMessage)));
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toast = (SuccessToast)d;
        toast._hideTimer.Stop();
        if (string.IsNullOrEmpty((string?)e.NewValue))
        {
            toast.Visibility = Visibility.Collapsed;
            return;
        }

        toast.Visibility = Visibility.Visible;
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        toast._hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        // Kept while the mouse is over it, so it can be read to the end.
        if (IsMouseOver)
        {
            return;
        }

        _hideTimer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            // A new message during the fade started the timer again, and keeps it showing.
            if (!_hideTimer.IsEnabled)
            {
                Visibility = Visibility.Collapsed;
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Only hides it: the message stays on the ViewModel until the next action clears it.</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        Visibility = Visibility.Collapsed;
    }
}
