using System.Windows;
using System.Windows.Media;
using Syncfusion.Windows.Shared;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Greys out a <see cref="DoubleTextBox"/>'s text while it's read-only, so a value that can't be typed doesn't look
/// typeable: <c>controls:ReadOnlyText.IsGreyed="{Binding IsPurchaseQuantityReadOnly}"</c>, bound to the same flag as
/// <c>IsReadOnly</c> (or <c>"True"</c> for a box that always is). The grey is the app's <c>ReadOnlyForegroundBrush</c>.
/// <para>
/// A DoubleTextBox draws its text with <c>PositiveForeground</c> (negative values too, unless
/// <c>ApplyNegativeForeground</c> is on), and 0 with <c>ZeroColor</c> only once <c>ApplyZeroColor</c> is on — never
/// with <c>Foreground</c> — so those are what's set. Turning it off clears them rather than setting a colour back, so the
/// theme's own text colour returns exactly.
/// </para>
/// </summary>
public static class ReadOnlyText
{
    public static readonly DependencyProperty IsGreyedProperty = DependencyProperty.RegisterAttached(
        "IsGreyed", typeof(bool), typeof(ReadOnlyText), new PropertyMetadata(false, OnIsGreyedChanged));

    public static bool GetIsGreyed(DependencyObject element) => (bool)element.GetValue(IsGreyedProperty);

    public static void SetIsGreyed(DependencyObject element, bool value) => element.SetValue(IsGreyedProperty, value);

    /// <summary>Used only if the app's brush can't be found, as in a designer.</summary>
    private static readonly Brush FallbackBrush = CreateFallbackBrush();

    private static void OnIsGreyedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DoubleTextBox box)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            var grey = box.TryFindResource("ReadOnlyForegroundBrush") as Brush ?? FallbackBrush;
            box.PositiveForeground = grey;
            box.ZeroColor = grey;
            box.ApplyZeroColor = true;
        }
        else
        {
            box.ClearValue(DoubleTextBox.PositiveForegroundProperty);
            box.ClearValue(DoubleTextBox.ZeroColorProperty);
            box.ClearValue(DoubleTextBox.ApplyZeroColorProperty);
        }
    }

    private static SolidColorBrush CreateFallbackBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xA8, 0xA2, 0x9E));
        brush.Freeze();
        return brush;
    }
}
