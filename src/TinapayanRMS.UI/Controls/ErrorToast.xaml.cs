using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The hosting screen's ErrorMessage as a toast in its page's bottom-right corner. Like every
/// control here it never sets its own DataContext: it binds to the ErrorMessage every
/// ViewModelBase has. It stays until its close button is clicked or the error clears, which the
/// next action does; a new error replaces it. RunSafelyAsync clears the error before every action,
/// so the same error twice still shows twice.
/// </summary>
public partial class ErrorToast : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(ErrorToast), new PropertyMetadata(null, OnMessageChanged));

    public ErrorToast()
    {
        InitializeComponent();
        SetBinding(MessageProperty, new Binding(nameof(ViewModelBase.ErrorMessage)));
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toast = (ErrorToast)d;
        if (string.IsNullOrEmpty((string?)e.NewValue))
        {
            toast.Visibility = Visibility.Collapsed;
            return;
        }

        toast.Visibility = Visibility.Visible;
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>Only hides it: the error stays on the ViewModel until the next action clears it.</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;
}
