using System.Windows;
using System.Windows.Input;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Stops a click from scrolling the page it lands on. A control that takes focus asks the ScrollViewer around
/// it to bring all of it into view, so clicking a tall control that's partly off the page (the sub-assembly
/// form's lines table, low in its Recipe Structure tab) scrolled the page between the mouse going down and
/// coming up, and the click was lost. Set <c>controls:FocusScrolling.SkipOnClick="True"</c> on the element
/// inside the ScrollViewer: while the left button is down the request stops there, since what was clicked is
/// already on screen. Tabbing to a control still scrolls it into view.
/// </summary>
public static class FocusScrolling
{
    public static readonly DependencyProperty SkipOnClickProperty = DependencyProperty.RegisterAttached(
        "SkipOnClick", typeof(bool), typeof(FocusScrolling), new PropertyMetadata(false, OnSkipOnClickChanged));

    public static bool GetSkipOnClick(DependencyObject element) => (bool)element.GetValue(SkipOnClickProperty);

    public static void SetSkipOnClick(DependencyObject element, bool value) => element.SetValue(SkipOnClickProperty, value);

    private static void OnSkipOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.RequestBringIntoView -= SkipWhileClicking;
        if ((bool)e.NewValue)
        {
            element.RequestBringIntoView += SkipWhileClicking;
        }
    }

    private static void SkipWhileClicking(object sender, RequestBringIntoViewEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
        }
    }
}
