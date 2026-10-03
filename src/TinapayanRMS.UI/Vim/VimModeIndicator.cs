using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// The Vim keys' mode in a window's title bar ("NORMAL", "INSERT", the key typed so far), there only while the keys are
/// on. A fixed width, so a change of text never moves anything else in the bar, and it never takes the mouse.
/// </summary>
public sealed class VimModeIndicator : TextBlock
{
    public VimModeIndicator()
    {
        Width = 96;
        Margin = new Thickness(0, 0, 8, 0);
        VerticalAlignment = VerticalAlignment.Center;
        TextAlignment = TextAlignment.Right;
        FontSize = 12;
        FontWeight = FontWeights.SemiBold;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Vim mode");
        SetResourceReference(ForegroundProperty, "MutedForegroundBrush");

        SetBinding(TextProperty, new Binding(nameof(VimState.Indicator)) { Source = VimKeys.State, Mode = BindingMode.OneWay });
        SetBinding(VisibilityProperty, new Binding(nameof(VimState.IsEnabled))
        {
            Source = VimKeys.State,
            Mode = BindingMode.OneWay,
            Converter = new BooleanToVisibilityConverter(),
        });
    }
}
