using System.Windows;
using System.Windows.Controls;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Two children side by side, sharing the width by LeadingWeight : TrailingWeight with Spacing between
/// them, or stacked (the first above the second, each at full width) when the panel is narrower than
/// Breakpoint. For a tab whose main card needs a minimum width to stay readable, such as the Products
/// recipe tables beside their summary cards. Each child keeps its own height, top-aligned.
/// </summary>
public class ResponsiveColumnsPanel : Panel
{
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LeadingWeightProperty = DependencyProperty.Register(
        nameof(LeadingWeight), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty TrailingWeightProperty = DependencyProperty.Register(
        nameof(TrailingWeight), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The narrowest width, in pixels, at which the children still sit side by side.</summary>
    public double Breakpoint
    {
        get => (double)GetValue(BreakpointProperty);
        set => SetValue(BreakpointProperty, value);
    }

    /// <summary>The gap between the children: across when side by side, down when stacked.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The first child's share of the width when side by side.</summary>
    public double LeadingWeight
    {
        get => (double)GetValue(LeadingWeightProperty);
        set => SetValue(LeadingWeightProperty, value);
    }

    /// <summary>The second child's share of the width when side by side.</summary>
    public double TrailingWeight
    {
        get => (double)GetValue(TrailingWeightProperty);
        set => SetValue(TrailingWeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (leading, trailing) = FirstTwo();
        if (leading is null || trailing is null)
        {
            leading?.Measure(availableSize);
            return leading?.DesiredSize ?? default;
        }

        var width = availableSize.Width;
        if (IsStacked(width))
        {
            var childSize = new Size(width, double.PositiveInfinity);
            leading.Measure(childSize);
            trailing.Measure(childSize);
            return new Size(
                Math.Max(leading.DesiredSize.Width, trailing.DesiredSize.Width),
                leading.DesiredSize.Height + Spacing + trailing.DesiredSize.Height);
        }

        var (leadingWidth, trailingWidth) = SplitWidth(width);
        leading.Measure(new Size(leadingWidth, availableSize.Height));
        trailing.Measure(new Size(trailingWidth, availableSize.Height));
        return new Size(
            double.IsInfinity(width) ? leading.DesiredSize.Width + Spacing + trailing.DesiredSize.Width : width,
            Math.Max(leading.DesiredSize.Height, trailing.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (leading, trailing) = FirstTwo();
        if (leading is null || trailing is null)
        {
            leading?.Arrange(new Rect(finalSize));
            return finalSize;
        }

        if (IsStacked(finalSize.Width))
        {
            leading.Arrange(new Rect(0, 0, finalSize.Width, leading.DesiredSize.Height));
            trailing.Arrange(new Rect(0, leading.DesiredSize.Height + Spacing, finalSize.Width, trailing.DesiredSize.Height));
            return finalSize;
        }

        var (leadingWidth, trailingWidth) = SplitWidth(finalSize.Width);
        leading.Arrange(new Rect(0, 0, leadingWidth, leading.DesiredSize.Height));
        trailing.Arrange(new Rect(leadingWidth + Spacing, 0, trailingWidth, trailing.DesiredSize.Height));
        return finalSize;
    }

    // An unbounded width (a horizontal ScrollViewer) never stacks: there's always room side by side.
    private bool IsStacked(double width) => !double.IsInfinity(width) && width < Breakpoint;

    private (double Leading, double Trailing) SplitWidth(double width)
    {
        var shared = Math.Max(0, width - Spacing);
        var leading = shared * LeadingWeight / (LeadingWeight + TrailingWeight);
        return (leading, shared - leading);
    }

    private (UIElement? Leading, UIElement? Trailing) FirstTwo() =>
        (InternalChildren.Count > 0 ? InternalChildren[0] : null, InternalChildren.Count > 1 ? InternalChildren[1] : null);
}
