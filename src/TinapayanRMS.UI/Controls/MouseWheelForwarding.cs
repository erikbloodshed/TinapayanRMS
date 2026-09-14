using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Passes the mouse wheel on from an element that doesn't scroll to whatever holds it. An SfDataGrid
/// with no height of its own shows all its rows and has nothing to scroll, yet its own ScrollViewer
/// still takes the wheel, so a page of stacked grids (the Products recipe sections) stopped scrolling
/// whenever the pointer was over one. Set <c>controls:MouseWheelForwarding.IsEnabled="True"</c> on such a grid.
/// </summary>
public static class MouseWheelForwarding
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(MouseWheelForwarding), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= ForwardToParent;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += ForwardToParent;
        }
    }

    private static void ForwardToParent(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject element || VisualTreeHelper.GetParent(element) is not UIElement parent)
        {
            return;
        }

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }
}
